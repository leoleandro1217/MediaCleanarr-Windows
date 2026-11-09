using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MediaCleanarr.Windows;

internal sealed record MetadataMatch(string Title, string Year, string Source, string Status);

internal static class MetadataLookup
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<string?> LoginTvdbAsync(string key, string pin, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api4.thetvdb.com/v4/login");
        var body = new Dictionary<string, string> { ["apikey"] = key };
        if (!string.IsNullOrWhiteSpace(pin)) body["pin"] = pin;
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await Client.SendAsync(request, cancel);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        return Get(GetObject(json.RootElement, "data"), "token");
    }

    public static async Task<MetadataMatch> FindAsync(MediaEntry item, string provider, string key, string? tvdbToken, CancellationToken cancel)
    {
        return provider == "TheTVDB"
            ? await FindTvdbAsync(item, tvdbToken ?? "", cancel)
            : await FindOmdbAsync(item, key, cancel);
    }

    private static async Task<MetadataMatch> FindOmdbAsync(MediaEntry item, string key, CancellationToken cancel)
    {
        var type = item.Type == "Movie" ? "movie" : "series";
        var url = $"https://www.omdbapi.com/?apikey={Uri.EscapeDataString(key)}&s={Uri.EscapeDataString(item.Title)}&type={type}&r=json";
        // The API key must not appear in exception text displayed to the user.
        using var resp = await Client.GetAsync(url, cancel);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"OMDb HTTP {(int)resp.StatusCode}");
        using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancel));
        var root = json.RootElement;
        if (Get(root, "Response") == "False")
        {
            var error = Get(root, "Error") ?? "No results";
            if (error.Contains("API", StringComparison.OrdinalIgnoreCase) || error.Contains("limit", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("OMDb: " + error);
            return new(item.Title, YearFrom(item.Detail), "OMDb", "Not found");
        }
        var list = GetObject(root, "Search");
        return Select(item, list, "Title", "Year", "OMDb");
    }

    private static async Task<MetadataMatch> FindTvdbAsync(MediaEntry item, string token, CancellationToken cancel)
    {
        var type = item.Type == "Movie" ? "movie" : "series";
        var url = $"https://api4.thetvdb.com/v4/search?query={Uri.EscapeDataString(item.Title)}&type={type}&limit=20";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await Client.SendAsync(request, cancel);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"TheTVDB HTTP {(int)resp.StatusCode}");
        using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancel));
        return Select(item, GetObject(json.RootElement, "data"), "name", "year", "TheTVDB");
    }

    private static MetadataMatch Select(MediaEntry item, JsonElement candidates, string titleField, string yearField, string source)
    {
        var chosenTitle = "";
        var chosenYear = "";
        var max = -1d;
        var expectedYear = YearFrom(item.Detail);
        if (candidates.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in candidates.EnumerateArray())
            {
                var title = Get(candidate, titleField);
                if (string.IsNullOrWhiteSpace(title)) continue;
                var year = YearFrom(Get(candidate, yearField) ?? "");
                var score = Similarity(item.Title, title);
                if (expectedYear.Length > 0 && year.Length > 0)
                    score += expectedYear == year ? 0.16 : -0.30;
                if (score > max)
                {
                    chosenTitle = title;
                    chosenYear = year;
                    max = score;
                }
            }
        }
        if (max < 0) return new(item.Title, expectedYear, source, "Not found");
        // Never silently claim that an ambiguous international title is verified.
        var status = max >= 0.82 ? "Matched" : "Review";
        return new(chosenTitle, chosenYear, source, status);
    }

    private static double Similarity(string left, string right)
    {
        static HashSet<string> Words(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            normalized = new string(normalized.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray());
            return Regex.Split(normalized.ToLowerInvariant(), @"[^a-z0-9]+")
                .Where(w => w.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        var a = Words(left);
        var b = Words(right);
        if (a.Count == 0 || b.Count == 0) return 0;
        var intersection = a.Intersect(b).Count();
        return 2d * intersection / (a.Count + b.Count);
    }

    private static string YearFrom(string detail)
    {
        var match = Regex.Match(detail, @"(?:19|20)\d{2}");
        return match.Success ? match.Value : "";
    }

    private static JsonElement GetObject(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) ? value : default;

    private static string? Get(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }
}
