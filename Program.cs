using System.Media;

namespace VoiceFlow;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test")) { Tests.Run(args.Last()); return; }
        using var form = new MainForm();
        if (args.Contains("--ui-test"))
        {
            form.Shown += async (_, _) =>
            {
                try { await form.SmokeTest(args.Last()); }
                catch (Exception ex) { File.WriteAllText(args.Last(), ex.ToString()); Environment.ExitCode = 1; }
                finally { form.Close(); }
            };
            Application.Run(form); return;
        }
        if (args.Contains("--render-preview"))
        {
            form.Show(); Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(args.Last()); form.Close(); return;
        }
        Application.Run(form);
    }
}

public sealed class MainForm : Form
{
    readonly Color bg = Color.FromArgb(19, 23, 32), panel = Color.FromArgb(29, 35, 47), ink = Color.FromArgb(232, 237, 245);
    readonly Label fileLabel = new(), status = new(), stats = new();
    readonly WaveView waveform = new();
    readonly CheckedListBox cutList = new();
    readonly CheckBox silence = new() { Text = "Remove silence", Checked = true };
    readonly CheckBox breaths = new() { Text = "Detect breaths (experimental)", Checked = true };
    readonly CheckBox compression = new() { Text = "Compress voice dynamics", Checked = true };
    readonly CheckBox normalize = new() { Text = "Normalize volume", Checked = true };
    readonly CheckBox retroRadio = new() { Text = "Half-Life-style radio (heavy / gritty)", Checked = false };
    readonly NumericUpDown gate = Number(-70, -15, -42), minimum = Number(50, 2000, 180), padding = Number(0, 200, 35);
    readonly NumericUpDown strength = Number(0, 100, 40), threshold = Number(-40, -3, -20), ratio = Number(1, 10, 3);
    readonly NumericUpDown target = Number(-30, -8, -18);
    readonly List<Control> busyControls = new();
    readonly Button cancel;
    Audio? source, output;
    string? sourcePath;
    List<Cut> candidates = new();
    CancellationTokenSource? cancellation;
    SoundPlayer? player;
    MemoryStream? playback;
    bool updating, busy;

