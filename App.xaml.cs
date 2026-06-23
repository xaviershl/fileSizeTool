using System.Configuration;
using System.Data;
using System.Windows;

namespace FileSizeTool;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        this.DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        ShowException(e.Exception, "WPF Dispatcher 异常");
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            ShowException(ex, "不可恢复的异常");
        }
    }

    private static void ShowException(Exception exception, string title)
    {
        try
        {
            System.Windows.MessageBox.Show(exception.ToString(), title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        catch
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FileSizeTool_Error.log"), exception.ToString());
        }
    }
}

