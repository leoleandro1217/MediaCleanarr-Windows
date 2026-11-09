using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MediaCleanarr.Windows;

/// <summary>TMDb v3 search, followed by localized details for a selected result.</summary>
internal static class TmdbLookup
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    public static async Task<MetadataMatch> FindAsync(
        MediaEntry item, string credential, string language, CancellationToken cancel)
    {
        var isMovie = item.Type == "Movie";
        var kind = isMovie ? "movie" : "tv";
        var search = await GetJsonAsync(
            $"https://api.themoviedb.org/3/search/{kind}?query={Uri.EscapeDataString(item.Title)}&language=fr-FR&include_adult=false&page=1",
            credential, cancel);
        using (search)
        {
            if (!search.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
                return new(item.Title, "", "TMDb", "Not found");

            var expectedYear = Regex.Match(item.Detail, @"(?<!\d)(?:19|20)\d{2}(?!\d)").Value;
            double bestScore = double.NegativeInfinity;
            JsonElement best = default;
            foreach (var candidate in results.EnumerateArray())
            {
                var title = Read(candidate, isMovie ? "title" : "name");
                var original = Read(candidate, isMovie ? "original_title" : "original_name");
                var year = ExtractYear(Read(candidate, isMovie ? "release_date" : "first_air_date"));
                var score = Math.Max(Similarity(item.Title, title), Similarity(item.Title, original));
                if (expectedYear.Length == 4 && year.Length == 4)
                    score += expectedYear == year ? 0.18 : -0.30;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (!best.TryGetProperty("id", out var idJson) || !idJson.TryGetInt32(out var id))
                return new(item.Title, "", "TMDb", "Not found");

            // Details supply the original title as well as the requested localization.
            var locale = language switch { "French" => "fr-FR", "English" => "en-US", _ => "en-US" };
            using var details = await GetJsonAsync(
                $"https://api.themoviedb.org/3/{kind}/{id}?language={locale}",
                credential, cancel);
            var data = details.RootElement;
            var localized = Read(data, isMovie ? "title" : "name");
            var originalTitle = Read(data, isMovie ? "original_title" : "original_name");
            var finalTitle = language == "Original" ? originalTitle : localized;
            if (string.IsNullOrWhiteSpace(finalTitle))
                finalTitle = string.IsNullOrWhiteSpace(localized) ? item.Title : localized;
            var yearValue = ExtractYear(Read(data, isMovie ? "release_date" : "first_air_date"));
            var verdict = bestScore >= 0.83 ? "Matched" : "Review";
            // Guard against accepting a dissimilar candidate solely due to a coincidental year.
            if (bestScore < 0.50) verdict = "Review";
            if (expectedYear.Length == 4 && yearValue.Length == 4 && expectedYear != yearValue)
                verdict = "Review";
            return new(finalTitle, yearValue, "TMDb", verdict);
        }
    }

    private static async Task<JsonDocument> GetJsonAsync(string url, string credential, CancellationToken cancel)
    {
        // V3 API keys are 32-char hex, while read-access tokens are JWTs.
        var isToken = credential.Contains('.');
        if (!isToken)
            url += (url.Contains('?') ? "&" : "?") + "api_key=" + Uri.EscapeDataString(credential);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (isToken) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
            using var response = await Http.SendAsync(request, cancel);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 2)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 + attempt * 2);
                if (delay > TimeSpan.FromSeconds(30)) delay = TimeSpan.FromSeconds(30);
                await Task.Delay(delay, cancel);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"TMDb HTTP {(int)response.StatusCode}. Check your key/token or API limits.");
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        }
        throw new InvalidOperationException("TMDb throttled requests; try again later.");
    }

    private static string Read(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string ExtractYear(string date) =>
        Regex.Match(date, @"^(?:19|20)\d{2}").Value;

    internal static double Similarity(string left, string right)
    {
        static HashSet<string> Words(string input)
        {
            var decomposed = input.Normalize(NormalizationForm.FormD);
            var stripped = new string(decomposed.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray());
            return Regex.Split(stripped.ToLowerInvariant(), @"[^a-z0-9]+")
                .Where(s => s.Length > 0).ToHashSet(StringComparer.Ordinal);
        }
        var a = Words(left);
        var b = Words(right);
        if (a.Count == 0 || b.Count == 0) return 0;
        return 2d * a.Intersect(b).Count() / (a.Count + b.Count);
    }
}
