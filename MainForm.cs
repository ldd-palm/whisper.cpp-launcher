using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace WhisperLauncher;

public partial class MainForm : Form
{
    // ── State ─────────────────────────────────────────────────────────────────
    private readonly TranscribeEngine _engine = new();
    private CancellationTokenSource?  _cts;
    private string _iniPath = string.Empty;

    private static readonly HashSet<string> MediaExts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4",".mkv",".mov",".avi",".wmv",".flv",".webm",".m4v",".ts",
            ".mp3",".wav",".flac",".aac",".ogg",".m4a",".wma",".opus",".aiff"
        };

    // ── Default config.ini content (item 9 — written if file absent) ──────────
    private const string DefaultIni = """
        ; ============================================================
        ; config.ini -- Whisper.cpp Launcher configuration file
        ; This file must be placed in the same directory as the exe.
        ; ============================================================
        [LastSession]
        WhisperPath  =
        FFmpegPath   =
        ModelPath    =
        SourcePath   =
        BatchMode    = false
        Language     = en
        OutputFormat = srt
        Platform     = CPU
        LogDetail    = false

        ; ============================================================
        ; Platform sections -- each platform has its own configuration
        ; DisplayName : Label shown in the Platform drop-down
        ; ExtraArgs   : Extra flags passed to whisper-cli, space-separated
        ; ============================================================
        [AdvancedArgs]
        ; Whisper-Cli Common flags:
        ; Entropy threshold (-et): segments above this value are treated as hallucinations and skipped
        ; Log-probability threshold (-lpt): set to -1.0 to disable
        ArgsCommon = -et 2.8 -lpt -1.0 --no-fallback
        ArgsTXT    = -otxt
        ArgsSRT    = -osrt
        ArgsVTT    = -ovtt
        ; FFmpeg audio extraction args, placed between -i <input> and <output.wav>
        ; Full call: ffmpeg -y -i <input> [ArgsFfmpeg] <output.wav>
        ArgsFfmpeg = -ar 16000 -ac 1 -c:a pcm_s16le -loglevel error

        [Vulkan]
        DisplayName = Vulkan GPU
        ExtraArgs   = --device 0 -t 4

        [CUDA]
        DisplayName = CUDA GPU
        ExtraArgs   = --device 0 -t 4

        [OpenVINO]
        DisplayName = OpenVINO NPU
        ExtraArgs   = --ov-e-device NPU

        [CPU]
        DisplayName = CPU AVX2
        ExtraArgs   =

        [Filter]
        ; File extensions to skip when scanning a folder (comma-separated, lowercase)
        SkipExtensions = .srt,.txt,.vtt,.log,.wav,.ini
        ; Marker string used in temporary WAV filenames -- files containing this string are skipped
        TempMark       = _whisper_temp

        ; ============================================================
        ; Download links for required tools and GGML model files
        ; Recommended models (place in the same folder as whisper-cli.exe):
        ;   ggml-medium.en.bin       - fast, English-only, good accuracy
        ;   ggml-large-v3-turbo.bin  - best accuracy, supports all languages
        ; ============================================================
        [Links]
        FFmpegUrl  = https://ffmpeg.org/download.html#build-windows
        WhisperUrl = https://github.com/ggml-org/whisper.cpp
        GgmlUrl    = https://huggingface.co/ggerganov/whisper.cpp
        """;

    // ─────────────────────────────────────────────────────────────────────────
    //  Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public MainForm()
    {
        InitializeComponent();
        WireEvents();

        _iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
        _engine.SetLogFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                        "whisper_process.log"));
        this.Load += OnFormLoad;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Wire events
    // ─────────────────────────────────────────────────────────────────────────

    private void WireEvents()
    {
        btnBrowseWhisper.Click += OnBrowseWhisper;
        btnBrowseModel.Click   += OnBrowseModel;
        btnBrowseFFmpeg.Click  += OnBrowseFFmpeg;
        btnBrowseSource.Click  += OnBrowseSource;

        txtWhisperPath.Leave += async (_, _) => await ValidateWhisperAsync();
        txtModelPath.Leave   += (_, _) => ValidateModel();
        txtFFmpegPath.Leave  += (_, _) => ValidateFFmpeg();

        rdoSingleFile.CheckedChanged  += OnSourceModeChanged;
        rdoBatchFolder.CheckedChanged += OnSourceModeChanged;
        txtSourcePath.Leave           += OnSourcePathChanged;
        txtSourcePath.DragEnter       += OnDragEnter;
        txtSourcePath.DragDrop        += OnDragDrop;

        btnRun.Click        += OnRun;
        btnStop.Click       += OnStop;
        btnEditConfig.Click += OnEditConfig;   // item 7

        picFFmpeg.Click  += (_, _) => OpenUrl("FFmpegUrl");
        picWhisper.Click += (_, _) => OpenUrl("WhisperUrl");
        picGgml.Click    += (_, _) => OpenUrl("GgmlUrl");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Form Load
    // ─────────────────────────────────────────────────────────────────────────

    private async void OnFormLoad(object? sender, EventArgs e)
    {
        EnsureConfigExists();
        LoadLogosInstance();           // item 8 — embedded resources
        LoadAppIcon();

        PopulatePlatforms();
        RestoreLastSession();
        ValidateModel();
        AutoDetectFFmpeg();
        OnSourceModeChanged(null, EventArgs.Empty);

        // item 1 — default to launcher directory
        if (string.IsNullOrWhiteSpace(txtWhisperPath.Text))
        {
            string defaultExe = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "whisper-cli.exe");
            if (File.Exists(defaultExe))
                txtWhisperPath.Text = defaultExe;
        }

        if (!string.IsNullOrWhiteSpace(txtWhisperPath.Text))
            await ValidateWhisperAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Embedded-resource logo loading (item 8)
    // ─────────────────────────────────────────────────────────────────────────

    private void LoadLogosInstance()
    {
        LoadEmbeddedImage(picFFmpeg,  "ffmpeg_logo.png");
        LoadEmbeddedImage(picWhisper, "whisper_logo.png");
        LoadEmbeddedImage(picGgml,    "gglm_logo.png");
    }

    private static void LoadEmbeddedImage(PictureBox pic, string resourceName)
    {
        var asm    = Assembly.GetExecutingAssembly();
        var stream = asm.GetManifestResourceStream($"WhisperLauncher.{resourceName}");
        if (stream == null) return;
        var ms = new MemoryStream();
        stream.CopyTo(ms);
        stream.Dispose();
        ms.Position = 0;
        try { pic.Image = Image.FromStream(ms); } catch { /* ignore */ }
    }

    private void LoadAppIcon()
    {
        try
        {
            this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch { /* use default */ }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  INI helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void EnsureConfigExists()
    {
        if (!File.Exists(_iniPath))
            File.WriteAllText(_iniPath, DefaultIni, System.Text.Encoding.UTF8);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Platform population (item 2)
    // ─────────────────────────────────────────────────────────────────────────

    private void PopulatePlatforms()
    {
        string? current = cboPlatform.SelectedItem?.ToString();
        cboPlatform.Items.Clear();

        foreach (string section in IniManager.GetSections(_iniPath))
        {
            string dn = IniManager.Read(section, "DisplayName", string.Empty, _iniPath);
            if (!string.IsNullOrWhiteSpace(dn))
                cboPlatform.Items.Add(section);
        }

        if (cboPlatform.Items.Count == 0)
            cboPlatform.Items.Add("CPU");

        int idx    = current != null ? cboPlatform.Items.IndexOf(current) : -1;
        int cpuIdx = cboPlatform.Items.IndexOf("CPU");
        cboPlatform.SelectedIndex = idx >= 0 ? idx : (cpuIdx >= 0 ? cpuIdx : 0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  INI: restore / save last session
    // ─────────────────────────────────────────────────────────────────────────

    private void RestoreLastSession()
    {
        if (!File.Exists(_iniPath)) return;

        string R(string key, string def)
            => IniManager.Read("LastSession", key, def, _iniPath);

        txtWhisperPath.Text = R("WhisperPath", string.Empty);
        txtModelPath.Text   = R("ModelPath",   string.Empty);
        txtFFmpegPath.Text  = R("FFmpegPath",  string.Empty);
        txtSourcePath.Text  = R("SourcePath",  string.Empty);

        bool batch = IniManager.ReadBool("LastSession", "BatchMode", false, _iniPath);
        rdoBatchFolder.Checked = batch;
        rdoSingleFile.Checked  = !batch;

        string lang = R("Language", "en");
        int li = cboLanguage.Items.IndexOf(lang);
        cboLanguage.SelectedIndex = li >= 0 ? li : cboLanguage.Items.IndexOf("en");

        string fmt = R("OutputFormat", "srt");
        rdoSRT.Checked = fmt == "srt";
        rdoTXT.Checked = fmt == "txt";
        rdoVTT.Checked = fmt == "vtt";
        if (!rdoTXT.Checked && !rdoVTT.Checked) rdoSRT.Checked = true;

        chkDetail.Checked = IniManager.ReadBool("LastSession", "LogDetail", false, _iniPath);

        string plat = R("Platform", "CPU");
        int pi = cboPlatform.Items.IndexOf(plat);
        if (pi >= 0) cboPlatform.SelectedIndex = pi;

        if (!string.IsNullOrWhiteSpace(txtSourcePath.Text)) RefreshBatchList();
    }

    private void SaveLastSession()
    {
        void W(string key, string val) => IniManager.Write("LastSession", key, val, _iniPath);
        W("WhisperPath",  txtWhisperPath.Text);
        W("ModelPath",    txtModelPath.Text);
        W("FFmpegPath",   txtFFmpegPath.Text);
        W("SourcePath",   txtSourcePath.Text);
        W("BatchMode",    rdoBatchFolder.Checked ? "true" : "false");
        W("Language",     cboLanguage.SelectedItem?.ToString() ?? "en");
        W("OutputFormat", rdoTXT.Checked ? "txt" : rdoVTT.Checked ? "vtt" : "srt");
        W("Platform",     cboPlatform.SelectedItem?.ToString() ?? "CPU");
        W("LogDetail",    chkDetail.Checked ? "true" : "false");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Environment validation  (item 1 — just file-exists check, no version)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task ValidateWhisperAsync()
    {
        string path = txtWhisperPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
            { SetStatus(lblWhisperStatus, "Not set", Color.Gray); return; }
        if (!File.Exists(path))
            { SetStatus(lblWhisperStatus, "✗ File not found", Color.Red); return; }

        SetStatus(lblWhisperStatus, "✓ " + path, Color.Green);
        PopulatePlatforms();
        await Task.CompletedTask;
    }

    private void AutoDetectFFmpeg()
    {
        string? found = FindInPath("ffmpeg.exe");
        if (found != null)
        {
            txtFFmpegPath.Text = found;
            SetStatus(lblFFmpegStatus, "✓ Found in system PATH", Color.Green);
            return;
        }
        // Fall back to last-session value
        string saved = IniManager.Read("LastSession", "FFmpegPath", string.Empty, _iniPath);
        if (!string.IsNullOrWhiteSpace(saved) && File.Exists(saved))
        {
            txtFFmpegPath.Text = saved;
            SetStatus(lblFFmpegStatus, "✓ Found (from config)", Color.Green);
            return;
        }
        if (string.IsNullOrWhiteSpace(txtFFmpegPath.Text))
            SetStatus(lblFFmpegStatus, "✗ Not found — please browse", Color.Red);
    }

    private void ValidateFFmpeg()
    {
        string path = txtFFmpegPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
            { SetStatus(lblFFmpegStatus, "Not set", Color.Gray); return; }
        bool ok = File.Exists(path) || FindInPath("ffmpeg.exe") != null;
        SetStatus(lblFFmpegStatus,
            ok ? "✓ OK" : "✗ File not found",
            ok ? Color.Green : Color.Red);
    }

    private void ValidateModel()
    {
        string path = txtModelPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
            { SetStatus(lblModelStatus, "Not set", Color.Gray); return; }
        bool ok = File.Exists(path);
        SetStatus(lblModelStatus,
            ok ? "✓ " + path : "✗ File not found — please browse",
            ok ? Color.Green : Color.Red);
    }

    private static string? FindInPath(string fileName)
    {
        string? envPath = Environment.GetEnvironmentVariable("PATH");
        if (envPath == null) return null;
        foreach (string dir in envPath.Split(Path.PathSeparator))
        {
            string full = Path.Combine(dir, fileName);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    private static void SetStatus(Label lbl, string text, Color color)
        => (lbl.Text, lbl.ForeColor) = (text, color);

    // ─────────────────────────────────────────────────────────────────────────
    //  Browse handlers
    // ─────────────────────────────────────────────────────────────────────────

    private async void OnBrowseWhisper(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "Select whisper-cli.exe",
            Filter = "whisper-cli|whisper-cli.exe|Executables|*.exe|All files|*.*",
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            txtWhisperPath.Text = dlg.FileName;
            await ValidateWhisperAsync();
        }
    }

    private void OnBrowseModel(object? sender, EventArgs e)
    {
        string startDir = string.IsNullOrWhiteSpace(txtModelPath.Text)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Path.GetDirectoryName(txtModelPath.Text) ?? string.Empty;

        using var dlg = new OpenFileDialog
        {
            Title            = "Select GGML Model (.bin)",
            Filter           = "GGML Models|*.bin|All files|*.*",
            InitialDirectory = Directory.Exists(startDir) ? startDir : string.Empty,
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            txtModelPath.Text = dlg.FileName;
            ValidateModel();
        }
    }

    private void OnBrowseFFmpeg(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "Select ffmpeg.exe",
            Filter = "ffmpeg|ffmpeg.exe|Executables|*.exe|All files|*.*",
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            txtFFmpegPath.Text = dlg.FileName;
            ValidateFFmpeg();
        }
    }

    private void OnBrowseSource(object? sender, EventArgs e)
    {
        if (rdoBatchFolder.Checked)   // item 4 — batch folder selection
        {
            using var dlg = new FolderBrowserDialog
            {
                Description            = "Select folder containing media files",
                UseDescriptionForTitle = true,
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                txtSourcePath.Text = dlg.SelectedPath;
                RefreshBatchList();
            }
        }
        else
        {
            using var dlg = new OpenFileDialog
            {
                Title  = "Select media file",
                Filter = "Media files|*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.flv;*.webm;*.m4v;" +
                         "*.ts;*.mp3;*.wav;*.flac;*.aac;*.ogg;*.m4a;*.wma;*.opus;*.aiff" +
                         "|All files|*.*",
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                txtSourcePath.Text = dlg.FileName;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Drag & Drop
    // ─────────────────────────────────────────────────────────────────────────

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;
        string dropped = files[0];
        txtSourcePath.Text = dropped;
        if (Directory.Exists(dropped)) { rdoBatchFolder.Checked = true; RefreshBatchList(); }
        else rdoSingleFile.Checked = true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Source mode toggle (item 4 — batch folder logic)
    // ─────────────────────────────────────────────────────────────────────────

    private void OnSourceModeChanged(object? sender, EventArgs e)
    {
        bool batch = rdoBatchFolder.Checked;
        // Row 2 of tlpMedia is the batch file list (row 0=source, 1=radios, 2=list)
        // LogicalToDeviceUnits converts design-time pixels to the actual device DPI so that
        // the programmatic height override doesn't undo what AutoScaleMode.Dpi already applied.
        int listH = batch ? LogicalToDeviceUnits(76) : 0;
        tlpMedia.RowStyles[2] = new RowStyle(SizeType.Absolute, listH);
        pnlMedia.Height       = batch ? LogicalToDeviceUnits(166) : LogicalToDeviceUnits(90);
        if (batch) RefreshBatchList();
    }

    private void OnSourcePathChanged(object? sender, EventArgs e)
    {
        if (rdoBatchFolder.Checked) RefreshBatchList();
    }

    private void RefreshBatchList()
    {
        lstFiles.Items.Clear();
        string dir = txtSourcePath.Text.Trim();
        if (!Directory.Exists(dir)) return;

        string skipMark = IniManager.Read("Filter", "TempMark", "_whisper_temp", _iniPath);
        string skipExts = IniManager.Read("Filter", "SkipExtensions",
                          ".srt,.txt,.vtt,.log,.wav,.ini", _iniPath);
        var skip = new HashSet<string>(
            skipExts.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var files = Directory.GetFiles(dir)
            .Select(Path.GetFileName).Where(f => f != null &&
                !f.Contains(skipMark, StringComparison.OrdinalIgnoreCase) &&
                !skip.Contains(Path.GetExtension(f)!) &&
                MediaExts.Contains(Path.GetExtension(f)!))
            .OrderBy(f => f).ToList();

        foreach (var f in files) lstFiles.Items.Add(f!);
        if (files.Count == 0) lstFiles.Items.Add("(no media files found)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Command preview builder (item 6)
    // ─────────────────────────────────────────────────────────────────────────

    private static string BuildCommandPreview(TranscribeOptions opts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("  [FFmpeg]  "
            + $"{Path.GetFileName(opts.FFmpegExe)}"
            + " -y -i <InputFile> -ar 16000 -ac 1 -c:a pcm_s16le <TempWAV>"
            + " -loglevel error");

        sb.Append("  [Whisper] " + Path.GetFileName(opts.WhisperExe));
        sb.Append($" -m \"{Path.GetFileName(opts.ModelPath)}\"");
        sb.Append(" -f <TempWAV>");
        sb.Append($" {opts.FormatArg}");
        sb.Append($" -l {opts.Language}");
        if (!string.IsNullOrWhiteSpace(opts.CommonArgs)) sb.Append($" {opts.CommonArgs}");
        if (!string.IsNullOrWhiteSpace(opts.ExtraArgs))  sb.Append($" {opts.ExtraArgs}");
        sb.Append(" -of <OutputFile>");
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Run
    // ─────────────────────────────────────────────────────────────────────────

    private async void OnRun(object? sender, EventArgs e)
    {
        if (!ValidateInputs(out var files, out var opts)) return;

        SaveLastSession();
        SetRunning(true);

        // item 6 — show assembled command in red
        AppendLog(new string('─', 53) + "\n", Color.DimGray);
        AppendLog($"Started  {DateTime.Now:yyyy/MM/dd HH:mm:ss}\n", Color.Cyan);
        AppendLog("Command preview:\n" + BuildCommandPreview(opts) + "\n", Color.OrangeRed);

        _cts = new CancellationTokenSource();
        var progress = new Progress<LogEntry>(AppendLogEntry);

        try
        {
            await _engine.RunAsync(files, opts, progress, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            AppendLog("\n[Stopped by user]\n", Color.Orange);
        }
        catch (Exception ex)
        {
            AppendLog($"\n[Unhandled Error] {ex.Message}\n", Color.Red);
        }
        finally
        {
            _cts.Dispose(); _cts = null;
            SetRunning(false);
        }
    }

    private bool ValidateInputs(out IReadOnlyList<string> files, out TranscribeOptions opts)
    {
        files = Array.Empty<string>();
        opts  = null!;

        if (!File.Exists(txtWhisperPath.Text.Trim()))
        {
            MessageBox.Show("whisper-cli.exe not found.\nPlease set a valid Whisper-CLI Path.",
                "Missing dependency", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (!File.Exists(txtModelPath.Text.Trim()))
        {
            MessageBox.Show("GGML model file not found.\nPlease select a .bin model.",
                "Missing model", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        string ffmpegPath = txtFFmpegPath.Text.Trim();
        if (!File.Exists(ffmpegPath))
            ffmpegPath = FindInPath("ffmpeg.exe") ?? ffmpegPath;
        if (!File.Exists(ffmpegPath))
        {
            MessageBox.Show("FFmpeg not found.\nPlease set a valid FFmpeg path.",
                "Missing dependency", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        string src = txtSourcePath.Text.Trim();
        if (rdoBatchFolder.Checked)
        {
            if (!Directory.Exists(src))
            {
                MessageBox.Show("Source folder not found.",
                    "Invalid path", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            string skipMark = IniManager.Read("Filter", "TempMark", "_whisper_temp", _iniPath);
            string skipExts = IniManager.Read("Filter", "SkipExtensions",
                              ".srt,.txt,.vtt,.log,.wav,.ini", _iniPath);
            var skip = new HashSet<string>(
                skipExts.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);

            files = Directory.GetFiles(src)
                .Where(f => !Path.GetFileName(f).Contains(skipMark,
                                StringComparison.OrdinalIgnoreCase) &&
                            !skip.Contains(Path.GetExtension(f)) &&
                            MediaExts.Contains(Path.GetExtension(f)))
                .OrderBy(f => f).ToList();

            if (files.Count == 0)
            {
                MessageBox.Show("No media files found in the selected folder.",
                    "Nothing to process", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
        }
        else
        {
            if (!File.Exists(src))
            {
                MessageBox.Show("Source file not found.",
                    "Invalid path", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            files = new[] { src };
        }

        // item 2 — read ExtraArgs for selected platform section
        string platform  = cboPlatform.SelectedItem?.ToString() ?? "CPU";
        string extraArgs    = IniManager.Read(platform,       "ExtraArgs",  string.Empty, _iniPath);
        string commonArgs   = IniManager.Read("AdvancedArgs", "ArgsCommon", string.Empty, _iniPath);
        string ffmpegArgs   = IniManager.Read("AdvancedArgs", "ArgsFfmpeg",
                                              "-ar 16000 -ac 1 -c:a pcm_s16le -loglevel error",
                                              _iniPath);

        string formatArg, outputExt, fmtKey;
        if (rdoTXT.Checked)      { formatArg = "-otxt"; outputExt = ".txt"; fmtKey = "ArgsTXT"; }
        else if (rdoVTT.Checked) { formatArg = "-ovtt"; outputExt = ".vtt"; fmtKey = "ArgsVTT"; }
        else                     { formatArg = "-osrt"; outputExt = ".srt"; fmtKey = "ArgsSRT"; }

        string iniFormatArg = IniManager.Read("AdvancedArgs", fmtKey, formatArg, _iniPath);
        if (!string.IsNullOrWhiteSpace(iniFormatArg)) formatArg = iniFormatArg;

        opts = new TranscribeOptions
        {
            WhisperExe    = txtWhisperPath.Text.Trim(),
            FFmpegExe     = ffmpegPath,
            ModelPath     = txtModelPath.Text.Trim(),
            PlatformLabel = IniManager.Read(platform, "DisplayName", platform, _iniPath),
            ExtraArgs     = extraArgs,
            CommonArgs    = commonArgs,
            FormatArg     = formatArg,
            OutputExt     = outputExt,
            FfmpegArgs    = ffmpegArgs,
            Language      = cboLanguage.SelectedItem?.ToString() ?? "en",
            DetailMode    = chkDetail.Checked,
        };
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Stop
    // ─────────────────────────────────────────────────────────────────────────

    private void OnStop(object? sender, EventArgs e)
    {
        _engine.Stop();
        _cts?.Cancel();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Edit Config (item 7)
    // ─────────────────────────────────────────────────────────────────────────

    private void OnEditConfig(object? sender, EventArgs e)
    {
        EnsureConfigExists();
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_iniPath}\"")
            { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot open Notepad:\n{ex.Message}",
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UI state
    // ─────────────────────────────────────────────────────────────────────────

    private void SetRunning(bool running)
    {
        btnRun.Enabled         = !running;
        btnStop.Enabled        =  running;
        btnEditConfig.Enabled  = !running;
        grpEnv.Enabled         = !running;
        pnlMedia.Enabled       = !running;
        pnlOpts.Enabled        = !running;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Log helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void AppendLogEntry(LogEntry entry)
    {
        Color color = entry.Level switch
        {
            LogLevel.Success   => Color.LimeGreen,
            LogLevel.Warning   => Color.Orange,
            LogLevel.Error     => Color.Tomato,
            LogLevel.Detail    => Color.DarkGray,
            LogLevel.Heartbeat => Color.DodgerBlue,
            _                  => Color.LightGray,
        };
        bool newLine = entry.Level != LogLevel.Heartbeat;
        AppendLog(entry.Message + (newLine ? "\n" : ""), color);
    }

    private void AppendLog(string text, Color color)
    {
        if (rtbLog.InvokeRequired) { rtbLog.Invoke(() => AppendLog(text, color)); return; }
        rtbLog.SelectionStart  = rtbLog.TextLength;
        rtbLog.SelectionLength = 0;
        rtbLog.SelectionColor  = color;
        rtbLog.AppendText(text);
        rtbLog.SelectionColor  = rtbLog.ForeColor;
        rtbLog.ScrollToCaret();
    }

    private void OpenUrl(string key)
    {
        string url = IniManager.Read("Links", key, string.Empty, _iniPath);
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            var r = MessageBox.Show("Transcription is in progress. Stop and exit?",
                "Confirm Exit", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) { e.Cancel = true; return; }
            OnStop(null, EventArgs.Empty);
        }
        SaveLastSession();
        base.OnFormClosing(e);
    }
}
