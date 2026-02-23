using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EmailToastUI
{
    public static class OAuthSettingsProvider
    {
        private const string SettingsFileName = "oauth.settings.json";

        public static bool TryGetProviderSettings(string provider, out OAuthProviderSettings settings, out string error)
        {
            settings = new OAuthProviderSettings();
            error = string.Empty;

            string settingsPath = Path.Combine(AppContext.BaseDirectory, SettingsFileName);
            if (!File.Exists(settingsPath))
            {
                error = $"Missing {SettingsFileName}. Copy oauth.settings.sample.json to oauth.settings.json and set Providers.{provider}.ClientId.";
                return false;
            }

            OAuthAppSettings? appSettings;
            try
            {
                string json = File.ReadAllText(settingsPath);
                appSettings = JsonSerializer.Deserialize<OAuthAppSettings>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (Exception ex)
            {
                error = $"Could not parse {SettingsFileName}: {ex.Message}";
                return false;
            }

            if (appSettings?.Providers == null || !appSettings.Providers.TryGetValue(provider, out var found) || found is null)
            {
                error = $"OAuth provider '{provider}' is not configured in {SettingsFileName}.";
                return false;
            }

            settings = found;
            if (string.IsNullOrWhiteSpace(settings.ClientId)
                || string.IsNullOrWhiteSpace(settings.AuthorizationEndpoint)
                || string.IsNullOrWhiteSpace(settings.TokenEndpoint)
                || string.IsNullOrWhiteSpace(settings.Scope))
            {
                error = $"OAuth config for '{provider}' is incomplete in {SettingsFileName}.";
                return false;
            }

            return true;
        }
    }

    public static class OAuthService
    {
        private static readonly HttpClient Http = new HttpClient();

        public static async Task<OAuthTokenInfo> AuthorizeAsync(string providerName, OAuthProviderSettings provider, CancellationToken cancellationToken = default)
        {
            int port = GetFreePort();
            string redirectUri = $"http://127.0.0.1:{port}/callback/";
            string state = Guid.NewGuid().ToString("N");
            string codeVerifier = CreateCodeVerifier();
            string codeChallenge = CreateCodeChallenge(codeVerifier);

            using var listener = new HttpListener();
            listener.Prefixes.Add(redirectUri);
            listener.Start();

            var authQuery = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["response_type"] = "code",
                ["client_id"] = provider.ClientId,
                ["redirect_uri"] = redirectUri,
                ["scope"] = provider.Scope,
                ["state"] = state,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256"
            };

            foreach (var pair in provider.AdditionalAuthorizationParameters)
            {
                authQuery[pair.Key] = pair.Value;
            }

            string authUrl = BuildUrl(provider.AuthorizationEndpoint, authQuery);
            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

            HttpListenerContext context = await listener.GetContextAsync().WaitAsync(cancellationToken);
            var query = ParseQueryString(context.Request.Url?.Query ?? string.Empty);

            if (!query.TryGetValue("state", out var returnedState) || !string.Equals(returnedState, state, StringComparison.Ordinal))
            {
                await WriteBrowserResponseAsync(context.Response, "OAuth failed: state mismatch. You can close this tab.");
                throw new InvalidOperationException("OAuth state mismatch.");
            }

            if (query.TryGetValue("error", out var oauthError) && !string.IsNullOrWhiteSpace(oauthError))
            {
                await WriteBrowserResponseAsync(context.Response, $"OAuth failed: {oauthError}. You can close this tab.");
                throw new InvalidOperationException($"OAuth authorization failed: {oauthError}");
            }

            if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            {
                await WriteBrowserResponseAsync(context.Response, "OAuth failed: missing authorization code. You can close this tab.");
                throw new InvalidOperationException("Authorization code was not returned by provider.");
            }

            await WriteBrowserResponseAsync(context.Response, "Sign-in complete. You can close this tab and return to the app.");

            var tokenForm = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = provider.ClientId,
                ["code_verifier"] = codeVerifier,
                ["scope"] = provider.Scope
            };

            if (!string.IsNullOrWhiteSpace(provider.ClientSecret))
            {
                tokenForm["client_secret"] = provider.ClientSecret;
            }

            using var tokenResponse = await Http.PostAsync(provider.TokenEndpoint, new FormUrlEncodedContent(tokenForm), cancellationToken);
            string tokenPayload = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Token exchange failed ({(int)tokenResponse.StatusCode}): {tokenPayload}");
            }

            OAuthTokenInfo tokenInfo = ParseTokenPayload(tokenPayload, provider.TokenEndpoint, provider.ClientId, provider.ClientSecret, provider.Scope);
            tokenInfo.AccountEmail = await ResolveAccountEmailAsync(providerName, tokenInfo, cancellationToken);
            return tokenInfo;
        }

        public static async Task<string> EnsureValidAccessTokenAsync(EmailAccountConfig config, CancellationToken cancellationToken = default)
        {
            if (config.OAuthToken is null)
            {
                throw new InvalidOperationException("OAuth token data is missing.");
            }

            if (config.OAuthToken.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(2)
                && !string.IsNullOrWhiteSpace(config.OAuthToken.AccessToken))
            {
                return config.OAuthToken.AccessToken;
            }

            if (string.IsNullOrWhiteSpace(config.OAuthToken.RefreshToken))
            {
                throw new InvalidOperationException("OAuth refresh token missing. Reconnect Outlook.");
            }

            var refreshForm = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = config.OAuthToken.RefreshToken,
                ["client_id"] = config.OAuthToken.ClientId,
                ["scope"] = config.OAuthToken.Scope
            };

            if (!string.IsNullOrWhiteSpace(config.OAuthToken.ClientSecret))
            {
                refreshForm["client_secret"] = config.OAuthToken.ClientSecret;
            }

            using var tokenResponse = await Http.PostAsync(config.OAuthToken.TokenEndpoint, new FormUrlEncodedContent(refreshForm), cancellationToken);
            string tokenPayload = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Token refresh failed ({(int)tokenResponse.StatusCode}): {tokenPayload}");
            }

            OAuthTokenInfo refreshed = ParseTokenPayload(
                tokenPayload,
                config.OAuthToken.TokenEndpoint,
                config.OAuthToken.ClientId,
                config.OAuthToken.ClientSecret,
                config.OAuthToken.Scope);

            config.OAuthToken.AccessToken = refreshed.AccessToken;
            if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
            {
                config.OAuthToken.RefreshToken = refreshed.RefreshToken;
            }
            config.OAuthToken.ExpiresAtUtc = refreshed.ExpiresAtUtc;
            if (!string.IsNullOrWhiteSpace(refreshed.AccountEmail))
            {
                config.OAuthToken.AccountEmail = refreshed.AccountEmail;
            }

            return config.OAuthToken.AccessToken;
        }

        private static OAuthTokenInfo ParseTokenPayload(string payload, string tokenEndpoint, string clientId, string clientSecret, string scope)
        {
            using JsonDocument doc = JsonDocument.Parse(payload);
            JsonElement root = doc.RootElement;

            string accessToken = root.TryGetProperty("access_token", out JsonElement accessEl)
                ? accessEl.GetString() ?? string.Empty
                : string.Empty;
            string refreshToken = root.TryGetProperty("refresh_token", out JsonElement refreshEl)
                ? refreshEl.GetString() ?? string.Empty
                : string.Empty;
            int expiresIn = root.TryGetProperty("expires_in", out JsonElement expiresEl) && expiresEl.TryGetInt32(out int value)
                ? value
                : 3600;
            string idToken = root.TryGetProperty("id_token", out JsonElement idTokenEl)
                ? idTokenEl.GetString() ?? string.Empty
                : string.Empty;

            if (root.TryGetProperty("scope", out JsonElement scopeEl))
            {
                scope = scopeEl.GetString() ?? scope;
            }

            string accountEmail = TryGetEmailFromIdToken(idToken);

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("OAuth provider did not return access_token.");
            }

            return new OAuthTokenInfo
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn)),
                TokenEndpoint = tokenEndpoint,
                ClientId = clientId,
                ClientSecret = clientSecret,
                Scope = scope,
                AccountEmail = accountEmail
            };
        }

        private static string TryGetEmailFromIdToken(string idToken)
        {
            if (string.IsNullOrWhiteSpace(idToken))
            {
                return string.Empty;
            }

            string[] parts = idToken.Split('.');
            if (parts.Length < 2)
            {
                return string.Empty;
            }

            try
            {
                string payload = parts[1].Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2:
                        payload += "==";
                        break;
                    case 3:
                        payload += "=";
                        break;
                }

                byte[] bytes = Convert.FromBase64String(payload);
                using JsonDocument claims = JsonDocument.Parse(bytes);
                JsonElement root = claims.RootElement;

                if (root.TryGetProperty("email", out JsonElement emailEl))
                {
                    return emailEl.GetString() ?? string.Empty;
                }

                if (root.TryGetProperty("preferred_username", out JsonElement preferredEl))
                {
                    return preferredEl.GetString() ?? string.Empty;
                }

                if (root.TryGetProperty("upn", out JsonElement upnEl))
                {
                    return upnEl.GetString() ?? string.Empty;
                }
            }
            catch
            {
                // best effort only
            }

            return string.Empty;
        }

        private static async Task WriteBrowserResponseAsync(HttpListenerResponse response, string message)
        {
            string html = $"<html><body style='font-family:Segoe UI; padding:24px; background:#111; color:#eee;'><h2>{WebUtility.HtmlEncode(message)}</h2></body></html>";
            byte[] bytes = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html";
            response.ContentEncoding = Encoding.UTF8;
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            response.Close();
        }

        private static string BuildUrl(string endpoint, Dictionary<string, string> query)
        {
            string queryString = string.Join("&", query.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
            char separator = endpoint.Contains('?') ? '&' : '?';
            return endpoint + separator + queryString;
        }

        private static Dictionary<string, string> ParseQueryString(string query)
        {
            if (query.StartsWith("?", StringComparison.Ordinal))
            {
                query = query[1..];
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(query))
            {
                return result;
            }

            foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int idx = part.IndexOf('=');
                if (idx < 0)
                {
                    result[Uri.UnescapeDataString(part)] = string.Empty;
                    continue;
                }

                string key = Uri.UnescapeDataString(part[..idx]);
                string value = Uri.UnescapeDataString(part[(idx + 1)..]);
                result[key] = value;
            }

            return result;
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static string CreateCodeVerifier()
        {
            byte[] data = RandomNumberGenerator.GetBytes(64);
            return Base64UrlEncode(data);
        }

        private static string CreateCodeChallenge(string verifier)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.ASCII.GetBytes(verifier));
            return Base64UrlEncode(hash);
        }

        private static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static async Task<string> ResolveAccountEmailAsync(string providerName, OAuthTokenInfo tokenInfo, CancellationToken cancellationToken)
        {
            string fromToken = tokenInfo.AccountEmail ?? string.Empty;

            if (!string.Equals(providerName, "Gmail", StringComparison.OrdinalIgnoreCase))
            {
                return fromToken;
            }

            // Gmail OAuth sometimes returns claims only from userinfo despite id_token.
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenInfo.AccessToken);
                using var res = await Http.SendAsync(req, cancellationToken);
                if (res.IsSuccessStatusCode)
                {
                    string json = await res.Content.ReadAsStringAsync(cancellationToken);
                    using JsonDocument doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("email", out JsonElement emailEl))
                    {
                        string? email = emailEl.GetString();
                        if (!string.IsNullOrWhiteSpace(email))
                        {
                            return email.Trim();
                        }
                    }
                }
            }
            catch
            {
                // best effort only
            }

            return fromToken;
        }
    }
}
