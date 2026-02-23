using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace EmailToastUI
{
    public enum EmailAuthMode
    {
        Password = 0,
        OAuth = 1
    }

    public class OAuthTokenInfo
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public string TokenEndpoint { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string AccountEmail { get; set; } = string.Empty;
    }

    public class OAuthProviderSettings
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string AuthorizationEndpoint { get; set; } = string.Empty;
        public string TokenEndpoint { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public Dictionary<string, string> AdditionalAuthorizationParameters { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public class OAuthAppSettings
    {
        public Dictionary<string, OAuthProviderSettings> Providers { get; set; } = new Dictionary<string, OAuthProviderSettings>(StringComparer.OrdinalIgnoreCase);
    }

    public class EmailItem : INotifyPropertyChanged
    {
        public string Provider { get; set; } = string.Empty;
        public string Sender { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Preview { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
        public string IconLetter { get; set; } = string.Empty;
        public SolidColorBrush ProviderBrush { get; set; } = new SolidColorBrush(Colors.Gray);

        private bool _isWarning;
        public bool IsWarning
        {
            get => _isWarning;
            set
            {
                if (_isWarning != value)
                {
                    _isWarning = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsWarning)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WarningVisibility)));
                }
            }
        }

        public Visibility WarningVisibility => _isWarning ? Visibility.Visible : Visibility.Collapsed;

        private Visibility _visibility = Visibility.Visible;
        public Visibility Visibility
        {
            get => _visibility;
            set
            {
                if (_visibility != value)
                {
                    _visibility = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Visibility)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class FilterItem : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;

        private SolidColorBrush _backgroundBrush = Brushes.Transparent;
        public SolidColorBrush BackgroundBrush
        {
            get => _backgroundBrush;
            set
            {
                if (_backgroundBrush != value)
                {
                    _backgroundBrush = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BackgroundBrush)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class EmailAccountConfig
    {
        public string Provider { get; init; } = string.Empty;
        public string EmailAddress { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string ImapHost { get; init; } = string.Empty;
        public int ImapPort { get; init; } = 993;
        public bool UseSsl { get; init; } = true;
        public int PollIntervalSeconds { get; init; } = 25;
        public EmailAuthMode AuthMode { get; init; } = EmailAuthMode.Password;
        public OAuthTokenInfo? OAuthToken { get; init; }
    }

    public class EmailMatchedEventArgs : EventArgs
    {
        public required string Provider { get; init; }
        public required string Sender { get; init; }
        public required string Subject { get; init; }
        public required string Preview { get; init; }
        public required DateTimeOffset ReceivedAt { get; init; }
        public required bool IsWarning { get; init; }
    }

    public class EmailMonitorErrorEventArgs : EventArgs
    {
        public required string Provider { get; init; }
        public required Exception Exception { get; init; }
    }
}