    static NumericUpDown Number(int min, int max, int value) => new() { Minimum = min, Maximum = max, Value = value, Width = 80 };
    public MainForm()
    {
        Text = "VoiceFlow • Speech studio";
        ClientSize = new Size(1120, 850); MinimumSize = new Size(980, 810);
        StartPosition = FormStartPosition.CenterScreen; BackColor = bg; ForeColor = ink;
        Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(24) };
        foreach (int height in new[] { 60, 52, 140 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        Controls.Add(root);
        var title = new Label { Text = "VoiceFlow", Font = new Font("Segoe UI", 26, FontStyle.Bold), Dock = DockStyle.Fill };
        root.Controls.Add(title, 0, 0);
        var files = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        files.Controls.Add(Button("Open WAV", async () => await Import(), true));
        fileLabel.Text = "Bring your voice into focus. Open a recording to begin.";
        fileLabel.AutoSize = true; fileLabel.Margin = new Padding(14, 11, 0, 0); files.Controls.Add(fileLabel);
        root.Controls.Add(files, 0, 1);
        waveform.Dock = DockStyle.Fill; waveform.Margin = new Padding(0, 0, 0, 16); root.Controls.Add(waveform, 0, 2);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 51)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 49));
        root.Controls.Add(body, 0, 3);
        var settings = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = panel, Padding = new Padding(12) };
        body.Controls.Add(settings, 0, 0);
        AddCheck(settings, silence); AddRow(settings, "Silence threshold (dBFS)", gate); AddRow(settings, "Minimum silence (ms)", minimum); AddRow(settings, "Keep at each speech edge (ms)", padding);
        AddCheck(settings, breaths); AddRow(settings, "Breath sensitivity", strength);
        AddCheck(settings, compression); AddRow(settings, "Compressor threshold (dBFS)", threshold); AddRow(settings, "Ratio (:1)", ratio);
        AddCheck(settings, normalize); AddRow(settings, "Target RMS (dBFS)", target);
        AddCheck(settings, retroRadio);
        var review = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(14, 0, 0, 0) };
        review.RowStyles.Add(new RowStyle(SizeType.Absolute, 72)); review.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); review.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        review.Controls.Add(new Label { Text = "REVIEW CUTS\nChecked sections will be removed. Select a cut to hear it with context. Quiet consonants may resemble breaths.", Dock = DockStyle.Fill }, 0, 0);
        cutList.Dock = DockStyle.Fill; cutList.BackColor = panel; cutList.ForeColor = ink; cutList.BorderStyle = BorderStyle.None; cutList.CheckOnClick = true;
        cutList.HorizontalScrollbar = true; cutList.IntegralHeight = false;
        cutList.ItemCheck += (_, _) => { if (!updating) InvalidateOutput("Cuts changed. Click Apply selected cuts to update the preview."); };
        busyControls.Add(cutList); review.Controls.Add(cutList, 0, 1);
        var reviewButtons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        reviewButtons.Controls.Add(Button("Hear selected cut", HearCut));
        reviewButtons.Controls.Add(Button("Apply selected cuts", async () => await Process(false)));
        review.Controls.Add(reviewButtons, 0, 2); body.Controls.Add(review, 1, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 9, 0, 0), WrapContents = false };
        actions.Controls.Add(Button("Analyze && process", async () => await Process(true), true));
        actions.Controls.Add(Button("Play original", () => Play(source)));
        actions.Controls.Add(Button("Play result", () => Play(output)));
        actions.Controls.Add(Button("Stop", Stop));
        actions.Controls.Add(Button("Export WAV", Export));
        cancel = Button("Cancel", () => cancellation?.Cancel()); cancel.Enabled = false; busyControls.Remove(cancel); actions.Controls.Add(cancel);
        root.Controls.Add(actions, 0, 4);
        var foot = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        foot.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); foot.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        stats.Dock = status.Dock = DockStyle.Fill; stats.Text = "Local processing  •  Mono / stereo WAV  •  16-bit WAV export";
        status.Text = "Ready. Your original recording is preserved."; status.ForeColor = Color.FromArgb(110, 220, 194);
        foot.Controls.Add(stats, 0, 0); foot.Controls.Add(status, 0, 1); root.Controls.Add(foot, 0, 5);
        FormClosing += (_, _) => { cancellation?.Cancel(); Stop(); };
    }

    Button Button(string text, Action action, bool primary = false)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 35, Padding = new Padding(8, 3, 8, 3), FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(95, 224, 189) : panel, ForeColor = primary ? bg : ink, Cursor = Cursors.Hand };
        b.FlatAppearance.BorderSize = 0; b.Click += (_, _) => action(); busyControls.Add(b); return b;
    }
    void AddCheck(FlowLayoutPanel parent, CheckBox check)
    {
        check.AutoSize = true; check.Margin = new Padding(0, 8, 0, 2); check.Font = new Font(Font, FontStyle.Bold);
        check.CheckedChanged += (_, _) => SettingsChanged(); busyControls.Add(check); parent.Controls.Add(check);
    }
    void AddRow(FlowLayoutPanel parent, string caption, NumericUpDown input)
    {
        var row = new FlowLayoutPanel { Width = 445, Height = 29, Margin = Padding.Empty, WrapContents = false };
        row.Controls.Add(new Label { Text = caption, Width = 290, Margin = new Padding(0, 3, 0, 0) });
        input.BackColor = bg; input.ForeColor = ink; input.BorderStyle = BorderStyle.FixedSingle;
        input.ValueChanged += (_, _) => SettingsChanged(); busyControls.Add(input); row.Controls.Add(input); parent.Controls.Add(row);
    }
    Settings Options() => new(silence.Checked, (double)gate.Value, (int)minimum.Value, (int)padding.Value, breaths.Checked,
        (int)strength.Value, compression.Checked, (double)threshold.Value, (double)ratio.Value, normalize.Checked, (double)target.Value, retroRadio.Checked);
    void SettingsChanged()
    {
        updating = true; cutList.Items.Clear(); candidates.Clear(); updating = false;
        InvalidateOutput("Settings changed. Click Analyze & process.");
    }
    void InvalidateOutput(string message) { output = null; waveform.Processed = null; waveform.Invalidate(); Stop(); status.Text = message; }
    void SetBusy(bool value)
    {
        busy = value;
        foreach (var c in busyControls) c.Enabled = !value;
        cancel.Enabled = value; UseWaitCursor = value;
    }
    async Task Import()
    {
        using var dialog = new OpenFileDialog { Filter = "WAV audio (*.wav)|*.wav", Title = "Open a voice recording" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        Stop(); SetBusy(true); cancel.Enabled = false; status.Text = "Reading audio…";
        try
        {
            var loaded = await Task.Run(() => Wave.Read(dialog.FileName));
            if (IsDisposed) return;
            source = loaded; sourcePath = dialog.FileName; output = null; candidates.Clear(); cutList.Items.Clear();
            waveform.Original = source; waveform.Processed = null; waveform.Invalidate();
            fileLabel.Text = $"{Path.GetFileName(sourcePath)}  •  {source.Seconds:0.0}s  •  {source.Rate:N0} Hz  •  {source.Channels} channel(s)";
            stats.Text = "Ready to process. Adjust the settings, then analyze your recording."; status.Text = "Recording loaded.";
        }
        catch (Exception ex) { if (!IsDisposed) Error(ex); }
        finally { if (!IsDisposed) SetBusy(false); }
    }
    async Task Process(bool analyze)
    {
        if (source == null) { status.Text = "Open a WAV recording first."; return; }
        if (busy) return;
        Stop(); var options = Options(); var audio = source;
        var selected = cutList.CheckedIndices.Cast<int>().Select(i => candidates[i]).ToList();
        using var cts = new CancellationTokenSource(); cancellation = cts; SetBusy(true);
        status.Text = analyze ? "Detecting pauses and breath candidates…" : "Applying your selected cuts…";
        try
        {
            var finished = await Task.Run(() =>
            {
                var cuts = analyze ? Dsp.Analyze(audio, options, cts.Token) : selected;
                return (Cuts: cuts, Result: Dsp.Process(audio, options, cuts, cts.Token));
            }, cts.Token);
            if (IsDisposed) return;
            if (analyze)
            {
                candidates = finished.Cuts; updating = true; cutList.Items.Clear();
                foreach (var c in candidates) cutList.Items.Add($"{c.Start / (double)audio.Rate:0.00}–{c.End / (double)audio.Rate:0.00}s  •  {c.Kind}", true);
                updating = false;
            }
            output = finished.Result.Audio; waveform.Processed = output; waveform.Invalidate();
            stats.Text = $"{audio.Seconds:0.00}s → {output.Seconds:0.00}s  •  Saved {audio.Seconds - output.Seconds:0.00}s  •  Peak {finished.Result.PeakDb:0.0} dBFS  •  RMS {finished.Result.RmsDb:0.0} dBFS";
            status.Text = finished.Result.Limited ? "Ready. Normalization gain was capped to keep peaks at −1 dBFS. Review cuts before exporting." : "Ready. Listen to the result and review cuts before exporting.";
        }
        catch (OperationCanceledException) { if (!IsDisposed) status.Text = "Processing canceled."; }
        catch (Exception ex) { if (!IsDisposed) Error(ex); }
        finally { cancellation = null; if (!IsDisposed) SetBusy(false); }
    }
    void Play(Audio? audio)
    {
        if (audio == null) { status.Text = "Load or process audio before playing it."; return; }
        try { Stop(); playback = new MemoryStream(); Wave.Write(playback, audio); playback.Position = 0;
            player = new SoundPlayer(playback); player.Load(); player.Play(); status.Text = "Playing. Use Stop to end playback."; }
        catch (Exception ex) { Error(ex); }
    }
    void Stop() { player?.Stop(); player?.Dispose(); player = null; playback?.Dispose(); playback = null; }
    void HearCut()
    {
        if (source == null || cutList.SelectedIndex < 0) { status.Text = "Select a detected cut first."; return; }
        var c = candidates[cutList.SelectedIndex]; int start = Math.Max(0, c.Start - source.Rate / 2), end = Math.Min(source.Frames, c.End + source.Rate / 2);
        Play(new Audio(source.Samples[(start * source.Channels)..(end * source.Channels)], source.Rate, source.Channels));
    }
    void Export()
    {
        if (output == null) { status.Text = "Process your recording before exporting."; return; }
        using var dialog = new SaveFileDialog { Filter = "16-bit PCM WAV (*.wav)|*.wav", DefaultExt = "wav", AddExtension = true, FileName = Path.GetFileNameWithoutExtension(sourcePath) + "-clean.wav" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        if (string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(sourcePath!), StringComparison.OrdinalIgnoreCase))
        { status.Text = "Choose a different filename to preserve the original recording."; return; }
        try { Wave.Save(dialog.FileName, output); status.Text = "Exported: " + dialog.FileName; }
        catch (Exception ex) { Error(ex); }
    }
    void Error(Exception ex) { status.Text = ex.Message; MessageBox.Show(this, ex.Message, "VoiceFlow", MessageBoxButtons.OK, MessageBoxIcon.Information); }

    internal async Task SmokeTest(string logPath)
    {
        var samples = new float[48000];
        for (int i = 0; i < samples.Length; i++)
            if (i < 16000 || i >= 32000) samples[i] = .25f * (float)Math.Sin(i * 2 * Math.PI * 220 / 16000);
        source = new Audio(samples, 16000, 1); waveform.Original = source;
        fileLabel.Text = "Test recording • 3.0s • 16,000 Hz • Mono";
        await Process(true);
        if (output == null || candidates.Count != 1 || output.Seconds >= 2.2) throw new Exception("UI processing failed.");
        using (var bitmap = new Bitmap(Width, Height))
        {
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(Path.ChangeExtension(logPath, ".png"));
        }
        cutList.SetItemChecked(0, false);
        if (output != null) throw new Exception("Changing cuts did not invalidate the old result.");
        await Process(false);
        if (output == null || output.Frames != source.Frames) throw new Exception("Unchecking a cut did not restore speech duration.");
        string exported = Path.ChangeExtension(logPath, ".wav");
        Wave.Save(exported, output);
        if (Wave.Read(exported).Frames != output.Frames) throw new Exception("UI result export failed.");
        File.Delete(exported);
        var cleanSamples = output.Samples.ToArray();
        retroRadio.Checked = true;
        if (output != null || !Options().RetroRadio) throw new Exception("Radio checkbox did not update settings and invalidate output.");
        await Process(false);
        if (output == null || output.Frames != source.Frames || output.Samples.SequenceEqual(cleanSamples))
            throw new Exception("Radio checkbox did not apply the effect.");
        retroRadio.Checked = false;
        await Process(false);
        if (output == null || !output.Samples.SequenceEqual(cleanSamples)) throw new Exception("Disabling radio did not restore normal processing.");
        File.WriteAllText(logPath, "PASS UI analyze/process, cut review, stale result invalidation, reprocessing, and exported WAV roundtrip.");
    }
}

