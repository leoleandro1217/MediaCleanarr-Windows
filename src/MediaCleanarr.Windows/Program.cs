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

    [GeneratedRegex(@"(?i)\b(?:2160p|1080p|720p|480p|bluray|brrip|webrip|web-dl|hdtv|x264|x265|h264|h265|hevc|remux|multi|french|vostfr|proper|repack)\b")]
    private static partial Regex NoisePattern();

    [GeneratedRegex(@"[._-]+")]
    private static partial Regex Separators();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static bool IsVideo(string path) => Extensions.Contains(System.IO.Path.GetExtension(path));

    public static MediaEntry Parse(string path, long bytes)
    {
        var filename = System.IO.Path.GetFileNameWithoutExtension(path);
        var episode = EpisodePattern().Match(filename);
        var year = YearPattern().Match(filename);
        var end = new[] { episode.Success ? episode.Index : filename.Length, year.Success ? year.Index : filename.Length }.Min();
        var title = filename[..end];
        title = Whitespace().Replace(Separators().Replace(NoisePattern().Replace(title, " "), " "), " ").Trim(' ', '[', ']', '(', ')');
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
    private readonly List<MediaEntry> entries = new();
    private CancellationTokenSource? scanCancellation;

    public MainForm()
    {
        Text = "MediaCleanarr Windows — .NET Alpha";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 500);
        Size = new Size(1180, 740);
        Font = new Font("Segoe UI", 10f);

        var page = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 6 };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
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

        var filterRow = NewRow();
        filterRow.Controls.Add(new Label { Text = "Filter:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        filterRow.Controls.Add(filterText, 1, 0);
        filterRow.ColumnStyles[0].Width = 70;
        page.Controls.Add(filterRow, 0, 3);

        grid.Columns.Add("Type", "Type");
        grid.Columns.Add("Title", "Detected title");
        grid.Columns.Add("Detail", "Year / Episode");
        grid.Columns.Add("Size", "Size (GiB)");
        grid.Columns.Add("Path", "Full path");
        grid.Columns["Type"]!.FillWeight = 55;
        grid.Columns["Title"]!.FillWeight = 115;
        grid.Columns["Detail"]!.FillWeight = 65;
        grid.Columns["Size"]!.FillWeight = 55;
        grid.Columns["Path"]!.FillWeight = 200;
        page.Controls.Add(grid, 0, 4);
        var footer = new Panel { Dock = DockStyle.Fill };
        footer.Controls.Add(status);
        footer.Controls.Add(busy);
        page.Controls.Add(footer, 0, 5);

        browseButton.Click += (_, _) => Browse();
        scanButton.Click += async (_, _) => await ScanAsync();
        stopButton.Click += (_, _) => scanCancellation?.Cancel();
        exportButton.Click += (_, _) => ExportCsv();
        filterText.TextChanged += (_, _) => ApplyFilter();
        FormClosing += (_, _) => scanCancellation?.Cancel();
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
            Drain(queue);
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
            var index = grid.Rows.Add(entry.Type, entry.Title, entry.Detail, entry.Size, entry.Path);
            var term = filterText.Text.Trim();
            if (term.Length > 0) grid.Rows[index].Visible = Matches(entry, term);
            shown++;
        }
        grid.ResumeLayout();
        if (shown > 0) status.Text = $"Scanning… {entries.Count:N0} files";
    }

    private static bool Matches(MediaEntry e, string text) =>
        string.Join(" ", e.Type, e.Title, e.Detail, e.Path).Contains(text, StringComparison.OrdinalIgnoreCase);

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
            writer.WriteLine("Type,Detected title,Year / Episode,Size (GiB),Full path");
            foreach (var e in entries)
                writer.WriteLine(string.Join(",", new[] { e.Type, e.Title, e.Detail, e.Size, e.Path }.Select(Csv)));
            status.Text = $"Exported {entries.Count:N0} entries: {dialog.FileName}";
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, ex.Message, "CSV export error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
