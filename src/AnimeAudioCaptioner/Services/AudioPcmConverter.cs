using System.Buffers.Binary;
using NAudio.Wave;
namespace AnimeAudioCaptioner.Services;

public sealed class AudioPcmConverter
{
    private readonly int _rate, _channels, _bits, _frameBytes;
    private readonly bool _float;
    private int _remainder;
    public AudioPcmConverter(WaveFormat format)
    {
        _rate = format.SampleRate; _channels = format.Channels; _bits = format.BitsPerSample;
        _frameBytes = format.BlockAlign;
        _float = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            (format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"));
        if (_rate < 16000 || _channels < 1 || !(_bits is 16 or 24 or 32) || (_float && _bits != 32))
            throw new NotSupportedException("지원하지 않는 출력 장치 음성 형식: " + format);
    }
    public byte[] Convert(byte[] input, int count, out double peak)
    {
        var output = new List<short>();
        peak = 0;
        for (var offset = 0; offset + _frameBytes <= count; offset += _frameBytes)
        {
            double mono = 0;
            for (var channel = 0; channel < _channels; channel++)
            {
                int p = offset + channel * (_bits / 8);
                double sample = _float ? BitConverter.ToSingle(input, p) : _bits switch
                {
                    16 => BinaryPrimitives.ReadInt16LittleEndian(input.AsSpan(p, 2)) / 32768.0,
                    24 => ((input[p] | input[p + 1] << 8 | input[p + 2] << 16) << 8 >> 8) / 8388608.0,
                    _ => BinaryPrimitives.ReadInt32LittleEndian(input.AsSpan(p, 4)) / 2147483648.0
                };
                if (!double.IsFinite(sample)) sample = 0;
                peak = Math.Max(peak, Math.Abs(sample));
                mono += sample;
            }
            mono /= _channels;
            _remainder += 16000;
            if (_remainder < _rate) continue;
            _remainder -= _rate;
            output.Add((short)Math.Clamp(mono * 32767, -32768, 32767));
        }
        var bytes = new byte[output.Count * 2];
        Buffer.BlockCopy(output.ToArray(), 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
