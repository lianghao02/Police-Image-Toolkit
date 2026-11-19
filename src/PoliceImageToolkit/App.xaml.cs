using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PoliceImageToolkit;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string logPath = LogException("DispatcherUnhandledException", e.Exception);
        MessageBox.Show($"應用程式發生未預期錯誤：\n\n{e.Exception.Message}\n\n詳細資訊已記錄至：\n{logPath}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogException("CurrentDomain_UnhandledException", ex);
        }
    }

    private static string LogException(string source, Exception ex)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string baseDir = string.IsNullOrWhiteSpace(appData) ? Path.GetTempPath() : Path.Combine(appData, "PoliceImageToolkit");
        string logPath = Path.Combine(baseDir, "crash.log");
        try
        {
            Directory.CreateDirectory(baseDir);
            string content = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex.GetType().Name} (0x{ex.HResult:X8}): {ex.Message}\n{ex.StackTrace}\n\n";
            File.AppendAllText(logPath, content);
        }
        catch
        {
            // 忽略寫入日誌失敗
        }
        return logPath;
    }
}
