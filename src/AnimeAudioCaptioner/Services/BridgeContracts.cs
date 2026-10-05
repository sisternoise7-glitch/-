namespace AnimeAudioCaptioner.Services;

public sealed class BridgeMessage
{
    public string? Type { get; init; }
    public string? Pcm16 { get; init; }
    public int SampleRate { get; init; } = 16000;
    public string? Language { get; init; }
}

public sealed record BridgeReply(string Type, string? State = null, string? Text = null, string? Detail = null);
