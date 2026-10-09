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
    private const int WindowBytes = BytesPerSecond;
    private const int MaxBufferedBytes = BytesPerSecond * 3;

    private readonly object _gate = new();
    private readonly SemaphoreSlim _modelGate = new(1, 1);
    private readonly List<byte> _pending = new();
    private WhisperFactory? _factory;
    private bool _processing;
    private bool _disposed;
    private int _newBytes;
    private int _generation;
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
            _generation++;
            _pending.Clear(); _newBytes = 0;
            _lastTranscript = string.Empty;
            if (language is not null) _language = language is "en" or "en-US" ? "en" : "ja";
        }
    }

    public void AppendPcm16(byte[] pcm, int sampleRate, string? language)
    {
        if (_disposed || sampleRate != SampleRate || pcm.Length == 0 || pcm.Length % 2 != 0) return;
        byte[]? window = null;
        string transcriptionLanguage;
        int generation;
        lock (_gate)
        {
            _pending.AddRange(pcm); _newBytes += pcm.Length;
            if (_pending.Count > MaxBufferedBytes) _pending.RemoveRange(0, _pending.Count - MaxBufferedBytes);
            transcriptionLanguage = _language; generation = _generation;
            if (!_processing && _newBytes >= WindowBytes)
            {
                window = _pending.ToArray();
                _newBytes = 0; _processing = true;
            }
        }
        // Native recognition must never block the WASAPI capture callback.
        if (window is not null) _ = Task.Run(() => ProcessWindowAsync(window, transcriptionLanguage, generation));
    }

    private async Task ProcessWindowAsync(byte[] pcm, string language, int generation)
    {
        try
        {
            if (AverageAmplitude(pcm) < 45)
            {
                StatusChanged?.Invoke(this, "입력은 있지만 대사 음량이 작거나 무음입니다.");
                return;
            }
            StatusChanged?.Invoke(this, $"대사 인식 중 — 최근 {pcm.Length / (double)BytesPerSecond:0.0}초");
            await EnsureReadyAsync();
            using var wav = WavEncoder.FromPcm16(pcm);
            using var processor = _factory!.CreateBuilder().WithLanguage(language).Build();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(wav)) text.Append(' ').Append(segment.Text);
            var transcript = Normalize(text.ToString());
            bool changed;
            lock (_gate)
            {
                changed = !_disposed && generation == _generation && transcript.Length > 0 && _lastTranscript != transcript;
                if (changed) _lastTranscript = transcript;
            }
            if (changed) TranscriptReady?.Invoke(this, transcript);
            StatusChanged?.Invoke(this, $"인식 처리 {watch.Elapsed.TotalSeconds:0.0}초" + (transcript.Length == 0 ? " — 대사를 찾지 못했어요." : " — 대사 인식 완료."));
        }
        catch (Exception error)
        {
            if (!_disposed) StatusChanged?.Invoke(this, $"음성 인식 오류: {error.Message}");
        }
        finally
        {
            byte[]? next = null;
            string languageForNext;
            int nextGeneration;
            lock (_gate)
            {
                _processing = false;
                languageForNext = _language; nextGeneration = _generation;
                if (!_disposed && _newBytes >= WindowBytes && _pending.Count >= WindowBytes)
                {
                    next = _pending.ToArray(); _newBytes = 0; _processing = true;
                }
                if (_disposed) { _factory?.Dispose(); _factory = null; }
            }
            if (next is not null) _ = Task.Run(() => ProcessWindowAsync(next, languageForNext, nextGeneration));
        }
    }

    public async Task<string> VerifyStreamingAsync(string audioPath)
    {
        await EnsureReadyAsync();
        Reset("ja");
        var transcripts = new List<string>();
        void Collect(object? sender, string text) { lock (transcripts) transcripts.Add(text); }
        TranscriptReady += Collect;
        try
        {
            using var reader = new AudioFileReader(audioPath);
            var converter = new AudioPcmConverter(reader.WaveFormat);
            var floats = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels / 20];
            int read;
            while ((read = reader.Read(floats, 0, floats.Length)) > 0)
            {
                var raw = new byte[read * 4];
                Buffer.BlockCopy(floats, 0, raw, 0, raw.Length);
                var pcm = converter.Convert(raw, raw.Length, out _);
                AppendPcm16(pcm, 16000, "ja");
                await Task.Delay(50);
            }
            await Task.Delay(8000);
            lock (transcripts) return string.Join(" / ", transcripts);
        }
        finally { TranscriptReady -= Collect; Reset("ja"); }
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

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true; _generation++; _pending.Clear();
            if (!_processing) { _factory?.Dispose(); _factory = null; }
        }
    }
}
