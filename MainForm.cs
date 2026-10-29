using System.Diagnostics;
using System.Globalization;

namespace WebmAudioMuxer;

public class MainForm : Form
{
    private readonly TextBox txtFfmpeg = new() { Text = "ffmpeg", Dock = DockStyle.Fill };
    private readonly TextBox txtVideo = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox txtAudio = new() { ReadOnly = true, Dock = DockStyle.Fill };

    private readonly NumericUpDown numOffset = MakeNum(0, 3600);
    private readonly NumericUpDown numTrimStart = MakeNum(0, 3600);
    private readonly NumericUpDown numTrimEnd = MakeNum(0, 3600);

    private readonly Label lblInfo = new() { Dock = DockStyle.Fill, AutoSize = true, Text = "Pick a video and an audio file." };
    private readonly Button btnPreview = new() { Text = "Preview", Width = 110, Height = 34 };
    private readonly Button btnExport = new() { Text = "EXPORT", Width = 140, Height = 34 };
    private readonly TextBox txtLog = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9f)
    };

    private double? videoDuration;
    private double? audioDuration;

    public MainForm()
    {
        Text = "WebM + WAV Muxer (keeps transparency)";
        Width = 760;
        Height = 620;
        StartPosition = FormStartPosition.CenterScreen;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Padding = new Padding(10),
            AutoSize = false
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

        AddRow(grid, "ffmpeg path:", txtFfmpeg, MakeBrowse(BrowseFfmpeg));
        AddRow(grid, "Video (.webm):", txtVideo, MakeBrowse(BrowseVideo));
        AddRow(grid, "Audio (.wav):", txtAudio, MakeBrowse(BrowseAudio));
        AddRow(grid, "Delay audio start by (sec):", numOffset, null);
        AddRow(grid, "Trim from audio start (sec):", numTrimStart, null);
        AddRow(grid, "Trim from audio end (sec):", numTrimEnd, null);

        grid.Controls.Add(lblInfo, 0, grid.RowCount);
        grid.SetColumnSpan(lblInfo, 3);
        grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.Add(btnPreview);
        buttons.Controls.Add(btnExport);
        grid.Controls.Add(buttons, 0, grid.RowCount);
        grid.SetColumnSpan(buttons, 3);
        grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        grid.Controls.Add(txtLog, 0, grid.RowCount);
        grid.SetColumnSpan(txtLog, 3);
        grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Controls.Add(grid);

        btnExport.Click += async (_, _) => await ExportAsync();
        btnPreview.Click += async (_, _) => await PreviewAsync();
        numTrimStart.ValueChanged += (_, _) => UpdateInfo();
        numTrimEnd.ValueChanged += (_, _) => UpdateInfo();
        numOffset.ValueChanged += (_, _) => UpdateInfo();
    }

    // ---------- UI helpers ----------

    private static NumericUpDown MakeNum(decimal min, decimal max) => new()
    {
        DecimalPlaces = 3,
        Increment = 0.05m,
        Minimum = min,
        Maximum = max,
        Width = 120
    };

    private static Button MakeBrowse(EventHandler onClick)
    {
        var b = new Button { Text = "Browse...", Dock = DockStyle.Fill };
        b.Click += onClick;
        return b;
    }

    private static void AddRow(TableLayoutPanel grid, string label, Control input, Control? button)
    {
        int row = grid.RowCount;
        grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) }, 0, row);
        grid.Controls.Add(input, 1, row);
        if (button != null) grid.Controls.Add(button, 2, row);
    }

    private void Log(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(text)); return; }
        txtLog.AppendText(text + Environment.NewLine);
    }

    private void UpdateInfo()
    {
        var parts = new List<string>();
        if (videoDuration.HasValue) parts.Add($"Video: {videoDuration:F3}s");
        if (audioDuration.HasValue)
        {
            double len = audioDuration.Value - (double)numTrimStart.Value - (double)numTrimEnd.Value;
            parts.Add($"Audio: {audioDuration:F3}s (after trim: {Math.Max(0, len):F3}s)");
            if (videoDuration.HasValue && len > 0)
            {
                double end = (double)numOffset.Value + len;
                parts.Add($"Audio ends at {end:F3}s" + (end > videoDuration ? " (will be cut at video end)" : ""));
            }
        }
        lblInfo.Text = parts.Count > 0 ? string.Join("   |   ", parts) : "Pick a video and an audio file.";
    }

    // ---------- Browse handlers ----------

    private void BrowseFfmpeg(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Filter = "ffmpeg.exe|ffmpeg.exe|All files|*.*" };
        if (dlg.ShowDialog() == DialogResult.OK) txtFfmpeg.Text = dlg.FileName;
    }

    private void BrowseVideo(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Filter = "WebM video|*.webm|All files|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        txtVideo.Text = dlg.FileName;
        videoDuration = ProbeDuration(dlg.FileName);
        UpdateInfo();
    }

    private void BrowseAudio(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Filter = "WAV audio|*.wav|All audio|*.wav;*.mp3;*.ogg;*.flac;*.m4a|All files|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        txtAudio.Text = dlg.FileName;
        audioDuration = ProbeDuration(dlg.FileName);
        UpdateInfo();
    }

    // ---------- ffmpeg / ffprobe ----------

    private string FfmpegExe => string.IsNullOrWhiteSpace(txtFfmpeg.Text) ? "ffmpeg" : txtFfmpeg.Text.Trim();

    private string SiblingTool(string name)
    {
        string exe = FfmpegExe;
        string? dir = Path.GetDirectoryName(exe);
        if (string.IsNullOrEmpty(dir)) return name;
        string candidate = Path.Combine(dir, name + ".exe");
        return File.Exists(candidate) ? candidate : name;
    }

    private double? ProbeDuration(string file)
    {
        try
        {
            var psi = new ProcessStartInfo(SiblingTool("ffprobe"))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", file })
                psi.ArgumentList.Add(a);

            using var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(10000);
            if (double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
        }
        catch (Exception ex)
        {
            Log("ffprobe failed: " + ex.Message);
        }
        return null;
    }

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Builds the ffmpeg argument list. Returns null (and logs why) if inputs are invalid.</summary>
    private List<string>? BuildArgs(string outputFile)
    {
        if (!File.Exists(txtVideo.Text)) { Log("Choose a video file first."); return null; }
        if (!File.Exists(txtAudio.Text)) { Log("Choose an audio file first."); return null; }

        double offset = (double)numOffset.Value;
        double trimStart = (double)numTrimStart.Value;
        double trimEnd = (double)numTrimEnd.Value;

        double? audioLen = null;
        if (audioDuration.HasValue)
        {
            audioLen = audioDuration.Value - trimStart - trimEnd;
            if (audioLen <= 0) { Log("Trim values remove all of the audio."); return null; }
        }
        else if (trimEnd > 0)
        {
            Log("Couldn't read the audio duration, so 'trim from end' can't be used.");
            return null;
        }

        var a = new List<string> { "-y", "-i", txtVideo.Text };

        // Input options for the audio file (apply to the next -i)
        if (trimStart > 0) { a.Add("-ss"); a.Add(Num(trimStart)); }
        if (audioLen.HasValue && trimEnd > 0) { a.Add("-t"); a.Add(Num(audioLen.Value)); }
        a.Add("-i"); a.Add(txtAudio.Text);

        a.AddRange(new[] { "-map", "0:v:0", "-map", "1:a:0" });

        // Copy video untouched so the alpha channel is preserved
        a.AddRange(new[] { "-c:v", "copy" });

        if (offset > 0)
        {
            long ms = (long)Math.Round(offset * 1000);
            a.Add("-af"); a.Add($"adelay={ms}:all=1");
        }

        a.AddRange(new[] { "-c:a", "libopus", "-b:a", "128k" });

        // Never let the output run longer than the video
        if (videoDuration.HasValue) { a.Add("-t"); a.Add(Num(videoDuration.Value)); }

        a.Add(outputFile);
        return a;
    }

    private Task<int> RunAsync(string exe, IEnumerable<string> args)
    {
        var tcs = new TaskCompletionSource<int>();
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Log(e.Data); };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Log(e.Data); };
        p.Exited += (_, _) =>
        {
            int code = p.ExitCode;
            p.Dispose();
            tcs.TrySetResult(code);
        };

        try
        {
            p.Start();
            p.BeginErrorReadLine();
            p.BeginOutputReadLine();
        }
        catch (Exception ex)
        {
            Log($"Could not start {exe}: {ex.Message}");
            tcs.TrySetResult(-1);
        }
        return tcs.Task;
    }

    // ---------- Actions ----------

    private void SetBusy(bool busy)
    {
        btnExport.Enabled = !busy;
        btnPreview.Enabled = !busy;
    }

    private async Task ExportAsync()
    {
        using var dlg = new SaveFileDialog { Filter = "WebM video|*.webm", FileName = "output.webm" };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        if (string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(txtVideo.Text), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Pick a different file name than the source video.");
            return;
        }

        var args = BuildArgs(dlg.FileName);
        if (args == null) return;

        txtLog.Clear();
        Log("ffmpeg " + string.Join(" ", args.Select(x => x.Contains(' ') ? $"\"{x}\"" : x)));
        SetBusy(true);
        int code = await RunAsync(FfmpegExe, args);
        SetBusy(false);

        if (code == 0)
        {
            Log("Done: " + dlg.FileName);
            MessageBox.Show("Export finished.");
        }
        else
        {
            Log($"ffmpeg exited with code {code}.");
        }
    }

    private async Task PreviewAsync()
    {
        string temp = Path.Combine(Path.GetTempPath(), "webm_muxer_preview_" + Guid.NewGuid().ToString("N") + ".webm");
        var args = BuildArgs(temp);
        if (args == null) return;

        txtLog.Clear();
        Log("Building preview...");
        SetBusy(true);
        int code = await RunAsync(FfmpegExe, args);

        if (code == 0)
        {
            string ffplay = SiblingTool("ffplay");
            Log("Playing preview with ffplay (close the player window to continue)...");
            await RunAsync(ffplay, new[] { "-autoexit", "-loglevel", "error", temp });
        }
        else
        {
            Log($"ffmpeg exited with code {code}.");
        }

        SetBusy(false);
        try { File.Delete(temp); } catch { /* ignore */ }
    }
}
