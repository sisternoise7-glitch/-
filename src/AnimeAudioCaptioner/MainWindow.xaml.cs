using System.ComponentModel;
using System.Windows;
using AnimeAudioCaptioner.Services;

namespace AnimeAudioCaptioner;

public partial class MainWindow : Window
{
    private readonly WhisperCaptionEngine _engine = new();
    private readonly LoopbackBridgeServer _server;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _starting;

    public MainWindow()
    {
        InitializeComponent();
        _server = new LoopbackBridgeServer(_engine);
        _engine.StatusChanged += (_, message) => SetStatus(message);
        _server.StatusChanged += (_, message) => SetStatus(message);
        Loaded += async (_, _) => await StartAsync();
    }

    private async Task StartAsync()
    {
        if (_starting || _server.IsRunning) return;
        _starting = true;
        StartButton.IsEnabled = false;
        try
        {
            SetStatus("음성 인식 모델을 확인하는 중…");
            await _engine.EnsureReadyAsync(_lifetime.Token);
            await _server.StartAsync(_lifetime.Token);
            SetStatus("준비 완료 — Chrome 확장의 오디오 연결을 기다리는 중");
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

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        await _server.StopAsync();
        await StartAsync();
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await _server.StopAsync();
        SetStatus("중지했어요.");
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

    protected override async void OnClosing(CancelEventArgs e)
    {
        _lifetime.Cancel();
        await _server.DisposeAsync();
        _engine.Dispose();
        base.OnClosing(e);
    }
}
