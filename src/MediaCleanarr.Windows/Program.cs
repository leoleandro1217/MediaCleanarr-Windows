using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Drawing;
using System.Windows.Forms;

namespace MediaCleanarr.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed record MediaEntry(string Type, string Title, string Detail, long Bytes, string Path)
{
    public string Size => (Bytes / 1073741824d).ToString("F2", CultureInfo.InvariantCulture);
}

internal static partial class MediaParser
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v", ".ts", ".mpg", ".mpeg", ".webm"
    };

    [GeneratedRegex(@"(?i)(?:^|[. _-])S(\d{1,2})[ ._-]*E(\d{1,3})(?:\b|[. _-])")]
    private static partial Regex EpisodePattern();

    [GeneratedRegex(@"(?<!\d)(19\d{2}|20\d{2})(?!\d)")]
    private static partial Regex YearPattern();

    // Match the beginning of a release-technical suffix, not just remove individual
    // tokens: anything after the suffix belongs to the release, not to the film title.
    // Avoid \b because dots/underscores and trailing brackets are common in filenames.
    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9])(?:4320p|2160p|1080p|720p|480p|4k(?:light)?|8k|uhd|ultrahd|ultra[ ._-]?hd|bluray|blu[ ._-]?ray|brrip|bdrip|webrip|web[ ._-]?dl|hdtv|hdr(?:10(?:\+|plus)?)?|dolby[ ._-]?vision|dovi|dv|imax|hevc(?:10)?|x26[45]|h[ ._-]?26[45]|av1|10[ ._-]?bits?|8[ ._-]?bits?|bt[ ._-]?2020|truehd|atmos|dts(?:[ ._-]?hd)?|ddp|eac3|ac3|aac|multi|vff|vfq|vf2|vof|vostfr|french|proper|repack|remux|extended|unrated)(?![A-Za-z0-9])")]
    private static partial Regex ReleaseTagPattern();

    [GeneratedRegex(@"[._]+")]
    private static partial Regex Separators();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static bool IsVideo(string path) => Extensions.Contains(System.IO.Path.GetExtension(path));

    public static MediaEntry Parse(string path, long bytes)
    {
        var filename = System.IO.Path.GetFileNameWithoutExtension(path);
        var episode = EpisodePattern().Match(filename);
        var year = YearPattern().Match(filename);
        var technical = ReleaseTagPattern().Match(filename);

        var end = new[]
        {
            episode.Success ? episode.Index : filename.Length,
            year.Success ? year.Index : filename.Length,
            technical.Success ? technical.Index : filename.Length
        }.Min();

        // Remove release-only bracket groups before any relevant year or tech tag.
        var title = filename[..end];
        var bracket = Regex.Match(title, @"(?i)[\[(]\s*(?:fr(?:[ -]en)?|en|multi|vff|imax|hdr|dv|4k)\b");
        if (bracket.Success) title = title[..bracket.Index];

        title = Separators().Replace(title, " ");
        title = Whitespace().Replace(title, " ").Trim(' ', '[', ']', '(', ')', '-', '.');
        if (string.IsNullOrWhiteSpace(title)) title = filename;

        var detail = episode.Success
            ? $"S{int.Parse(episode.Groups[1].Value):D2}E{int.Parse(episode.Groups[2].Value):D2}"
            : year.Success ? year.Value : "";
        return new MediaEntry(episode.Success ? "TV Show" : "Movie", title, detail, bytes, path);
    }
}

