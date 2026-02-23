using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media.Animation;

namespace EmailToastUI
{
    public static class ToastManager
    {
        private static readonly List<ToastWindow> ActiveToasts = new List<ToastWindow>();
        private const double ToastMargin = 15;

        public static void ShowToast(string provider, string sender, string subject, string preview)
        {
            ActiveToasts.RemoveAll(t => !t.IsLoaded || !t.IsVisible);

            if (ActiveToasts.Count >= 4)
            {
                ActiveToasts[0].CloseToast();
            }

            var toast = new ToastWindow(provider, sender, subject, preview);
            ActiveToasts.Add(toast);
            toast.Show();

            var desktopWorkingArea = SystemParameters.WorkArea;
            toast.Left = desktopWorkingArea.Right - toast.ActualWidth - ToastMargin;
            toast.Top = desktopWorkingArea.Bottom;

            RepositionToasts();
        }

        public static void RemoveToast(ToastWindow toast)
        {
            if (ActiveToasts.Remove(toast))
            {
                RepositionToasts();
            }
        }

        private static void RepositionToasts()
        {
            var desktopWorkingArea = SystemParameters.WorkArea;
            double currentBottom = desktopWorkingArea.Bottom - ToastMargin;

            foreach (var toast in ActiveToasts.ToList().AsEnumerable().Reverse())
            {
                if (!toast.IsLoaded || !toast.IsVisible)
                {
                    ActiveToasts.Remove(toast);
                    continue;
                }

                double targetTop = currentBottom - toast.ActualHeight;
                var topAnim = new DoubleAnimation(targetTop, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                toast.BeginAnimation(Window.TopProperty, topAnim);

                currentBottom = targetTop - 10;
            }
        }
    }
}
