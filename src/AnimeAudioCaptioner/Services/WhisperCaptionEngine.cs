using System.Text;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.IO;
using Whisper.net;
using Whisper.net.Ggml;

namespace AnimeAudioCaptioner.Services;

public sealed class WhisperCaptionEngine : IDisposable
{
    private const int SampleRate = 16000;
    private const int BytesPerSecond = SampleRate * sizeof(short);
    private const int WindowBytes = BytesPerSecond * 4;
    private const int MaxBufferedBytes = BytesPerSecond * 12;

    private readonly object _gate = new();
    private readonly SemaphoreSlim _modelGate = new(1, 1);
    private readonly List<byte> _pending = new();
    private WhisperFactory? _factory;
    private bool _processing;
    private string _language = "ja";
    private string _lastTranscript = string.Empty;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? TranscriptReady;

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        if (_factory is not null) return;
        await _modelGate.WaitAsync(cancellationToken);
        try
        {
            if (_factory is not null) return;
            var bundled = Path.Combine(AppContext.BaseDirectory, "Models", "ggml-base.bin");
            var models = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnimeAudioCaptioner", "Models");
            var modelPath = bundled;
            if (!IsValidModel(modelPath))
            {
                Directory.CreateDirectory(models);
                modelPath = Path.Combine(models, "ggml-base.bin");
                if (!IsValidModel(modelPath))
                {
                    var temporary = Path.Combine(models, Guid.NewGuid().ToString("N") + ".download");
                    try
                    {
                        StatusChanged?.Invoke(this, "음성 인식 모델 다운로드 중… 창을 닫지 마세요.");
                        await using (var source = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Base))
                        await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            await source.CopyToAsync(target, cancellationToken);
                        }
                        if (!IsValidModel(temporary))
                            throw new InvalidDataException("모델 다운로드가 완료되지 않았어요. 자막 시작을 눌러 다시 시도하세요.");
                        File.Move(temporary, modelPath, true);
                    }
                    finally
                    {
                        if (File.Exists(temporary)) File.Delete(temporary);
                    }
                }
            }
            StatusChanged?.Invoke(this, "음성 인식 모델을 불러오는 중…");
            // Managed file IO supports Korean paths; native fopen paths can fail.
            _factory = await Task.Run(async () =>
            {
                var bytes = await File.ReadAllBytesAsync(modelPath, cancellationToken);
                var factory = WhisperFactory.FromBuffer(bytes);
                try
                {
                    using var processor = factory.CreateBuilder().WithLanguage("ja").Build();
                    return factory;
                }
                catch
                {
                    factory.Dispose();
                    throw;
                }
            }, cancellationToken);
            StatusChanged?.Invoke(this, "음성 인식 모델 준비 완료.");
        }
        finally
        {
            _modelGate.Release();
        }
    }

    private static bool IsValidModel(string path)
    {
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        if (stream.Length < 140_000_000) return false;
        using var reader = new BinaryReader(stream);
        return reader.ReadUInt32() == 0x67676d6c;
    }

    public async Task<string> VerifyRecognitionAsync(string audioPath)
    {
        await EnsureReadyAsync();
        using var audio = new AudioFileReader(audioPath);
        ISampleProvider mono = audio.WaveFormat.Channels == 1 ? audio : audio.ToMono();
        var resampled = new WdlResamplingSampleProvider(mono, SampleRate);
        using var wav = new MemoryStream();
        WaveFileWriter.WriteWavFileToStream(wav, new SampleToWaveProvider16(resampled));
        wav.Position = 0;
        using var processor = _factory!.CreateBuilder().WithLanguage("ja").Build();
        var text = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(wav)) text.Append(segment.Text);
        return text.ToString().Trim();
    }

    public void Reset(string? language)
    {
        lock (_gate)
        {
            _pending.Clear();
            _lastTranscript = string.Empty;
            _language = language is "en" or "en-US" ? "en" : "ja";
        }
    }

    public void AppendPcm16(byte[] pcm, int sampleRate, string? language)
    {
        if (sampleRate != SampleRate || pcm.Length == 0 || pcm.Length % 2 != 0) return;
        byte[]? window = null;
        string transcriptionLanguage;
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(language)) _language = language is "en" or "en-US" ? "en" : "ja";
            _pending.AddRange(pcm);
            if (_pending.Count > MaxBufferedBytes) _pending.RemoveRange(0, _pending.Count - MaxBufferedBytes);
            transcriptionLanguage = _language;
            if (!_processing && _pending.Count >= WindowBytes)
            {
                window = _pending.Take(WindowBytes).ToArray();
                _pending.RemoveRange(0, WindowBytes);
                _processing = true;
            }
        }
        if (window is not null) _ = ProcessWindowAsync(window, transcriptionLanguage);
    }

    private async Task ProcessWindowAsync(byte[] pcm, string language)
    {
        try
        {
            if (AverageAmplitude(pcm) < 180) return;
            await EnsureReadyAsync();
            using var wav = WavEncoder.FromPcm16(pcm);
            using var processor = _factory!.CreateBuilder().WithLanguage(language).Build();
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(wav)) text.Append(' ').Append(segment.Text);
            var transcript = Normalize(text.ToString());
            if (string.IsNullOrWhiteSpace(transcript)) return;

            bool changed;
            lock (_gate)
            {
                changed = !LooksRepeated(_lastTranscript, transcript);
                if (changed) _lastTranscript = transcript;
            }
            if (changed) TranscriptReady?.Invoke(this, transcript);
        }
        catch (Exception error)
        {
            StatusChanged?.Invoke(this, $"음성 인식 오류: {error.Message}");
        }
        finally
        {
            byte[]? next = null;
            string languageForNext;
            lock (_gate)
            {
                _processing = false;
                languageForNext = _language;
                if (_pending.Count >= WindowBytes)
                {
                    next = _pending.Take(WindowBytes).ToArray();
                    _pending.RemoveRange(0, WindowBytes);
                    _processing = true;
                }
            }
            if (next is not null) _ = ProcessWindowAsync(next, languageForNext);
        }
    }

    private static double AverageAmplitude(byte[] pcm)
    {
        long total = 0;
        for (var i = 0; i < pcm.Length; i += 2)
        {
            var sample = BitConverter.ToInt16(pcm, i);
            total += Math.Abs((int)sample);
        }
        return total / (double)(pcm.Length / 2);
    }

    private static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static bool LooksRepeated(string previous, string current)
    {
        if (string.IsNullOrWhiteSpace(previous)) return false;
        var left = new HashSet<string>(previous.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var right = new HashSet<string>(current.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (left.Count == 0 || right.Count == 0) return previous == current;
        return left.Intersect(right).Count() / (double)Math.Min(left.Count, right.Count) >= 0.72;
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _modelGate.Dispose();
    }
}
