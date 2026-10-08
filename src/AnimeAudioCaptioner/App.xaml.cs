using System.Windows;
using System.Windows.Threading;

namespace AnimeAudioCaptioner;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
            window.Activate();
        }
        catch (Exception error)
        {
            MessageBox.Show(error.ToString(), "음성 한글자막 시작 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.ToString(), "음성 한글자막 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(-1);
    }
}
