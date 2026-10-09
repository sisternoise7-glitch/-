using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AnimeAudioCaptioner.Services;
namespace AnimeAudioCaptioner;

public partial class MainWindow : Window
{
    private readonly WhisperCaptionEngine _engine = new();
    private readonly SystemAudioCapture _capture;
    private readonly KoreanTranslationService _translator = new();
    private readonly CaptionOverlay _overlay = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _inputTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly SemaphoreSlim _translationGate = new(1, 1);
    private DateTime _lastAudio = DateTime.MinValue;
    private bool _starting, _active;
    private int _session;

    public MainWindow() : this(true) { }
    public MainWindow(bool autoStart)
    {
        InitializeComponent();
        _capture = new SystemAudioCapture(_engine);
        _engine.StatusChanged += (_, message) => Ui(() => RecognitionText.Text = message);
        _capture.StatusChanged += (_, message) => SetStatus(message);
        _capture.LevelChanged += (_, level) => Ui(() =>
        {
            if (!_active) return;
            _lastAudio = DateTime.UtcNow;
            AudioMeter.Value = Math.Min(100, level.Peak * 100);
            AudioText.Text = $"입력 장치: {level.Device}\n음량 {level.Peak * 100:0.0}% · 수신 {level.Bytes / 1024:N0}KB";
        });
        _engine.TranscriptReady += (_, text) =>
        {
            var session = _session;
            if (_active) _ = TranslateAndShowAsync(text, session);
        };
        _inputTimer.Tick += (_, _) =>
        {
            if (_active && (DateTime.UtcNow - _lastAudio).TotalSeconds > 3)
            {
                AudioMeter.Value = 0;
                AudioText.Text = "실제 소리 입력 없음 — 출력 장치 연결과 영상 소리를 확인해야 합니다.";
            }
        };
        _inputTimer.Start();
        if (autoStart) Loaded += async (_, _) => await StartAsync();
    }
    private string SelectedLanguage => (LanguageBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ja";
    private async Task StartAsync()
    {
        if (_starting) return;
        _starting = true; StartButton.IsEnabled = false;
        try
        {
            StopCurrent();
            SetStatus("음성 인식 모델 확인 중…");
            await _engine.EnsureReadyAsync(_lifetime.Token);
            _active = true; _lastAudio = DateTime.UtcNow;
            _capture.Start(SelectedLanguage);
        }
        catch (OperationCanceledException) { SetStatus("중지했어요."); }
        catch (Exception error) { _active = false; SetStatus("시작 오류: " + error.Message); }
        finally { _starting = false; StartButton.IsEnabled = true; }
    }
    private async Task TranslateAndShowAsync(string transcript, int session)
    {
        var started = Stopwatch.StartNew();
        await _translationGate.WaitAsync(_lifetime.Token);
        try
        {
            if (session != _session) return;
            var language = await Dispatcher.InvokeAsync(() => SelectedLanguage);
            await Dispatcher.InvokeAsync(() => { OriginalText.Text = transcript; StatusText.Text = "원문 인식 완료 — 한국어 번역 중"; });
            var korean = await _translator.TranslateAsync(transcript, language, _lifetime.Token);
            await Dispatcher.InvokeAsync(() =>
            {
                if (session != _session) return;
                KoreanText.Text = korean;
                _overlay.ShowCaption(korean);
                TimingText.Text = $"원문 인식 후 번역·표시 {started.Elapsed.TotalSeconds:0.00}초 (음성 수집·인식 시간은 별도)";
                StatusText.Text = "한국어 자막 표시 완료";
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { SetStatus("번역 오류: " + error.Message); }
        finally { _translationGate.Release(); }
    }
    public async Task<string> RunCaptionTestAsync()
    {
        StopCurrent();
        SetStatus("포함된 일본어 음성으로 인식 → 번역 → 화면 표시 검사 중…");
        var path = Path.Combine(AppContext.BaseDirectory, "test-ja.wav");
        var japanese = await _engine.VerifyRecognitionAsync(path);
        await TranslateAndShowAsync(japanese, _session);
        if (string.IsNullOrEmpty(_overlay.DisplayedText)) throw new InvalidOperationException("자막 화면 표시 실패");
        return KoreanText.Text;
    }
    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        try { await RunCaptionTestAsync(); SetStatus("자체 검사 완료. 영상 소리는 '자막 시작'을 눌러 확인하세요."); }
        catch (Exception error) { SetStatus("자체 검사 오류: " + error.Message); }
        finally { TestButton.IsEnabled = true; }
    }
    private async void StartButton_Click(object sender, RoutedEventArgs e) => await StartAsync();
    private void StopCurrent()
    {
        _active = false; _session++;
        _capture.Stop(); _overlay.Hide();
    }
    private void StopButton_Click(object sender, RoutedEventArgs e) { StopCurrent(); SetStatus("중지했어요."); }
    private void Ui(Action action)
    {
        if (_lifetime.IsCancellationRequested) return;
        if (Dispatcher.CheckAccess()) action(); else Dispatcher.BeginInvoke(action);
    }
    private void SetStatus(string message) => Ui(() => StatusText.Text = message);
    protected override void OnClosing(CancelEventArgs e)
    {
        _inputTimer.Stop(); StopCurrent(); _lifetime.Cancel();
        _capture.Dispose(); _engine.Dispose(); _overlay.Close();
        base.OnClosing(e);
    }
}
