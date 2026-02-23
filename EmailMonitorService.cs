using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

namespace EmailToastUI
{
    public sealed class EmailMonitorService : IDisposable
    {
        private sealed class AccountState
        {
            public required EmailAccountConfig Config { get; init; }
            public required CancellationTokenSource Cts { get; init; }
            public required Task MonitorTask { get; set; }
            public required HashSet<uint> SeenUids { get; init; }
            public required Queue<uint> SeenOrder { get; init; }
            public required SemaphoreSlim PollGate { get; init; }
        }

        private readonly Func<IReadOnlyList<string>> _keywordProvider;
        private readonly ConcurrentDictionary<string, AccountState> _accounts = new(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<EmailMatchedEventArgs>? EmailMatched;
        public event EventHandler<EmailMonitorErrorEventArgs>? MonitorError;

        public EmailMonitorService(Func<IReadOnlyList<string>> keywordProvider)
        {
            _keywordProvider = keywordProvider;
        }

        public async Task<EmailAccountConfig> ResolveAndVerifyConnectionAsync(EmailAccountConfig config, CancellationToken cancellationToken = default)
        {
            Exception? last = null;

            foreach (var candidate in GetVerificationCandidates(config))
            {
                try
                {
                    await VerifySingleConnectionAsync(candidate, cancellationToken);
                    return candidate;
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }

            throw last ?? new InvalidOperationException("IMAP verification failed.");
        }

        public async Task VerifyConnectionAsync(EmailAccountConfig config, CancellationToken cancellationToken = default)
        {
            await VerifySingleConnectionAsync(config, cancellationToken);
        }

        private static async Task VerifySingleConnectionAsync(EmailAccountConfig config, CancellationToken cancellationToken)
        {
            using var client = new ImapClient();
            await client.ConnectAsync(config.ImapHost, config.ImapPort, config.UseSsl, cancellationToken);
            await AuthenticateAsync(client, config, cancellationToken);
            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }

        public async Task AddOrReplaceAccountAsync(EmailAccountConfig config, CancellationToken cancellationToken = default)
        {
            await RemoveAccountAsync(config.Provider);

            var state = new AccountState
            {
                Config = config,
                Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken),
                MonitorTask = Task.CompletedTask,
                SeenUids = new HashSet<uint>(),
                SeenOrder = new Queue<uint>(),
                PollGate = new SemaphoreSlim(1, 1)
            };

            _accounts[config.Provider] = state;

            // Prime recent UID list so old mail does not flood as "new".
            await PollSingleAccountAsync(state, emitMatches: false, state.Cts.Token);

            state.MonitorTask = Task.Run(() => MonitorLoopAsync(state), state.Cts.Token);
        }

        public async Task RemoveAccountAsync(string provider)
        {
            if (!_accounts.TryRemove(provider, out var state))
            {
                return;
            }

            state.Cts.Cancel();
            try
            {
                await state.MonitorTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
            finally
            {
                state.Cts.Dispose();
                state.PollGate.Dispose();
            }
        }

        public async Task<int> CheckNowAsync(CancellationToken cancellationToken = default)
        {
            int matches = 0;
            foreach (var state in _accounts.Values)
            {
                matches += await PollSingleAccountAsync(state, emitMatches: true, cancellationToken);
            }

            return matches;
        }

        private async Task MonitorLoopAsync(AccountState state)
        {
            while (!state.Cts.IsCancellationRequested)
            {
                try
                {
                    await PollSingleAccountAsync(state, emitMatches: true, state.Cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    MonitorError?.Invoke(this, new EmailMonitorErrorEventArgs
                    {
                        Provider = state.Config.Provider,
                        Exception = ex
                    });
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, state.Config.PollIntervalSeconds)), state.Cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task<int> PollSingleAccountAsync(AccountState state, bool emitMatches, CancellationToken cancellationToken)
        {
            await state.PollGate.WaitAsync(cancellationToken);
            try
            {
                using var client = new ImapClient();
                await client.ConnectAsync(state.Config.ImapHost, state.Config.ImapPort, state.Config.UseSsl, cancellationToken);
                await AuthenticateAsync(client, state.Config, cancellationToken);

                var inbox = client.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

                var recentUids = await inbox.SearchAsync(SearchQuery.DeliveredAfter(DateTime.UtcNow.AddDays(-2)), cancellationToken);
                if (recentUids.Count == 0)
                {
                    await client.DisconnectAsync(true, cancellationToken);
                    return 0;
                }

                var latestUids = recentUids.Skip(Math.Max(0, recentUids.Count - 60)).ToList();
                var newUids = latestUids.Where(uid => !state.SeenUids.Contains(uid.Id)).ToList();
                if (newUids.Count == 0)
                {
                    await client.DisconnectAsync(true, cancellationToken);
                    return 0;
                }

                var summaries = await inbox.FetchAsync(
                    newUids,
                    MessageSummaryItems.Envelope | MessageSummaryItems.InternalDate | MessageSummaryItems.UniqueId,
                    cancellationToken);

                var keywords = _keywordProvider()
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Select(k => k.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int emailsFound = 0;
                foreach (var summary in summaries.OrderBy(s => s.UniqueId))
                {
                    TrackSeenUid(state, summary.UniqueId.Id);

                    string? subject = summary.Envelope?.Subject?.Trim();
                    if (string.IsNullOrWhiteSpace(subject))
                    {
                        subject = "(No subject)";
                    }

                    var mailbox = summary.Envelope?.From?.Mailboxes.FirstOrDefault();
                    string? sender = mailbox?.Name;
                    if (string.IsNullOrWhiteSpace(sender))
                    {
                        sender = mailbox?.Address ?? "Unknown sender";
                    }

                    DateTimeOffset receivedAt = summary.Envelope?.Date
                        ?? summary.InternalDate
                        ?? DateTimeOffset.Now;

                    if (!emitMatches)
                    {
                        continue;
                    }

                    bool isWarning = keywords.Any(keyword =>
                            subject.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                            || sender.Contains(keyword, StringComparison.OrdinalIgnoreCase));

                    emailsFound++;
                    EmailMatched?.Invoke(this, new EmailMatchedEventArgs
                    {
                        Provider = state.Config.Provider,
                        Sender = sender,
                        Subject = subject,
                        Preview = isWarning
                            ? $"Warning keyword matched in {state.Config.EmailAddress}."
                            : $"New email in {state.Config.EmailAddress}.",
                        ReceivedAt = receivedAt,
                        IsWarning = isWarning
                    });
                }

                await client.DisconnectAsync(true, cancellationToken);
                return emailsFound;
            }
            finally
            {
                state.PollGate.Release();
            }
        }

        private static async Task AuthenticateAsync(ImapClient client, EmailAccountConfig config, CancellationToken cancellationToken)
        {
            if (config.AuthMode == EmailAuthMode.OAuth)
            {
                string username = string.IsNullOrWhiteSpace(config.Username) ? config.EmailAddress : config.Username;
                string accessToken = await OAuthService.EnsureValidAccessTokenAsync(config, cancellationToken);

                client.AuthenticationMechanisms.Remove("PLAIN");
                var oauth2 = new SaslMechanismOAuth2(username, accessToken);
                await client.AuthenticateAsync(oauth2, cancellationToken);
                return;
            }

            await AuthenticatePasswordAsync(client, config.Username, config.Password, cancellationToken);
        }

        private static async Task AuthenticatePasswordAsync(ImapClient client, string username, string password, CancellationToken cancellationToken)
        {
            // Force password auth path for IMAP app-password logins.
            client.AuthenticationMechanisms.Remove("XOAUTH2");
            client.AuthenticationMechanisms.Remove("OAUTHBEARER");

            await client.AuthenticateAsync(username, password, cancellationToken);
        }

        private static IEnumerable<EmailAccountConfig> GetVerificationCandidates(EmailAccountConfig config)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            IEnumerable<string> hosts = GetHostCandidates(config.Provider, config.ImapHost);
            IEnumerable<string> usernames = GetUsernameCandidates(config.Username, config.EmailAddress);

            foreach (string host in hosts)
            {
                foreach (string username in usernames)
                {
                    string key = $"{host}|{config.ImapPort}|{config.UseSsl}|{username}";
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    yield return new EmailAccountConfig
                    {
                        Provider = config.Provider,
                        EmailAddress = config.EmailAddress,
                        Username = username,
                        Password = config.Password,
                        ImapHost = host,
                        ImapPort = config.ImapPort,
                        UseSsl = config.UseSsl,
                        PollIntervalSeconds = config.PollIntervalSeconds,
                        AuthMode = config.AuthMode,
                        OAuthToken = config.OAuthToken
                    };
                }
            }
        }

        private static IEnumerable<string> GetHostCandidates(string provider, string currentHost)
        {
            yield return currentHost;

            if (provider == "Outlook")
            {
                if (!string.Equals(currentHost, "outlook.office365.com", StringComparison.OrdinalIgnoreCase))
                {
                    yield return "outlook.office365.com";
                }

                if (!string.Equals(currentHost, "imap-mail.outlook.com", StringComparison.OrdinalIgnoreCase))
                {
                    yield return "imap-mail.outlook.com";
                }
            }
        }

        private static IEnumerable<string> GetUsernameCandidates(string username, string email)
        {
            if (!string.IsNullOrWhiteSpace(username))
            {
                yield return username.Trim();
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                string trimmedEmail = email.Trim();
                if (!string.Equals(trimmedEmail, username?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    yield return trimmedEmail;
                }
            }
        }

        private static void TrackSeenUid(AccountState state, uint uid)
        {
            if (!state.SeenUids.Add(uid))
            {
                return;
            }

            state.SeenOrder.Enqueue(uid);
            while (state.SeenOrder.Count > 2000)
            {
                uint oldest = state.SeenOrder.Dequeue();
                state.SeenUids.Remove(oldest);
            }
        }

        public void Dispose()
        {
            foreach (var provider in _accounts.Keys.ToList())
            {
                RemoveAccountAsync(provider).GetAwaiter().GetResult();
            }
        }
    }
}
