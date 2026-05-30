using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WhisperLauncher;

// ── Log messaging ─────────────────────────────────────────────────────────────

public enum LogLevel { Info, Success, Warning, Error, Detail, Heartbeat }

public sealed record LogEntry(LogLevel Level, string Message);

// ── Options passed to the engine ─────────────────────────────────────────────

public sealed class TranscribeOptions
{
    public required string WhisperExe    { get; init; }
    public required string FFmpegExe     { get; init; }
    public required string ModelPath     { get; init; }
    public required string PlatformLabel { get; init; }
    public required string ExtraArgs     { get; init; }  // platform-specific
    public required string CommonArgs    { get; init; }  // [AdvancedArgs].ArgsCommon
    public required string FormatArg     { get; init; }  // -osrt / -otxt / -ovtt
    public required string OutputExt     { get; init; }  // .srt  / .txt  / .vtt
    public required string FfmpegArgs    { get; init; }  // [AdvancedArgs].ArgsFfmpeg
    public required string Language      { get; init; }
    public          bool   DetailMode    { get; init; }
}

// ── Engine ────────────────────────────────────────────────────────────────────

public sealed class TranscribeEngine
{
    // ── Sleep-prevention (ES_SYSTEM_REQUIRED only — screen may still turn off)
    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);
    private const uint ES_CONTINUOUS      = 0x80000000u;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001u;

    // ── Current child process (so Stop() can kill it)
    private volatile Process? _activeProcess;

    // ── Log file path (set once from MainForm)
    private string _logFile = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "whisper_process.log");

    public void SetLogFile(string path) => _logFile = path;

    // ─────────────────────────────────────────────────────────────────────────
    //  Main entry point
    // ─────────────────────────────────────────────────────────────────────────

    public async Task RunAsync(
        IReadOnlyList<string>   files,
        TranscribeOptions       opts,
        IProgress<LogEntry>     progress,
        CancellationToken       ct)
    {
        SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);

        var   totalTimer = Stopwatch.StartNew();
        int   succeeded  = 0;
        int   failed     = 0;

        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                string inputFile = files[i];
                string dir       = Path.GetDirectoryName(inputFile)
                                   ?? AppDomain.CurrentDomain.BaseDirectory;
                string baseName  = Path.GetFileNameWithoutExtension(inputFile);
                string tempWav   = Path.Combine(dir, baseName + "_whisper_temp.wav");
                string outPrefix = Path.Combine(dir, baseName);
                string finalOut  = outPrefix + opts.OutputExt;

                progress.Report(new(LogLevel.Info,
                    $"\n[{i + 1}/{files.Count}] {inputFile}"));

                var fileTimer = Stopwatch.StartNew();

                // ── Step 1: FFmpeg audio extraction ──────────────────────────
                progress.Report(new(LogLevel.Info, " → [1/2] Extracting audio..."));

                string ffArgs = $"-y -i \"{inputFile}\" {opts.FfmpegArgs} \"{tempWav}\"";

                bool ffOk = await RunProcessAsync(opts.FFmpegExe, ffArgs,
                                                  opts.DetailMode, progress, ct);
                if (ct.IsCancellationRequested)
                {
                    TryDelete(tempWav);
                    break;
                }
                if (!ffOk)
                {
                    progress.Report(new(LogLevel.Error,
                        " [FAILED] FFmpeg could not extract audio."));
                    TryDelete(tempWav);
                    failed++;
                    continue;
                }
                progress.Report(new(LogLevel.Success, " Done."));

                // ── Step 2: whisper-cli inference ────────────────────────────
                ct.ThrowIfCancellationRequested();
                progress.Report(new(LogLevel.Info,
                    $" → [2/2] Transcribing ({opts.PlatformLabel})..."));

                // Build the full argument string
                var whisperArgs = new StringBuilder();
                whisperArgs.Append($"-m \"{opts.ModelPath}\"");
                whisperArgs.Append($" -f \"{tempWav}\"");
                whisperArgs.Append($" {opts.FormatArg}");
                whisperArgs.Append($" -l {opts.Language}");
                if (!string.IsNullOrWhiteSpace(opts.CommonArgs))
                    whisperArgs.Append($" {opts.CommonArgs}");
                if (!string.IsNullOrWhiteSpace(opts.ExtraArgs))
                    whisperArgs.Append($" {opts.ExtraArgs}");
                whisperArgs.Append($" -of \"{outPrefix}\"");

                bool wpOk = await RunProcessAsync(opts.WhisperExe, whisperArgs.ToString(),
                                                  opts.DetailMode, progress, ct);
                TryDelete(tempWav);

                string elapsed = FormatElapsed(fileTimer.Elapsed);

                if (ct.IsCancellationRequested) break;

                if (!wpOk)
                {
                    progress.Report(new(LogLevel.Error,
                        $" [FAILED] Transcription error. ({elapsed})"));
                    failed++;
                    continue;
                }

                if (File.Exists(finalOut))
                {
                    progress.Report(new(LogLevel.Success, $" Done.  ({elapsed})"));
                    progress.Report(new(LogLevel.Success, $"   ↳ Output : {finalOut}"));
                    succeeded++;
                }
                else
                {
                    string msg = $"whisper-cli exited 0 but output not found: {finalOut}";
                    progress.Report(new(LogLevel.Warning, $" [WARNING] {msg}"));
                    WriteErrorLog($"[System Warning] {msg}");
                    failed++;
                }
            }
        }
        finally
        {
            SetThreadExecutionState(ES_CONTINUOUS); // restore sleep

            string total = FormatElapsed(totalTimer.Elapsed);
            progress.Report(new(LogLevel.Info, new string('─', 53)));

            if (ct.IsCancellationRequested)
                progress.Report(new(LogLevel.Warning,
                    $" Stopped by user.  " +
                    $"Succeeded: {succeeded}  Failed/Skipped: {failed}  " +
                    $"Total: {total}"));
            else
                progress.Report(new(LogLevel.Success,
                    $" All done!  " +
                    $"Succeeded: {succeeded}  Failed: {failed}  " +
                    $"Total: {total}"));

            progress.Report(new(LogLevel.Info, new string('─', 53)));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Stop — kills the currently active child process
    // ─────────────────────────────────────────────────────────────────────────

    public void Stop()
    {
        try { _activeProcess?.Kill(entireProcessTree: true); } catch { }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Core process runner
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<bool> RunProcessAsync(
        string              exe,
        string              args,
        bool                detailMode,
        IProgress<LogEntry> progress,
        CancellationToken   ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = exe,
            Arguments              = args,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };

        using var proc = new Process { StartInfo = psi };

        try
        {
            proc.Start();
            _activeProcess = proc;

            // Always drain stdout (discard) to prevent pipe-buffer deadlock
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(CancellationToken.None);

            // Stderr: collect for error logging; optionally stream to UI
            var stderrBuf = new StringBuilder();
            Task stderrTask;

            if (detailMode)
            {
                stderrTask = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await proc.StandardError
                                            .ReadLineAsync(CancellationToken.None)) != null)
                    {
                        stderrBuf.AppendLine(line);
                        progress.Report(new(LogLevel.Detail, "    " + line));
                    }
                }, CancellationToken.None);
            }
            else
            {
                stderrTask = Task.Run(async () =>
                {
                    string text = await proc.StandardError
                                           .ReadToEndAsync(CancellationToken.None);
                    stderrBuf.Append(text);
                }, CancellationToken.None);
            }

            // ── Wait with heartbeat (summary) or plain wait (detail) ─────────
            if (!detailMode)
            {
                // Heartbeat: report "." every 5 s while process is running
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var waitTask = WaitForExitAsync(proc, cts.Token);

                while (!waitTask.IsCompleted)
                {
                    var delay = Task.Delay(5_000, ct);
                    await Task.WhenAny(waitTask, delay);
                    if (!waitTask.IsCompleted && !ct.IsCancellationRequested)
                        progress.Report(new(LogLevel.Heartbeat, "."));
                }

                await waitTask; // re-throw OCE if cancelled
            }
            else
            {
                await WaitForExitAsync(proc, ct);
            }

            await stderrTask;
            await stdoutTask;

            int exitCode = proc.ExitCode;

            if (exitCode != 0)
                WriteErrorLog($"[Process Error] {exe}\nArgs: {args}\n" +
                              $"ExitCode: {exitCode}\nStderr:\n{stderrBuf}");

            return exitCode == 0;
        }
        catch (OperationCanceledException)
        {
            // Kill was already issued via Stop() or the ct.Register below
            try
            {
                if (!proc.HasExited) proc.Kill(entireProcessTree: true);
            }
            catch { }
            // Drain streams so the child can actually exit
            try { await proc.StandardOutput.ReadToEndAsync(CancellationToken.None); } catch { }
            try { await proc.StandardError.ReadToEndAsync(CancellationToken.None); }  catch { }
            return false;
        }
        finally
        {
            if (_activeProcess == proc) _activeProcess = null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  WaitForExitAsync with kill-on-cancel
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task WaitForExitAsync(Process proc, CancellationToken ct)
    {
        // Register kill callback so cancellation actually terminates the process
        using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
        });

        // Wait for process exit (ignoring ct here — kill has already been issued)
        await Task.Run(() => proc.WaitForExit(), CancellationToken.None);
        ct.ThrowIfCancellationRequested();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string FormatElapsed(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
        if (ts.TotalMinutes >= 1)
            return $"{ts.Minutes}m {ts.Seconds}s";
        return $"{ts.Seconds}s";
    }

    private void WriteErrorLog(string message)
    {
        try
        {
            string header  = new string('─', 53);
            string entry   = $"{header}\r\n[{DateTime.Now:yyyy/MM/dd HH:mm:ss}]\r\n{message}\r\n";
            string existing = File.Exists(_logFile) ? File.ReadAllText(_logFile) : string.Empty;
            File.WriteAllText(_logFile, entry + existing, System.Text.Encoding.UTF8);
        }
        catch { /* best-effort */ }
    }
}
