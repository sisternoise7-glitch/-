using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
namespace AnimeAudioCaptioner.Services;

public sealed record AudioLevel(string Device, double Peak, long Bytes);
public sealed class SystemAudioCapture : IDisposable
{
    private sealed class Input
    {
        public required MMDevice Device;
        public required string DeviceName;
        public required WasapiLoopbackCapture Capture;
        public required AudioPcmConverter Converter;
        public double Peak;
        public long LastSignal;
    }
    private readonly WhisperCaptionEngine _engine;
    private readonly List<Input> _inputs = new();
    private readonly object _gate = new();
    private Input? _selected;
    private long _lastReport, _bytes;
    private bool _running;
    public bool IsRunning => _running;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<AudioLevel>? LevelChanged;
    public SystemAudioCapture(WhisperCaptionEngine engine) => _engine = engine;
    public void Start(string language)
    {
        Stop();
        _engine.Reset(language);
        _bytes = 0; _running = true;
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            try
            {
                var capture = new WasapiLoopbackCapture(device);
                var input = new Input { Device = device, DeviceName = device.FriendlyName, Capture = capture, Converter = new AudioPcmConverter(capture.WaveFormat) };
                capture.DataAvailable += (_, e) => Receive(input, e);
                capture.RecordingStopped += (_, e) => { if (e.Exception is not null && _running) StatusChanged?.Invoke(this, "소리 연결 오류: " + e.Exception.Message); };
                _inputs.Add(input);
                capture.StartRecording();
            }
            catch (Exception error)
            {
                StatusChanged?.Invoke(this, "출력 장치 연결 실패: " + error.Message);
                device.Dispose();
            }
        }
        if (_inputs.Count == 0) { _running = false; throw new InvalidOperationException("사용 가능한 스피커·헤드셋 출력 장치를 찾지 못했어요."); }
        StatusChanged?.Invoke(this, $"출력 장치 {_inputs.Count}개 확인 — 실제 소리가 들어오면 입력 막대가 움직입니다.");
    }
    private void Receive(Input input, WaveInEventArgs e)
    {
        if (!_running) return;
        try
        {
            var pcm = input.Converter.Convert(e.Buffer, e.BytesRecorded, out var peak);
            var now = Stopwatch.GetTimestamp();
            bool accept, report;
            lock (_gate)
            {
                input.Peak = peak;
                if (peak > 0.002) input.LastSignal = now;
                if (_selected is null || (now - _selected.LastSignal) / (double)Stopwatch.Frequency > 1.0)
                {
                    if (peak > 0.002) _selected = input;
                }
                accept = _selected == input;
                if (accept) _bytes += e.BytesRecorded;
                report = accept && (now - _lastReport) / (double)Stopwatch.Frequency >= 0.2;
                if (report) _lastReport = now;
            }
            if (accept && pcm.Length > 0) _engine.AppendPcm16(pcm, 16000, null);
            if (report) LevelChanged?.Invoke(this, new AudioLevel(input.DeviceName, peak, _bytes));
        }
        catch (Exception error) { StatusChanged?.Invoke(this, "음성 입력 처리 오류: " + error.Message); }
    }
    public void Stop()
    {
        _running = false;
        foreach (var input in _inputs)
        {
            try { input.Capture.StopRecording(); } catch { }
            input.Capture.Dispose(); input.Device.Dispose();
        }
        _inputs.Clear();
        lock (_gate) { _selected = null; }
        _engine.Reset(null);
    }
    public void Dispose() => Stop();
}
