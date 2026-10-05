using System.Buffers.Binary;
using System.IO;

namespace AnimeAudioCaptioner.Services;

internal static class WavEncoder
{
    public static MemoryStream FromPcm16(byte[] pcm, int sampleRate = 16000)
    {
        var stream = new MemoryStream(44 + pcm.Length);
        Span<byte> header = stackalloc byte[44];
        header.Clear();
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], 36 + pcm.Length);
        "WAVEfmt "u8.CopyTo(header[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], sampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], 16);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..], pcm.Length);
        stream.Write(header);
        stream.Write(pcm);
        stream.Position = 0;
        return stream;
    }
}
