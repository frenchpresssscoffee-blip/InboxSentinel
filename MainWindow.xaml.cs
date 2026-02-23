using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace EmailToastUI
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<string> ConnectedAccounts { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<string> ActiveKeywords { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<EmailItem> DashboardEmails { get; set; } = new ObservableCollection<EmailItem>();
        public ObservableCollection<FilterItem> Filters { get; set; } = new ObservableCollection<FilterItem>();

        private readonly Dictionary<string, EmailAccountConfig> _accountConfigs = new(StringComparer.OrdinalIgnoreCase);
        private readonly EmailMonitorService _monitorService;
        private string _currentFilter = "All";

        public MainWindow()
        {
            InitializeComponent();

            _monitorService = new EmailMonitorService(() => ActiveKeywords.ToList());
            _monitorService.EmailMatched += MonitorService_EmailMatched;
            _monitorService.MonitorError += MonitorService_MonitorError;

            KeywordsList.ItemsSource = ActiveKeywords;
            FilterPanel.ItemsSource = Filters;
            EmailsList.ItemsSource = DashboardEmails;

            ActiveKeywords.Add("Invoice");
            ActiveKeywords.Add("Payment");
            ActiveKeywords.Add("Password Reset");
            ActiveKeywords.Add("Urgent");
            ActiveKeywords.Add("Security");

            Filters.Add(new FilterItem
            {
                Name = "All",
                BackgroundBrush = BrushFromHex("#1A2A4C")
            });

            ConnectedAccounts.CollectionChanged += (_, _) => UpdateEmailsView();
            DashboardEmails.CollectionChanged += (_, _) => UpdateEmailsView();
            Closed += MainWindow_Closed;

            SetCurrentFilter("All");
            ShowAccountsView();
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _monitorService.EmailMatched -= MonitorService_EmailMatched;
            _monitorService.MonitorError -= MonitorService_MonitorError;
            _monitorService.Dispose();
        }

        private static SolidColorBrush BrushFromHex(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        private static (SolidColorBrush ProviderBrush, string IconLetter) GetProviderStyle(string provider)
        {
            return provider switch
            {
                "Gmail" => (BrushFromHex("#EA4335"), "G"),
                "Outlook" => (BrushFromHex("#0078D4"), "O"),
                _ => (new SolidColorBrush(Colors.Gray), string.IsNullOrWhiteSpace(provider) ? "?" : provider.Substring(0, 1))
            };
        }

        private static string BuildSignInErrorMessage(string provider, Exception ex, EmailAccountConfig? config)
        {
            if (provider == "Gmail" && config?.AuthMode == EmailAuthMode.OAuth)
            {
                return
                    "Sign-in failed for Gmail (OAuth).\n\n" +
                    $"{ex.Message}\n\n" +
                    $"Signed-in account: {config?.EmailAddress}\n\n" +
                    "Checklist:\n" +
                    "1. oauth.settings.json Gmail scope includes: openid email https://mail.google.com/\n" +
                    "2. Gmail IMAP is enabled in Gmail settings.\n" +
                    "3. The Google account has an actual Gmail/Workspace mailbox.";
            }

            if (provider == "Outlook" && config?.AuthMode == EmailAuthMode.OAuth)
            {
                return
                    "Sign-in failed for Outlook (OAuth).\n\n" +
                    $"{ex.Message}\n\n" +
                    "Verify OAuth is configured in oauth.settings.json and that your Microsoft app allows scopes:\n" +
                    "openid email offline_access https://outlook.office.com/IMAP.AccessAsUser.All";
            }

            return $"Sign-in failed for {provider}.\n\n{ex.Message}";
        }

        private void MonitorService_EmailMatched(object? sender, EmailMatchedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                InsertEmail(
                    e.Provider,
                    e.Sender,
                    e.Subject,
                    e.Preview,
                    e.ReceivedAt.ToLocalTime().ToString("h:mm tt"),
                    e.IsWarning);
                ToastManager.ShowToast(e.Provider, e.Sender, e.Subject, e.Preview);
            });
        }

        private void MonitorService_MonitorError(object? sender, EmailMonitorErrorEventArgs e)
        {
            App.LogException($"MonitorError[{e.Provider}]", e.Exception);
        }

        private void InsertEmail(string provider, string sender, string subject, string preview, string time, bool isWarning)
        {
            (SolidColorBrush providerBrush, string iconLetter) = GetProviderStyle(provider);

            DashboardEmails.Insert(0, new EmailItem
            {
                Provider = provider,
                Sender = sender,
                Subject = subject,
                Preview = preview,
                Time = time,
                ProviderBrush = providerBrush,
                IconLetter = iconLetter,
                IsWarning = isWarning
            });
        }

        private void UpdateEmailsView()
        {
            if (DashboardEmails.Count == 0 && ConnectedAccounts.Count > 0)
            {
                NoEmailsText.Text = "No recent emails yet.";
            }
            else if (ConnectedAccounts.Count == 0)
            {
                NoEmailsText.Text = "No accounts connected. Go to Connect Accounts tab.";
            }

            NoEmailsText.Visibility = DashboardEmails.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var email in DashboardEmails)
            {
                bool showItem = string.Equals(_currentFilter, "All", StringComparison.Ordinal)
                    || string.Equals(email.Provider, _currentFilter, StringComparison.Ordinal);
                email.Visibility = showItem ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void SetCurrentFilter(string filterName)
        {
            if (!Filters.Any(f => string.Equals(f.Name, filterName, StringComparison.Ordinal)))
            {
                filterName = "All";
            }

            _currentFilter = filterName;
            foreach (var filter in Filters)
            {
                filter.BackgroundBrush = string.Equals(filter.Name, filterName, StringComparison.Ordinal)
                    ? BrushFromHex("#1A2A4C")
                    : Brushes.Transparent;
            }

            UpdateEmailsView();
        }

        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.Tag is string filterName)
            {
                SetCurrentFilter(filterName);
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseApp_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void ShowAccountsView()
        {
            ViewAccounts.Visibility = Visibility.Visible;
            ViewDashboard.Visibility = Visibility.Collapsed;
            ViewSettings.Visibility = Visibility.Collapsed;

            NavAccounts.Background = BrushFromHex("#1A2A4C");
            NavAccounts.Foreground = BrushFromHex("#4A80F6");
            NavDashboard.Background = Brushes.Transparent;
            NavDashboard.Foreground = BrushFromHex("#8A8A93");
            NavSettings.Background = Brushes.Transparent;
            NavSettings.Foreground = BrushFromHex("#8A8A93");
        }

        private void ShowDashboardView()
        {
            ViewAccounts.Visibility = Visibility.Collapsed;
            ViewDashboard.Visibility = Visibility.Visible;
            ViewSettings.Visibility = Visibility.Collapsed;

            NavDashboard.Background = BrushFromHex("#1A2A4C");
            NavDashboard.Foreground = BrushFromHex("#4A80F6");
            NavAccounts.Background = Brushes.Transparent;
            NavAccounts.Foreground = BrushFromHex("#8A8A93");
            NavSettings.Background = Brushes.Transparent;
            NavSettings.Foreground = BrushFromHex("#8A8A93");
        }

        private void ShowSettingsView()
        {
            ViewAccounts.Visibility = Visibility.Collapsed;
            ViewDashboard.Visibility = Visibility.Collapsed;
            ViewSettings.Visibility = Visibility.Visible;

            NavSettings.Background = BrushFromHex("#1A2A4C");
            NavSettings.Foreground = BrushFromHex("#4A80F6");
            NavDashboard.Background = Brushes.Transparent;
            NavDashboard.Foreground = BrushFromHex("#8A8A93");
            NavAccounts.Background = Brushes.Transparent;
            NavAccounts.Foreground = BrushFromHex("#8A8A93");
        }

        // --- Navigation ---
        private void NavAccounts_Click(object sender, RoutedEventArgs e)
        {
            ShowAccountsView();
        }

        private void NavDashboard_Click(object sender, RoutedEventArgs e)
        {
            ShowDashboardView();
        }

        private void NavSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsView();
        }

        // --- Accounts / Login ---
        private async void LoginProvider_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement btn || btn.Tag is not string providerName)
            {
                return;
            }

            bool alreadyConnected = ConnectedAccounts.Contains(providerName);
            if (alreadyConnected)
            {
                var replace = MessageBox.Show(
                    $"{providerName} is already connected. Replace credentials?",
                    "Reconnect Account",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (replace != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var connectWindow = new ConnectAccountWindow(providerName)
            {
                Owner = this
            };

            bool? connectResult = connectWindow.ShowDialog();
            if (connectResult != true || connectWindow.AccountConfig is null)
            {
                return;
            }

            ModalText.Text = $"Connecting to {providerName}...";
            ModalOverlay.Visibility = Visibility.Visible;

            try
            {
                EmailAccountConfig resolvedConfig = await _monitorService.ResolveAndVerifyConnectionAsync(connectWindow.AccountConfig);
                await _monitorService.AddOrReplaceAccountAsync(resolvedConfig);

                _accountConfigs[providerName] = resolvedConfig;

                if (!alreadyConnected)
                {
                    ConnectedAccounts.Add(providerName);
                    if (!Filters.Any(f => string.Equals(f.Name, providerName, StringComparison.Ordinal)))
                    {
                        Filters.Add(new FilterItem { Name = providerName, BackgroundBrush = Brushes.Transparent });
                    }
                }

                ToastManager.ShowToast(providerName, "Account", $"Connected to {providerName}", $"Monitoring started for {resolvedConfig.EmailAddress}.");
                SetCurrentFilter(providerName);
                ShowDashboardView();
            }
            catch (Exception ex)
            {
                App.LogException("LoginProvider_Click", ex);
                string message = BuildSignInErrorMessage(providerName, ex, connectWindow.AccountConfig);
                MessageBox.Show(message, "Sign-In Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ModalOverlay.Visibility = Visibility.Collapsed;
            }
        }

        // --- Keywords ---
        private void AddKeyword_Click(object sender, RoutedEventArgs e)
        {
            AddKeyword();
        }

        private void txtKeyword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddKeyword();
            }
        }

        private void AddKeyword()
        {
            string kw = txtKeyword.Text.Trim();
            if (string.IsNullOrEmpty(kw))
            {
                return;
            }

            if (!ActiveKeywords.Any(existing => string.Equals(existing, kw, StringComparison.OrdinalIgnoreCase)))
            {
                ActiveKeywords.Add(kw);
            }

            txtKeyword.Text = string.Empty;
        }

        private void RemoveKeyword_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.Tag is string kw)
            {
                ActiveKeywords.Remove(kw);
            }
        }

        // --- Manual check ---
        private async void SimulateCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_accountConfigs.Count == 0)
            {
                MessageBox.Show("Please connect at least one email account first.", "No Accounts", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ModalText.Text = "Checking connected inboxes...";
            ModalOverlay.Visibility = Visibility.Visible;

            try
            {
                int found = await _monitorService.CheckNowAsync();
                if (found == 0)
                {
                    MessageBox.Show("No new emails found.", "Inbox Check", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                App.LogException("CheckNow", ex);
                MessageBox.Show($"Inbox check failed.\n\n{ex.Message}", "Check Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ModalOverlay.Visibility = Visibility.Collapsed;
            }
        }
    }
}
