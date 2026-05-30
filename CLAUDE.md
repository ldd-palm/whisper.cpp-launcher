# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

---

## Project Overview

**Whisper.cpp Launcher** — two complementary frontends for batch audio/video transcription using whisper.cpp, sharing a single `config.ini`:

| Component | Language | Output |
|-----------|----------|--------|
| `WhisperGUI.exe` | C# / WinForms / .NET 8 | GUI launcher |
| `whispercli.exe` | C++14 / Win32 console | CLI launcher |

---

## Build Commands

### GUI (WhisperGUI.exe)
```bat
dotnet build -c Release          :: compile only
dotnet publish -c Release        :: full publish to bin\Release\net8.0-windows\publish\
```
Build target: `x64` only (`<Platforms>x64</Platforms>` in csproj).

### CLI (whispercli.exe)
```bat
build.bat
```
Requires TDM-GCC / MinGW-w64 at `C:\Program Files (x86)\Dev-Cpp\MinGW64\bin\`.  
Two-step: `windres.exe whispercli.rc → app.res.o`, then `g++ -std=c++14 -mconsole -O2`.  
Icon: `whispercli.ico` (via `whispercli.rc`).  
If `whispercli.ico` is missing, build falls through to `:compile_no_icon` and continues without embedding.

---

## Architecture

### Shared config.ini

Both tools locate `config.ini` in the **same directory as their own executable** — never relative to cwd.

- **GUI writes** via `IniManager.Write` → `WritePrivateProfileStringW` (Unicode API).
- **CLI reads** via `IniGet()` → `GetPrivateProfileStringA` (ANSI API). `IniGet` always trims leading/trailing whitespace from returned values.
- The GUI creates `config.ini` on first run from the `DefaultIni` literal in `MainForm.cs` if the file is absent.

Key sections and consumers:

| Section | Keys consumed by CLI | Keys consumed by GUI |
|---------|---------------------|---------------------|
| `[LastSession]` | WhisperPath, FFmpegPath, ModelPath, SourcePath, Language, Platform | all |
| `[AdvancedArgs]` | ArgsCommon, ArgsSRT, ArgsTXT, ArgsVTT, ArgsFfmpeg | ArgsCommon, ArgsSRT/TXT/VTT, ArgsFfmpeg |
| `[<Platform>]` | DisplayName, ExtraArgs | DisplayName (for dropdown), ExtraArgs |
| `[Filter]` | SkipExtensions, TempMark | SkipExtensions, TempMark |

### GUI — C# component roles

- **`MainForm.cs`** — view + controller. Handles all UI events, reads/writes INI on every field-leave, and calls `_engine.RunAsync()`. No async UI state is held in the engine.
- **`TranscribeEngine.cs`** — pure async worker. Receives a `TranscribeOptions` record and a list of file paths; runs the FFmpeg → whisper-cli pipeline per file. Reports progress via `IProgress<LogEntry>`. Cancellation kills the active child process via `proc.Kill(entireProcessTree: true)`.
- **`IniManager.cs`** — thin wrapper around `GetPrivateProfileString(W)` / `WritePrivateProfileString(W)`. `Read()` always calls `.Trim()` on the result. `GetSections()` parses the file line-by-line (the Win32 multi-string API has buffer limitations).
- **`TranscribeOptions`** — immutable record that carries every parameter needed for one transcription job from `ValidateInputs()` to the engine. Adding a new config key means: add to `TranscribeOptions`, read it in `ValidateInputs()`, use it in `RunAsync()`.

### GUI — FFmpeg + whisper-cli invocation

```
ffmpeg -y -i "<input>" [ArgsFfmpeg] "<tempWav>"
whisper-cli -m "<model>" -f "<tempWav>" <FormatArg> -l <lang> [ArgsCommon] [ExtraArgs] -of "<outPrefix>"
```

Temp WAV goes to the **same directory as the input file** (`baseName + "_whisper_temp.wav"`). Output file lands next to the input file.

### CLI — whispercli.cpp structure

Single-file, no external headers beyond Win32 and stdlib.

Call flow:
1. `main()` — arg parsing (two-pass: pass 1 handles `-h`/`-e`/`-t`; pass 2 handles `-o`)
2. Pre-flight checks A–D (whisper-cli, FFmpeg via `SearchPathA`, sleep prevention API, SourcePath existence)
3. Branch to CLI mode or interactive mode
4. **CLI mode**: expand globs via `ExpandGlob()` → `ProcessBatch()`
5. **Interactive mode**: build file list from `cfg.sourcePath`, show Steps 1–3 menus → `ProcessBatch()`
6. `ProcessBatch()` — shared; runs FFmpeg + whisper-cli per file using `RunProcess()` (pipes with three threads: stdout drain, stderr capture, heartbeat dot every 5 s)

Key helpers:
- `IniGet()` — wraps `GetPrivateProfileStringA`, trims result.
- `SplitArgs(s)` — splits a whitespace-separated string into a `vector<string>`.
- `ExpandGlob(pattern)` — `FindFirstFileA`/`FindNextFileA`; no-dir patterns resolve against cwd.
- `cprint(text, color, newline=true)` — ANSI-coloured console output.

### SourcePath handling (CLI interactive mode)

`cfg.sourcePath` is used **directly** as `scanDir` with one exception: if it points to an existing file (not directory), the parent directory is used instead. There is **no silent fallback to cwd** — if the path doesn't exist, `scanDir` is still set to that value and the file list will be empty with a clear message. Pre-flight step D warns if `cfg.sourcePath` is set but `GetFileAttributesA` returns `INVALID_FILE_ATTRIBUTES`.

### Platform detection

`[LastSession] Platform` holds the active section name (e.g. `Vulkan`). Both tools read `[<Platform>] ExtraArgs` and append them to the whisper-cli command. The GUI populates the platform dropdown by scanning all INI sections that have a `DisplayName` key.

---

## Coding Conventions

### 1. Think Before Coding
State assumptions explicitly. If multiple interpretations exist, present them. Push back on overcomplicated approaches.

### 2. Simplicity First
Minimum code that solves the problem. No speculative features, no single-use abstractions. If it could be 50 lines, don't write 200.

### 3. Surgical Changes
Touch only what the task requires. Match existing style. Do not improve adjacent code. Remove only orphans your changes created — mention pre-existing dead code but don't delete it.

### 4. Goal-Driven Execution
For multi-step tasks, state a plan with verifiable steps before starting. Build and confirm success criterion before reporting done.

---

## Key Invariants

- **C++ build is C++14** — no `std::filesystem`, no structured bindings, no `if constexpr`.
- **Config path** is always `<exe_dir>\config.ini`, computed from `GetModuleFileNameA` (CLI) or `AppDomain.CurrentDomain.BaseDirectory` (GUI). Never use cwd.
- **ArgsFfmpeg** tokens are inserted **between** `-i <input>` and the output WAV path — order matters.
- **Output files** always land next to the input file, not in a configurable output directory.
- When changing the `TranscribeOptions` record, update all three files: `TranscribeEngine.cs` (field), `MainForm.cs` `ValidateInputs` (read + assign), and `TranscribeEngine.cs` `RunAsync` (use).
- The `DefaultIni` string literal in `MainForm.cs` must stay in sync with `config.ini` in the repo root (the repo root copy is the template/documentation copy).