public sealed class WaveView : Control
{
    public Audio? Original { get; set; }
    public Audio? Processed { get; set; }
    public WaveView() { DoubleBuffered = true; BackColor = Color.FromArgb(25, 31, 42); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics;
        if (Original == null)
        {
            using var brush = new SolidBrush(Color.FromArgb(138, 153, 174));
            g.DrawString("YOUR RECORDING\n\nOpen a WAV file to compare the original and processed waveforms.", Font, brush, 20, 20); return;
        }
        Draw(g, Original, 0, "ORIGINAL", Color.FromArgb(128, 151, 188));
        if (Processed != null) Draw(g, Processed, Height / 2, "PROCESSED", Color.FromArgb(95, 224, 189));
    }
    void Draw(Graphics g, Audio a, int top, string label, Color color)
    {
        using var pen = new Pen(color); using var brush = new SolidBrush(color);
        g.DrawString(label, Font, brush, 10, top + 5);
        int left = 120, width = Math.Max(1, Width - left - 15), half = Height / 4 - 8, center = top + Height / 4;
        for (int x = 0; x < width; x++)
        {
            int start = (int)((long)x * a.Frames / width), end = Math.Min(a.Frames, Math.Max(start + 1, (int)((long)(x + 1) * a.Frames / width)));
            float peak = 0;
            for (int i = start; i < end; i += Math.Max(1, (end - start) / 96))
                for (int c = 0; c < a.Channels; c++) peak = Math.Max(peak, Math.Abs(a.Samples[i * a.Channels + c]));
            g.DrawLine(pen, left + x, center - peak * half, left + x, center + peak * half);
        }
    }
}