internal sealed class MainForm : Form
{
    private readonly TextBox folderText = new() { Dock = DockStyle.Fill, PlaceholderText = "Choose a media folder or network drive…" };
    private readonly TextBox filterText = new() { Dock = DockStyle.Fill, PlaceholderText = "Filter by title, year, episode or path…" };
    private readonly Button browseButton = new() { Text = "Browse…", AutoSize = true };
    private readonly Button scanButton = new() { Text = "Scan library", AutoSize = true };
    private readonly Button stopButton = new() { Text = "Stop", AutoSize = true, Enabled = false };
    private readonly Button exportButton = new() { Text = "Export CSV", AutoSize = true, Enabled = false };
    private readonly CheckBox recursiveCheck = new() { Text = "Include subfolders", Checked = true, AutoSize = true };
    private readonly Label status = new() { Text = "Ready — select a folder.", AutoSize = true };
    private readonly ProgressBar busy = new() { Style = ProgressBarStyle.Marquee, Visible = false, Dock = DockStyle.Bottom };
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, MultiSelect = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle
    };
    private readonly ComboBox providerSelect = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 115 };
    private readonly TextBox apiKey = new() { Width = 175, UseSystemPasswordChar = true, PlaceholderText = "API key" };
    private readonly TextBox tvdbPin = new() { Width = 100, UseSystemPasswordChar = true, PlaceholderText = "TVDB PIN" };
    private readonly ComboBox titleLanguage = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly CheckBox cacheEnabled = new() { Text = "SQLite cache", Checked = true, AutoSize = true };
    private MetadataCache? metadataCache;
    private readonly Button checkButton = new() { Text = "Check metadata", AutoSize = true };
    private readonly Button cancelLookupButton = new() { Text = "Stop lookup", AutoSize = true, Enabled = false };
    private readonly CheckBox useMetadataYear = new() { Text = "Show year in title", Checked = true, AutoSize = true };
    private readonly List<MediaEntry> entries = new();
    private readonly Dictionary<string, MetadataMatch> matches = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? lookupCancellation;
    private CancellationTokenSource? scanCancellation;

    public MainForm()
    {
        Text = "MediaCleanarr Windows — .NET Alpha";
        // Reuse the embedded executable icon for the window and taskbar.
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 500);
        Size = new Size(1180, 740);
        Font = new Font("Segoe UI", 10f);

        var page = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 7 };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        Controls.Add(page);

        var title = new Label
        {
            Text = "MediaCleanarr Windows\nRead-only media inventory · No files are modified",
            Dock = DockStyle.Fill, Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Color.FromArgb(35, 48, 72)
        };
        page.Controls.Add(title, 0, 0);

        var folderRow = NewRow();
        folderRow.Controls.Add(folderText, 0, 0);
        folderRow.Controls.Add(browseButton, 1, 0);
        folderRow.ColumnStyles[0].Width = 100;
        page.Controls.Add(folderRow, 0, 1);

        var actionRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        actionRow.Controls.AddRange([recursiveCheck, scanButton, stopButton, exportButton]);
        page.Controls.Add(actionRow, 0, 2);

        providerSelect.Items.AddRange(["TMDb", "OMDb", "TheTVDB", "Off"]);
        providerSelect.SelectedIndex = 0;
        titleLanguage.Items.AddRange(["Original", "French", "English"]);
        titleLanguage.SelectedIndex = 0;
        var metadataRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        metadataRow.Controls.AddRange([
            new Label { Text = "Metadata:", AutoSize = true, Padding = new Padding(0, 7, 0, 0) },
            providerSelect, apiKey, tvdbPin, titleLanguage, useMetadataYear, cacheEnabled, checkButton, cancelLookupButton
        ]);
        page.Controls.Add(metadataRow, 0, 3);
        apiKey.Enabled = true;
        tvdbPin.Enabled = false;
        titleLanguage.Enabled = true;
        checkButton.Enabled = false;
        providerSelect.SelectedIndexChanged += (_, _) => {
            apiKey.Enabled = providerSelect.Text != "Off";
            tvdbPin.Enabled = providerSelect.Text == "TheTVDB";
            titleLanguage.Enabled = providerSelect.Text == "TMDb";
            checkButton.Enabled = providerSelect.Text != "Off" && entries.Count > 0 && scanCancellation == null && lookupCancellation == null;
        };
        checkButton.Click += async (_, _) => await LookupAsync();
        cancelLookupButton.Click += (_, _) => lookupCancellation?.Cancel();
        useMetadataYear.CheckedChanged += (_, _) => RefreshDisplay();
        var attribution = new LinkLabel {
            Text = "TMDb attribution", AutoSize = true,
            Margin = new Padding(6, 8, 3, 3)
        };
        attribution.LinkClicked += (_, _) => MessageBox.Show(this,
            "This product uses the TMDB API but is not endorsed or certified by TMDB.\n\nThe Movie Database: https://www.themoviedb.org",
            "TMDb credits", MessageBoxButtons.OK, MessageBoxIcon.Information);
        metadataRow.Controls.Add(attribution);

        var filterRow = NewRow();
        filterRow.Controls.Add(new Label { Text = "Filter:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        filterRow.Controls.Add(filterText, 1, 0);
        filterRow.ColumnStyles[0].Width = 70;
        page.Controls.Add(filterRow, 0, 4);

        grid.Columns.Add("Type", "Type");
        grid.Columns.Add("Title", "Detected title");
        grid.Columns.Add("Match", "Metadata status");
        grid.Columns.Add("Detail", "Year / Episode");
        grid.Columns.Add("Size", "Size (GiB)");
        grid.Columns.Add("Path", "Full path");
        grid.Columns["Type"]!.FillWeight = 55;
        grid.Columns["Title"]!.FillWeight = 115;
        grid.Columns["Match"]!.FillWeight = 70;
        grid.Columns["Detail"]!.FillWeight = 65;
        grid.Columns["Size"]!.FillWeight = 55;
        grid.Columns["Path"]!.FillWeight = 200;
        page.Controls.Add(grid, 0, 5);
        var footer = new Panel { Dock = DockStyle.Fill };
        footer.Controls.Add(status);
        footer.Controls.Add(busy);
        page.Controls.Add(footer, 0, 6);

        browseButton.Click += (_, _) => Browse();
        scanButton.Click += async (_, _) => await ScanAsync();
        stopButton.Click += (_, _) => scanCancellation?.Cancel();
        exportButton.Click += (_, _) => ExportCsv();
        filterText.TextChanged += (_, _) => ApplyFilter();
        FormClosing += (_, _) => { scanCancellation?.Cancel(); lookupCancellation?.Cancel(); };
    }

    private static TableLayoutPanel NewRow()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return row;
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog { Description = "Select your media library", ShowNewFolderButton = false };
        if (Directory.Exists(folderText.Text)) dialog.InitialDirectory = folderText.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK) folderText.Text = dialog.SelectedPath;
    }

    private async Task ScanAsync()
    {
        var folder = folderText.Text.Trim();
        if (!Directory.Exists(folder))
        {
            MessageBox.Show(this, "Choose an accessible folder first.", "Invalid folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        entries.Clear();
        matches.Clear();
        grid.Rows.Clear();
        scanCancellation = new CancellationTokenSource();
        var token = scanCancellation.Token;
        SetScanning(true);
        int errors = 0;
        var queue = new System.Collections.Concurrent.ConcurrentQueue<MediaEntry>();
        try
        {
            var work = Task.Run(() =>
            {
                var directories = new Stack<string>();
                directories.Push(folder);
                while (directories.Count > 0 && !token.IsCancellationRequested)
                {
                    var dir = directories.Pop();
                    try
                    {
                        if (recursiveCheckSnapshot)
                        {
                            foreach (var subdir in Directory.EnumerateDirectories(dir))
                            {
                                if (!token.IsCancellationRequested) directories.Push(subdir);
                            }
                        }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        Interlocked.Increment(ref errors);
                    }
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(dir))
                        {
                            if (token.IsCancellationRequested) break;
                            if (!MediaParser.IsVideo(file)) continue;
                            try { queue.Enqueue(MediaParser.Parse(file, new FileInfo(file).Length)); }
                            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                            { Interlocked.Increment(ref errors); }
                        }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        Interlocked.Increment(ref errors);
                    }
                }
            }, token);
            while (!work.IsCompleted)
            {
                Drain(queue);
                await Task.Delay(75);
            }
            await work;
            while (!queue.IsEmpty) Drain(queue);
            status.Text = $"{entries.Count:N0} files found · {errors} inaccessible directories/files" +
                          (token.IsCancellationRequested ? " · Stopped" : " · Finished") + " · Nothing changed";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Scan failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetScanning(false);
            scanCancellation.Dispose();
            scanCancellation = null;
        }
    }

    private bool recursiveCheckSnapshot;

    private void SetScanning(bool running)
    {
        if (running) recursiveCheckSnapshot = recursiveCheck.Checked;
        scanButton.Enabled = !running;
        browseButton.Enabled = !running;
        recursiveCheck.Enabled = !running;
        stopButton.Enabled = running;
        exportButton.Enabled = !running && entries.Count > 0;
        checkButton.Enabled = !running && entries.Count > 0 && providerSelect.Text != "Off" && lookupCancellation == null;
        busy.Visible = running;
        if (running) status.Text = "Scanning…";
    }

    private void Drain(System.Collections.Concurrent.ConcurrentQueue<MediaEntry> queue)
    {
        grid.SuspendLayout();
        var shown = 0;
        while (shown < 300 && queue.TryDequeue(out var entry))
        {
            entries.Add(entry);
            var index = grid.Rows.Add(entry.Type, DisplayTitle(entry), "", entry.Detail, entry.Size, entry.Path);
            var term = filterText.Text.Trim();
            if (term.Length > 0) grid.Rows[index].Visible = Matches(entry, term);
            shown++;
        }
        grid.ResumeLayout();
        if (shown > 0) status.Text = $"Scanning… {entries.Count:N0} files";
    }

    private string DisplayTitle(MediaEntry entry)
    {
        var hasMatch = matches.TryGetValue(entry.Path, out var match);
        var title = hasMatch && match!.Status == "Matched" ? match.Title : entry.Title;
        var year = hasMatch && match!.Year.Length == 4 ? match.Year :
            Regex.Match(entry.Detail, @"(?:19|20)\d{2}").Value;
        if (useMetadataYear.Checked && year.Length == 4) return $"{title} ({year})";
        return title;
    }

    private void RefreshDisplay()
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            grid.Rows[i].Cells["Title"].Value = DisplayTitle(entry);
            grid.Rows[i].Cells["Match"].Value = matches.TryGetValue(entry.Path, out var found) ? $"{found.Source}: {found.Status}" : "";
        }
        ApplyFilter();
    }

    private async Task LookupAsync()
    {
        if (providerSelect.Text == "Off" || entries.Count == 0) return;
        var key = apiKey.Text.Trim();
        if (key.Length == 0)
        {
            MessageBox.Show(this, "Enter your API key first.", "Metadata", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var provider = providerSelect.Text;
        var language = provider == "TMDb" ? titleLanguage.Text : "Default";
        MetadataCache? cache = null;
        if (cacheEnabled.Checked)
        {
            try { cache = metadataCache ??= new MetadataCache(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "SQLite cache could not open: " + ex.Message +
                    "\nOnline lookup will continue without caching.", "Cache warning");
            }
        }
        lookupCancellation = new CancellationTokenSource();
        var token = lookupCancellation.Token;
        checkButton.Enabled = false;
        scanButton.Enabled = false;
        cancelLookupButton.Enabled = true;
        providerSelect.Enabled = false;
        apiKey.Enabled = false;
        tvdbPin.Enabled = false;
        titleLanguage.Enabled = false;
        cacheEnabled.Enabled = false;
        busy.Visible = true;
        try
        {
            string? tvdbToken = null;
            if (provider == "TheTVDB")
            {
                status.Text = "Authenticating to TheTVDB…";
                tvdbToken = await MetadataLookup.LoginTvdbAsync(key, tvdbPin.Text.Trim(), token);
                if (string.IsNullOrWhiteSpace(tvdbToken)) throw new InvalidOperationException("TheTVDB did not return an authentication token.");
            }
            var done = 0;
            var cacheHits = 0;
            foreach (var entry in entries.ToArray())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var cacheKey = MetadataCache.Key(entry, provider, language);
                    var cached = cache?.Get(cacheKey);
                    if (cached is not null)
                    {
                        matches[entry.Path] = cached;
                        cacheHits++;
                    }
                    else
                    {
                        var found = provider == "TMDb"
                            ? await TmdbLookup.FindAsync(entry, key, language, token)
                            : await MetadataLookup.FindAsync(entry, provider, key, tvdbToken, token);
                        matches[entry.Path] = found;
                        cache?.Put(cacheKey, found);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // Do not show secret credentials or a URL containing credentials.
                    matches[entry.Path] = new(entry.Title, "", provider, "Error");
                    status.Text = $"Metadata request failed: {ex.GetType().Name}. Check key, access and API limits.";
                    if (done == 0) throw new InvalidOperationException($"First lookup failed ({ex.GetType().Name}). Check your credentials, access and API limits.");
                }
                done++;
                if (done % 10 == 0 || done == entries.Count)
                {
                    RefreshDisplay();
                    status.Text = $"Metadata: {done:N0}/{entries.Count:N0} · {matches.Values.Count(x => x.Status == "Matched")} matched · {matches.Values.Count(x => x.Status == "Review")} to review · {cacheHits} cached";
                }
                if (cacheHits != done) await Task.Delay(200, token);
            }
            RefreshDisplay();
            status.Text = $"Metadata complete: {done} files · {matches.Values.Count(x => x.Status == "Matched")} matched · {matches.Values.Count(x => x.Status == "Review")} to review · {cacheHits} cached.";
        }
        catch (OperationCanceledException) { status.Text = "Metadata lookup stopped. Existing results retained."; }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Metadata lookup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            lookupCancellation.Dispose();
            lookupCancellation = null;
            scanButton.Enabled = true;
            checkButton.Enabled = providerSelect.Text != "Off" && entries.Count > 0;
            cancelLookupButton.Enabled = false;
            providerSelect.Enabled = true;
            apiKey.Enabled = providerSelect.Text != "Off";
            tvdbPin.Enabled = providerSelect.Text == "TheTVDB";
            titleLanguage.Enabled = providerSelect.Text == "TMDb";
            cacheEnabled.Enabled = true;
            busy.Visible = false;
        }
    }

    private bool Matches(MediaEntry e, string text) =>
        string.Join(" ", e.Type, DisplayTitle(e), e.Detail, e.Path, matches.TryGetValue(e.Path, out var match) ? match.Status : "").Contains(text, StringComparison.OrdinalIgnoreCase);

    private void ApplyFilter()
    {
        var term = filterText.Text.Trim();
        grid.CurrentCell = null;
        foreach (DataGridViewRow row in grid.Rows)
        {
            var entry = entries[row.Index];
            row.Visible = term.Length == 0 || Matches(entry, term);
        }
    }

    private void ExportCsv()
    {
        using var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "mediacleanarr-inventory.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true));
            writer.WriteLine("Type,Detected title,Display title,Year / Episode,Metadata title,Metadata year,Metadata source,Metadata status,Size (GiB),Full path");
            foreach (var e in entries)
            {
                matches.TryGetValue(e.Path, out var m);
                writer.WriteLine(string.Join(",", new[] { e.Type, e.Title, DisplayTitle(e), e.Detail, m?.Title ?? "", m?.Year ?? "", m?.Source ?? "", m?.Status ?? "", e.Size, e.Path }.Select(Csv)));
            }
            status.Text = $"Exported {entries.Count:N0} entries: {dialog.FileName}";
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, ex.Message, "CSV export error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
