using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace EmailToastUI
{
    public partial class ConnectAccountWindow : Window
    {
        private const string GmailClientIdUrl = "https://console.cloud.google.com/apis/credentials";
        private const string OutlookClientIdUrl = "https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade";
        private const string OAuthSettingsFileName = "oauth.settings.json";

        public EmailAccountConfig? AccountConfig { get; private set; }

        private readonly string _provider;
        private OAuthTokenInfo? _oauthToken;
        private string _imapHost = string.Empty;
        private int _imapPort = 993;
        private bool _useSsl = true;

        public ConnectAccountWindow(string provider)
        {
            InitializeComponent();
            _provider = provider;

            TitleText.Text = $"Connect {provider}";
            ApplyProviderDefaults(provider);
        }

        private void ApplyProviderDefaults(string provider)
        {
            switch (provider)
            {
                case "Gmail":
                    _imapHost = "imap.gmail.com";
                    _imapPort = 993;
                    _useSsl = true;
                    HelpText.Text = "Sign in with Google OAuth to connect Gmail.";
                    OAuthSignInButton.Visibility = Visibility.Visible;
                    OAuthSignInButton.Content = "Sign in with Google (OAuth)";
                    OAuthSetupButton.Visibility = Visibility.Visible;
                    OAuthSetupButton.Content = "Get Google Client ID";
                    OAuthHintText.Visibility = Visibility.Visible;
                    UpdateConfigStatusText();
                    break;
                case "Outlook":
                    _imapHost = "imap-mail.outlook.com";
                    _imapPort = 993;
                    _useSsl = true;
                    HelpText.Text = "Sign in with Microsoft OAuth to connect Outlook/Hotmail.";
                    OAuthSignInButton.Visibility = Visibility.Visible;
                    OAuthSignInButton.Content = "Sign in with Microsoft (OAuth)";
                    OAuthSetupButton.Visibility = Visibility.Visible;
                    OAuthSetupButton.Content = "Get Microsoft Client ID";
                    OAuthHintText.Visibility = Visibility.Visible;
                    UpdateConfigStatusText();
                    break;
                default:
                    _imapHost = string.Empty;
                    _imapPort = 993;
                    _useSsl = true;
                    OAuthSignInButton.Visibility = Visibility.Collapsed;
                    OAuthSetupButton.Visibility = Visibility.Collapsed;
                    OAuthHintText.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private async void OAuthSignInButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.Equals(_provider, "Outlook", StringComparison.Ordinal)
                && !string.Equals(_provider, "Gmail", StringComparison.Ordinal))
            {
                return;
            }

            if (!OAuthSettingsProvider.TryGetProviderSettings(_provider, out var settings, out string settingsError))
            {
                MessageBox.Show(
                    $"{settingsError}\n\nConfig file:\n{GetOAuthSettingsPath()}\n\nUse \"{OAuthSetupButton.Content}\" to create/find your client ID, paste it in oauth.settings.json, then click Sign in again.",
                    "OAuth Not Configured",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                OAuthSignInButton.IsEnabled = false;
                OAuthStatusText.Visibility = Visibility.Visible;
                OAuthStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD2DA"));
                OAuthStatusText.Text = $"Opening browser for {_provider} sign-in...";

                _oauthToken = await OAuthService.AuthorizeAsync(_provider, settings);
                string resolvedEmail = _oauthToken.AccountEmail;
                if (string.IsNullOrWhiteSpace(resolvedEmail))
                {
                    SignedInText.Visibility = Visibility.Collapsed;
                    OAuthStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#77C890"));
                    OAuthStatusText.Text = "OAuth sign-in complete. Click Connect to continue.";
                }
                else
                {
                    SignedInText.Text = $"Signed in as: {resolvedEmail}";
                    SignedInText.Visibility = Visibility.Visible;
                    OAuthStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#77C890"));
                    OAuthStatusText.Text = "OAuth sign-in complete. Click Connect to continue.";
                }
            }
            catch (Exception ex)
            {
                _oauthToken = null;
                OAuthStatusText.Visibility = Visibility.Collapsed;
                SignedInText.Visibility = Visibility.Collapsed;
                MessageBox.Show($"OAuth sign-in failed.\n\n{ex.Message}", "OAuth Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                OAuthSignInButton.IsEnabled = true;
            }
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_imapHost))
            {
                MessageBox.Show("Provider IMAP settings are not configured.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_oauthToken is null)
            {
                MessageBox.Show("Please complete OAuth sign-in first.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.Equals(_provider, "Gmail", StringComparison.Ordinal)
                && !(_oauthToken.Scope?.Contains("https://mail.google.com/", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                MessageBox.Show(
                    "Gmail OAuth token is missing IMAP scope.\n\n" +
                    "Required scope: https://mail.google.com/\n" +
                    "Update oauth.settings.json for Gmail and sign in again.",
                    "Validation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string email = _oauthToken.AccountEmail;
            if (string.IsNullOrWhiteSpace(email))
            {
                MessageBox.Show(
                    "OAuth succeeded but no email/username claim was returned.\n\n" +
                    "Add openid/email scopes for this provider in oauth.settings.json, then sign in again.",
                    "Validation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            AccountConfig = new EmailAccountConfig
            {
                Provider = _provider,
                EmailAddress = email,
                Username = email,
                Password = string.Empty,
                ImapHost = _imapHost,
                ImapPort = _imapPort,
                UseSsl = _useSsl,
                AuthMode = EmailAuthMode.OAuth,
                OAuthToken = _oauthToken
            };

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OAuthSetupButton_Click(object sender, RoutedEventArgs e)
        {
            if (OAuthSettingsProvider.TryGetProviderSettings(_provider, out _, out _))
            {
                MessageBox.Show(
                    $"OAuth credentials for {_provider} are already configured in:\n{GetOAuthSettingsPath()}\n\nClick the Sign in button to continue.",
                    "OAuth Already Configured",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            OpenProviderSetupPage();
        }

        private void UpdateConfigStatusText()
        {
            if (OAuthSettingsProvider.TryGetProviderSettings(_provider, out _, out _))
            {
                OAuthStatusText.Text = $"OAuth credentials detected in {OAuthSettingsFileName}.";
                OAuthStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#77C890"));
                OAuthStatusText.Visibility = Visibility.Visible;
                return;
            }

            OAuthStatusText.Text = $"OAuth credentials not configured. Fill {OAuthSettingsFileName} first.";
            OAuthStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D9A66B"));
            OAuthStatusText.Visibility = Visibility.Visible;
        }

        private static string GetOAuthSettingsPath()
        {
            return Path.Combine(AppContext.BaseDirectory, OAuthSettingsFileName);
        }

        private void OpenProviderSetupPage()
        {
            string? url = _provider switch
            {
                "Gmail" => GmailClientIdUrl,
                "Outlook" => OutlookClientIdUrl,
                _ => null
            };

            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open setup URL.\n\n{ex.Message}", "Open Setup Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
