using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AnimeAudioCaptioner.Services;

namespace AnimeAudioCaptioner;

public partial class MainWindow : Window
{
    private readonly WhisperCaptionEngine _engine = new();
    private readonly SystemAudioCapture _capture;
    private readonly KoreanTranslationService _translator = new();
    private readonly CaptionOverlay _overlay = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _starting;

    public MainWindow()
    {
        InitializeComponent();
        _capture = new SystemAudioCapture(_engine);
        _engine.StatusChanged += (_, message) => SetStatus(message);
        _capture.StatusChanged += (_, message) => SetStatus(message);
        _engine.TranscriptReady += (_, text) => _ = TranslateAndShowAsync(text);
        Loaded += async (_, _) => await StartAsync();
    }

    private string SelectedLanguage => (LanguageBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ja";

    private async Task StartAsync()
    {
        if (_starting) return;
        _starting = true;
        StartButton.IsEnabled = false;
        try
        {
            SetStatus("음성 인식 모델을 확인하는 중…");
            await _engine.EnsureReadyAsync(_lifetime.Token);
            _capture.Stop();
            _capture.Start(SelectedLanguage);
        }
        catch (OperationCanceledException)
        {
            SetStatus("중지했어요.");
        }
        catch (Exception error)
        {
            SetStatus($"시작 오류: {error.Message}");
        }
        finally
        {
            _starting = false;
            StartButton.IsEnabled = true;
        }
    }

    private async Task TranslateAndShowAsync(string transcript)
    {
        try
        {
            var language = await Dispatcher.InvokeAsync(() => SelectedLanguage);
            var korean = await _translator.TranslateAsync(transcript, language, _lifetime.Token);
            await Dispatcher.InvokeAsync(() =>
            {
                _overlay.ShowCaption(korean);
                SetStatus("음성 인식 중 — 한국어 자막을 화면에 표시하고 있어요.");
            });
        }
        catch (Exception error)
        {
            SetStatus($"번역 오류: {error.Message}");
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        await StartAsync();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _capture.Stop();
        _overlay.Hide();
    }

    private void SetStatus(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetStatus(message));
            return;
        }
        StatusText.Text = message;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _lifetime.Cancel();
        _capture.Dispose();
        _engine.Dispose();
        _overlay.Close();
        base.OnClosing(e);
    }
}
