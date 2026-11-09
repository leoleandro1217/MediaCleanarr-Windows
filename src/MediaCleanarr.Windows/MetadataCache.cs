using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MediaCleanarr.Windows;

/// <summary>
/// Persistent local metadata cache. No API keys or file paths are stored.
/// A result is valid for at most 90 days (negative results: 7 days).
/// </summary>
internal sealed class MetadataCache
{
    private readonly string connectionString;

    public MetadataCache()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MediaCleanarr-Windows");
        Directory.CreateDirectory(folder);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(folder, "metadata-cache.sqlite"),
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        connectionString = builder.ToString();
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS metadata_cache (
                cache_key TEXT PRIMARY KEY,
                value_json TEXT NOT NULL,
                expires_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connectionString);
        db.Open();
        return db;
    }

    public static string Key(MediaEntry item, string provider, string language)
    {
        var text = string.Join("|", "v1", provider, language, item.Type,
            item.Title.Trim().ToLowerInvariant(), item.Detail.Trim().ToLowerInvariant());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    public MetadataMatch? Get(string key)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT value_json FROM metadata_cache WHERE cache_key = $key AND expires_at > $now";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        var json = command.ExecuteScalar() as string;
        return json is null ? null : JsonSerializer.Deserialize<MetadataMatch>(json);
    }

    public void Put(string key, MetadataMatch match)
    {
        if (match.Status == "Error") return;
        var days = match.Status == "Not found" ? 7 : 90;
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            INSERT INTO metadata_cache(cache_key, value_json, expires_at)
            VALUES($key, $json, $expires)
            ON CONFLICT(cache_key) DO UPDATE SET
                value_json = excluded.value_json,
                expires_at = excluded.expires_at
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(match));
        command.Parameters.AddWithValue("$expires", DateTimeOffset.UtcNow.AddDays(days).ToString("O"));
        command.ExecuteNonQuery();
    }
}
