using System.Net.Http;
using System.Text.Json;

namespace AnimeAudioCaptioner.Services;

public sealed class KoreanTranslationService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };

    public async Task<string> TranslateAsync(string text, string sourceLanguage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var source = sourceLanguage is "en" or "en-US" ? "en" : "ja";
        var uri = "https://translate.googleapis.com/translate_a/single?client=gtx&sl="
            + source + "&tl=ko&dt=t&q=" + Uri.EscapeDataString(text);
        using var response = await Client.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var parts = document.RootElement[0];
        var translated = string.Concat(parts.EnumerateArray()
            .Where(part => part.GetArrayLength() > 0)
            .Select(part => part[0].GetString()));
        return string.IsNullOrWhiteSpace(translated) ? text : translated.Trim();
    }
}
