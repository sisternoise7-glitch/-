using NAudio.Wave;

namespace AnimeAudioCaptioner.Services;

public sealed class SystemAudioCapture : IDisposable
{
    private readonly WhisperCaptionEngine _engine;
    private WasapiLoopbackCapture? _capture;
    private int _sourceRate;
    private int _channels;
    private int _resampleRemainder;
    private bool _disposed;

    public bool IsRunning => _capture is not null;
    public event EventHandler<string>? StatusChanged;

    public SystemAudioCapture(WhisperCaptionEngine engine)
    {
        _engine = engine;
    }

    public void Start(string language)
    {
        if (_capture is not null) return;
        _engine.Reset(language);
        _capture = new WasapiLoopbackCapture();
        _sourceRate = _capture.WaveFormat.SampleRate;
        _channels = _capture.WaveFormat.Channels;
        _resampleRemainder = 0;
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
        StatusChanged?.Invoke(this, "PC에서 재생되는 소리를 듣는 중 — 영상 재생 후 자막이 표시됩니다.");
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_sourceRate <= 0 || _channels <= 0 || e.BytesRecorded < sizeof(float) * _channels) return;

        var samples = new float[e.BytesRecorded / sizeof(float)];
        Buffer.BlockCopy(e.Buffer, 0, samples, 0, samples.Length * sizeof(float));
        var output = new List<short>();
        for (var offset = 0; offset + _channels <= samples.Length; offset += _channels)
        {
            var mono = 0f;
            for (var channel = 0; channel < _channels; channel++) mono += samples[offset + channel];
            mono /= _channels;

            _resampleRemainder += 16000;
            if (_resampleRemainder < _sourceRate) continue;
            _resampleRemainder -= _sourceRate;
            output.Add((short)Math.Clamp(mono * short.MaxValue, short.MinValue, short.MaxValue));
        }

        if (output.Count == 0) return;
        var pcm = new byte[output.Count * sizeof(short)];
        Buffer.BlockCopy(output.ToArray(), 0, pcm, 0, pcm.Length);
        _engine.AppendPcm16(pcm, 16000, null);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null) StatusChanged?.Invoke(this, $"시스템 소리 연결 오류: {e.Exception.Message}");
    }

    public void Stop()
    {
        var capture = Interlocked.Exchange(ref _capture, null);
        if (capture is null) return;
        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;
        try { capture.StopRecording(); } catch { }
        capture.Dispose();
        StatusChanged?.Invoke(this, "자막을 중지했어요.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
