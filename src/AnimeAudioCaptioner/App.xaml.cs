using System.IO;
using System.Windows;
using System.Windows.Threading;
using AnimeAudioCaptioner.Services;

namespace AnimeAudioCaptioner;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--self-test"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                using var engine = new WhisperCaptionEngine();
                var japanese = await engine.VerifyRecognitionAsync(Path.Combine(AppContext.BaseDirectory, "test-ja.wav"));
                if (!japanese.Contains("音声") || !japanese.Contains("テスト"))
                    throw new InvalidDataException("Japanese audio recognition mismatch: " + japanese);
                var translator = new KoreanTranslationService();
                var korean = await translator.TranslateAsync(japanese, "ja");
                if (!korean.Any(c => c >= '\uAC00' && c <= '\uD7A3') || !korean.Contains("테스트"))
                    throw new InvalidDataException("Korean translation mismatch: " + korean);
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test.txt"),
                    "Japanese: " + japanese + Environment.NewLine + "Korean: " + korean + Environment.NewLine + "MODEL_LOAD_AND_RECOGNITION_OK");
                Shutdown(0);
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test.txt"), error.ToString());
                Shutdown(1);
            }
            return;
        }
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
