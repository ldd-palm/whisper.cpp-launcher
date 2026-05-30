# Whisper.cpp Launcher — 项目总结

## 项目定位

这是一个 **C# WinForms GUI 前端**，作为 `whisper-cli.exe` + `ffmpeg.exe` 的图形化封装器。程序本身不做任何 AI 推理，只负责拼接命令行参数并启动子进程。

------

## 技术栈

- **语言**：C# 12 / .NET 8
- **UI 框架**：WinForms（Windows only）
- **目标平台**：x64 Windows 10/11
- **构建**：Visual Studio 2026 Community，单文件发布（Self-contained，`win-x64`）
- **配置文件**：Windows INI 格式（`config.ini`），通过 Win32 API `GetPrivateProfileStringA` 读写

------

## 文件结构

```
WhisperLauncher.csproj    项目文件
Program.cs                入口
MainForm.cs               所有业务逻辑和事件处理
MainForm.Designer.cs      UI 控件布局（TableLayoutPanel 驱动，DPI-aware）
IniManager.cs             INI 读写封装（P/Invoke）
TranscribeEngine.cs       子进程执行引擎
config.ini                运行时配置（自动生成）
ffmpeg_logo.png           } 三个 logo 打包为 EmbeddedResource
whisper_logo.png          }（随 exe 嵌入，不需要外部文件）
gglm_logo.png             }
app.ico                   应用图标（via ApplicationIcon in .csproj）
```

------

## UI 布局（从上到下）

```
┌─────────────────────────────────────────┐
│ Environment (GroupBox)                  │
│   Whisper-CLI Path  [textbox] [Browse]  │
│   ✓ /path/to/whisper-cli.exe            │
│   GGML Model        [textbox] [Browse]  │
│   FFmpeg Path       [textbox] [Browse]  │
│   ✓ Found in system PATH                │
│   Platform          [CPU ▼]             │
├─────────────────────────────────────────┤
│ Media File (Panel，无边框)               │
│   Source File(s):   [textbox] [Browse]  │
│   ● Single File  ○ Batch (all files)   │
│   [批处理时显示文件预览列表]              │
├─────────────────────────────────────────┤
│ Language: [en ▼]  Output: ●.srt ○.txt ○.vtt │
├─────────────────────────────────────────┤
│ Log Output                  □ Detail mode│
│ [红色命令行预览]                          │
│ [黑底彩色日志输出 RichTextBox]            │
├─────────────────────────────────────────┤
│ [▶ Run] [■ Stop] [Edit Config]          │
│                  [FFmpeg] [whisper] [ggml] →│
└─────────────────────────────────────────┘
```

布局技术：每个区域内部使用 `TableLayoutPanel`（3列：label 132px | input percent-fill | button 90px），解决了 `Anchor=Right` 在 `Dock=Top` GroupBox 内因初始化时机导致按钮消失的问题。

------

## 核心功能点

### 1. 环境检测（启动时自动）

- 默认检查 launcher 同目录下有无 `whisper-cli.exe`，有则自动填入
- FFmpeg 先搜系统 PATH，找到自动填充；找不到才让用户手动选
- Whisper-CLI Path 验证：只检查文件是否存在（`--version` 不可用）

### 2. 媒体文件选择

- **单文件模式**：Browse 打开文件对话框，支持所有常见音视频格式
- **批处理模式**：Browse 打开文件夹对话框，扫描目录内所有媒体文件（按扩展名过滤），在列表中预览，排除临时文件和已生成的字幕文件
- 支持**拖拽**文件或文件夹到输入框，自动识别并切换模式

### 3. 转录执行（TranscribeEngine.cs）

每个文件串行执行两步流水线：

```
FFmpeg → 提取音频为 16kHz/Mono/PCM WAV（临时文件）
         ↓
whisper-cli → 推理生成 .srt/.txt/.vtt
         ↓
删除临时 WAV
```

- 异步执行（`async/await`），UI 不冻结

