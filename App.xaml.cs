using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace EmailToastUI;

public partial class App : Application
{
    private static readonly string ErrorLogPath = Path.Combine(AppContext.BaseDirectory, "app_errors.log");
    private static readonly bool EnableFileLogging =
        string.Equals(Environment.GetEnvironmentVariable("EMAILTOAST_ENABLE_FILE_LOGGING"), "1", StringComparison.Ordinal);

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    public static void LogException(string source, Exception exception)
    {
        if (!EnableFileLogging)
        {
            return;
        }

        try
        {
            string sanitized = SanitizeForLog(exception.ToString());
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{sanitized}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(ErrorLogPath, line);
        }
        catch
        {
            // Best-effort logging only.
        }
    }

    private static string SanitizeForLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Redact common secret/token fields from JSON-ish payloads and query strings.
        text = Regex.Replace(text, "(?i)(\"?(?:access_token|refresh_token|id_token|client_secret|password|code_verifier)\"?\\s*[:=]\\s*\")([^\"]+)(\")", "$1[REDACTED]$3");
        text = Regex.Replace(text, "(?i)((?:access_token|refresh_token|id_token|client_secret|password|code_verifier)=)([^&\\s]+)", "$1[REDACTED]");
        text = Regex.Replace(text, "(?i)(Authorization\\s*:\\s*Bearer\\s+)([^\\s]+)", "$1[REDACTED]");

        // Redact email addresses.
        text = Regex.Replace(text, "[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}", "[REDACTED_EMAIL]");

        return text;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("DispatcherUnhandledException", e.Exception);
        MessageBox.Show(
            EnableFileLogging
                ? "An unexpected error occurred. Details were written to app_errors.log."
                : "An unexpected error occurred.",
            "Application Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogException("AppDomainUnhandledException", ex);
        }
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }
}
