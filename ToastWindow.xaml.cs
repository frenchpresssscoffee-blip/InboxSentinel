using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace EmailToastUI
{
    public partial class ToastWindow : Window
    {
        private readonly DispatcherTimer _closeTimer;
        private bool _isClosing;

        public string ToastId { get; private set; }

        public ToastWindow(string provider, string sender, string subject, string preview)
        {
            InitializeComponent();
            ToastId = Guid.NewGuid().ToString();

            SetupProvider(provider);
            SenderTextBlock.Text = sender;
            SubjectTextBlock.Text = subject;
            PreviewTextBlock.Text = preview;
            TimeTextBlock.Text = DateTime.Now.ToString("h:mm tt");

            _closeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _closeTimer.Tick += CloseTimer_Tick;

            Closed += (_, _) =>
            {
                _closeTimer.Stop();
                _closeTimer.Tick -= CloseTimer_Tick;
            };
        }

        private void SetupProvider(string provider)
        {
            switch (provider.ToLowerInvariant())
            {
                case "gmail":
                    AppNameTextBlock.Text = "GMAIL";
                    IconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA4335"));
                    GlowBorder.Background = IconBorder.Background;
                    IconText.Text = "G";
                    break;
                case "outlook":
                    AppNameTextBlock.Text = "OUTLOOK";
                    IconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0078D4"));
                    GlowBorder.Background = IconBorder.Background;
                    IconText.Text = "O";
                    break;
                case "yahoo":
                    AppNameTextBlock.Text = "YAHOO MAIL";
                    IconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6001D2"));
                    GlowBorder.Background = IconBorder.Background;
                    IconText.Text = "Y!";
                    IconText.FontSize = 18;
                    IconText.Margin = new Thickness(0, 0, 0, 0);
                    break;
                case "proton":
                case "protonmail":
                    AppNameTextBlock.Text = "PROTON MAIL";
                    IconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6D4AFF"));
                    GlowBorder.Background = IconBorder.Background;
                    IconText.Text = "P";
                    break;
                default:
                    AppNameTextBlock.Text = "MAIL";
                    IconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#424242"));
                    GlowBorder.Background = IconBorder.Background;
                    IconText.Text = "M";
                    break;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (FindResource("FadeInSlideUp") is Storyboard sb)
            {
                sb.Begin();
            }

            _closeTimer.Start();
        }

        private void CloseTimer_Tick(object? sender, EventArgs e)
        {
            _closeTimer.Stop();
            CloseToast();
        }

        private void Grid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _closeTimer.Stop();
            CloseToast();
        }

        private void MainGrid_MouseEnter(object sender, MouseEventArgs e)
        {
            _closeTimer.Stop();
            DoubleAnimation fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200));
            CloseButton.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        private void MainGrid_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isClosing)
            {
                _closeTimer.Start();
            }

            DoubleAnimation fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
            CloseButton.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _closeTimer.Stop();
            CloseToast();
        }

        public void CloseToast()
        {
            if (_isClosing)
            {
                return;
            }

            _isClosing = true;
            ToastManager.RemoveToast(this);

            if (FindResource("FadeOutSlideDown") is Storyboard sb)
            {
                sb.Begin();
            }
            else
            {
                Close();
            }
        }

        private void FadeOut_Completed(object sender, EventArgs e)
        {
            Close();
        }
    }
}