- `CancellationToken` 支持随时 Stop（`Process.Kill(entireProcessTree: true)`）

- **防系统睡眠**：运行时调用 `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)`（允许息屏），停止后恢复

- 日志模式

  ：

  - 默认：每 5 秒打一个 `.` 心跳点 + 完成摘要（耗时、输出路径）
  - Detail 模式：实时流式输出 whisper-cli 的 stderr

### 4. 命令行参数拼接（item 6）

点击 Run 后，日志区顶部用**红色**显示实际使用的命令模板：

```
[FFmpeg]  ffmpeg.exe -y -i <InputFile> -ar 16000 -ac 1 -c:a pcm_s16le <TempWAV> -loglevel error
[Whisper] whisper-cli.exe -m "model.bin" -f <TempWAV> -osrt -l en -et 2.4 -lpt -1.0 --no-fallback -of <OutputFile>
```

### 5. INI 配置（config.ini）

三类 Section：

- `[LastSession]`：UI 状态持久化（路径、语言、格式、平台等），下次启动自动恢复

- ```
  [AdvancedArgs]
  ```

  ：命令行参数模板，高级用户可手动修改：

  ```ini
  ArgsCommon = -et 2.4 -lpt -1.0 --no-fallbackArgsSRT    = -osrt
  ```

- `[Vulkan]` / `[CUDA]` / `[OpenVINO]` / `[CPU]`：各平台的 `ExtraArgs`，按选择的平台自动追加

- `[Links]`：三个 logo 对应的下载 URL

**Config 按钮**：直接用 Notepad 打开 config.ini 供用户编辑。

### config.ini 示例

```
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

[AdvancedArgs]
ArgsCommon = -et 2.4 -lpt -1.0 --no-fallback
ArgsTXT    = -otxt
ArgsSRT    = -osrt
ArgsVTT    = -ovtt

[Vulkan]
DisplayName = Vulkan GPU
ExtraArgs   = --vulkan-device 0

[CUDA]
DisplayName = CUDA GPU
ExtraArgs   = 

[OpenVINO]
DisplayName = OpenVINO NPU
ExtraArgs   = --ov-e-device NPU

[CPU]
DisplayName = CPU AVX2
ExtraArgs   = 

[Filter]
SkipExtensions = .srt,.txt,.vtt,.log,.wav,.ini
TempMark       = _whisper_temp

[Links]
FFmpegUrl  = https://ffmpeg.org/download.html#build-windows
WhisperUrl = https://github.com/ggml-org/whisper.cpp
GgmlUrl    = https://huggingface.co/ggerganov/whisper.cpp
```



### 6. 错误处理

- 子进程失败时捕获 stderr，写入 `whisper_process.log`（最新记录置顶）
- 日志框用红色显示错误
- 关闭窗口时若有任务在跑，弹出确认对话框

------

## 数据流

```
用户选择文件/路径
     ↓
ValidateInputs() 校验所有输入
     ↓
BuildCommandPreview() → 显示红色命令模板
     ↓
TranscribeEngine.RunAsync(files, opts, progress, ct)
     ↓
  for each file:
    RunProcessAsync(ffmpeg, args)   → 异步，5s心跳
    RunProcessAsync(whisper, args)  → 异步，5s心跳 or 流式stderr
    DeleteTempWav()
     ↓
AllowSleep() + 显示总耗时
```

------





## 已知限制 / 可扩展点

1. **Platform 下拉只显示在config.ini中定义的平台**
2. **单文件发布**（支持.NET 8， win-x64 ），logos 通过 `EmbeddedResource` 打包进 exe
3. **config.ini 首次运行自动生成**，内容硬编码在 `MainForm.cs` 的 `DefaultIni` 常量中
4. **whisper-cli 不支持 `--version`**，所以路径验证只做文件存在性检查
5. **批处理串行**，无并发（GPU 资源单占，并发反而慢），
6. 要求布局美观元素互不遮挡
