/*
================================================================================
  WHISPERCLI BATCH TRANSCRIPTION FRAMEWORK
================================================================================
  Version : 2.0
  Compiler: TDM-GCC (MinGW) / C++14
  Platform: Windows 10/11
  Build   : build.bat
================================================================================
*/

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlwapi.h>    // PathFileExistsA, PathIsRelativeA, SearchPathA
#include <iostream>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>
#include <thread>
#include <atomic>
#include <algorithm>
#include <chrono>
#include <ctime>
#include <stdexcept>

// ============================================================================
// ANSI 颜色 (Win10/11 原生支持)
// ============================================================================
namespace Color {
    const std::string RESET   = "\033[0m";
    const std::string CYAN    = "\033[96m";
    const std::string GREEN   = "\033[92m";
    const std::string YELLOW  = "\033[93m";
    const std::string RED     = "\033[91m";
    const std::string MAGENTA = "\033[95m";
    const std::string DCYAN   = "\033[36m";
}

#ifndef ENABLE_VIRTUAL_TERMINAL_PROCESSING
#define ENABLE_VIRTUAL_TERMINAL_PROCESSING 0x0004
#endif

static void EnableAnsi() {
    HANDLE hOut = GetStdHandle(STD_OUTPUT_HANDLE);
    DWORD mode = 0;
    GetConsoleMode(hOut, &mode);
    SetConsoleMode(hOut, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
    SetConsoleOutputCP(CP_UTF8);
}

static void cprint(const std::string& text,
                   const std::string& color = Color::RESET,
                   bool newline = true) {
    std::cout << color << text << Color::RESET;
    if (newline) std::cout << "\n";
    std::cout.flush();
}

// ============================================================================
// 配置结构体
// ============================================================================
struct Config {
    std::string whisperExe;      // [LastSession] WhisperPath
    std::string ffmpegExe;       // [LastSession] FFmpegPath
    std::string modelPath;       // [LastSession] ModelPath  (完整路径到 .bin)
    std::string sourcePath;      // [LastSession] SourcePath (文件或文件夹)
    std::string language;        // [LastSession] Language
    std::string activePlatform;  // [LastSession] Platform
    std::string platformDisplay;
    std::vector<std::string> extraArgs;
    std::string argsCommon;      // [AdvancedArgs] ArgsCommon
    std::string argsSRT;         // [AdvancedArgs] ArgsSRT
    std::string argsTXT;         // [AdvancedArgs] ArgsTXT
    std::string argsVTT;         // [AdvancedArgs] ArgsVTT
    std::string argsFfmpeg;      // [AdvancedArgs] ArgsFfmpeg
    std::vector<std::string> skipExts;
    std::string tempMark;
};

// ============================================================================
// INI 读取
// ============================================================================
static std::string IniGet(const std::string& section,
                           const std::string& key,
                           const std::string& def,
                           const std::string& iniPath) {
    char buf[1024] = {};
    GetPrivateProfileStringA(section.c_str(), key.c_str(),
                             def.c_str(), buf, sizeof(buf),
                             iniPath.c_str());
    std::string result(buf);
    auto first = result.find_first_not_of(" \t\r\n");
    if (first == std::string::npos) return "";
    auto last = result.find_last_not_of(" \t\r\n");
    return result.substr(first, last - first + 1);
}

static std::vector<std::string> SplitComma(const std::string& s) {
    std::vector<std::string> result;
    std::stringstream ss(s);
    std::string token;
    while (std::getline(ss, token, ',')) {
        token.erase(0, token.find_first_not_of(" \t"));
        if (!token.empty())
            token.erase(token.find_last_not_of(" \t") + 1);
        if (!token.empty()) result.push_back(token);
    }
    return result;
}

// 空格分隔的参数字符串 → token 列表
static std::vector<std::string> SplitArgs(const std::string& s) {
    std::vector<std::string> result;
    std::istringstream iss(s);
    std::string token;
    while (iss >> token) result.push_back(token);
    return result;
}

static Config LoadConfig(const std::string& iniPath) {
    Config cfg;
    cfg.whisperExe     = IniGet("LastSession", "WhisperPath",   "whisper-cli.exe",                 iniPath);
    cfg.ffmpegExe      = IniGet("LastSession", "FFmpegPath",    "ffmpeg",                          iniPath);
    cfg.modelPath      = IniGet("LastSession", "ModelPath",     "",                                iniPath);
    cfg.sourcePath     = IniGet("LastSession", "SourcePath",    "",                                iniPath);
    cfg.language       = IniGet("LastSession", "Language",      "en",                              iniPath);
    cfg.activePlatform = IniGet("LastSession", "Platform",      "CPU",                             iniPath);

    cfg.platformDisplay = IniGet(cfg.activePlatform, "DisplayName", cfg.activePlatform, iniPath);
    std::string rawExtra = IniGet(cfg.activePlatform, "ExtraArgs", "", iniPath);
    {
        std::istringstream iss(rawExtra);
        std::string token;
        while (iss >> token) cfg.extraArgs.push_back(token);
    }

    cfg.argsCommon  = IniGet("AdvancedArgs", "ArgsCommon",  "-et 2.8 -lpt -1.0 --no-fallback",          iniPath);
    cfg.argsSRT     = IniGet("AdvancedArgs", "ArgsSRT",     "-osrt",                                     iniPath);
    cfg.argsTXT     = IniGet("AdvancedArgs", "ArgsTXT",     "-otxt",                                     iniPath);
    cfg.argsVTT     = IniGet("AdvancedArgs", "ArgsVTT",     "-ovtt",                                     iniPath);
    cfg.argsFfmpeg  = IniGet("AdvancedArgs", "ArgsFfmpeg",  "-ar 16000 -ac 1 -c:a pcm_s16le -loglevel error", iniPath);

    std::string exts = IniGet("Filter", "SkipExtensions", ".srt,.txt,.vtt,.log,.wav,.ini", iniPath);
    cfg.skipExts = SplitComma(exts);
    cfg.tempMark = IniGet("Filter", "TempMark", "_whisper_temp", iniPath);
    return cfg;
}

// ============================================================================
// 日志 (最新记录置顶)
// ============================================================================
static std::string g_logFile;

static std::string NowStr() {
    auto t  = std::time(nullptr);
    char buf[64];
    std::strftime(buf, sizeof(buf), "%Y/%m/%d %A %H:%M:%S", std::localtime(&t));
    return buf;
}

static void WriteLog(const std::string& message, const std::string& rawError = "") {
    std::string sep(51, '-');
    std::string entry = sep + "\r\n[" + NowStr() + "] " + message + "\r\n";
    if (!rawError.empty())
        entry += "[Captured Stderr Stream]:\r\n" + rawError + "\r\n";

    std::string old;
    std::ifstream fin(g_logFile);
    if (fin) {
        std::ostringstream oss;
        oss << fin.rdbuf();
        old = oss.str();
    }

    std::ofstream fout(g_logFile, std::ios::trunc);
    fout << entry << old;
}

// ============================================================================
// 防睡眠
// ============================================================================
static bool g_sleepApiOk = false;
typedef EXECUTION_STATE(WINAPI* PFN_STES)(EXECUTION_STATE);
static PFN_STES g_pfnStes = nullptr;

static void InitSleepPrevention() {
    HMODULE hKernel = GetModuleHandleA("kernel32.dll");
    if (hKernel)
        g_pfnStes = (PFN_STES)GetProcAddress(hKernel, "SetThreadExecutionState");
    if (g_pfnStes) {
        g_sleepApiOk = true;
        cprint(" [+] Win32 Power Management API : PASSED", Color::GREEN);
    } else {
        cprint("\n[WARNING] Sleep prevention init failed.", Color::YELLOW);
        cprint(" -> Result: Sleep/Standby Prevention is DISABLED.", Color::YELLOW);
        WriteLog("[Warning] SetThreadExecutionState not available.");
    }
}

static void PreventSleep() {
    if (g_pfnStes)
        g_pfnStes(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
}

static void AllowSleep() {
    if (g_pfnStes)
        g_pfnStes(ES_CONTINUOUS);
}

// ============================================================================
// 退出辅助
// ============================================================================
static void PauseAndExit(int code = 0) {
    std::cout << "\nPress any key to continue . . . ";
    std::cout.flush();
    HANDLE hIn = GetStdHandle(STD_INPUT_HANDLE);
    DWORD oldMode;
    GetConsoleMode(hIn, &oldMode);
    SetConsoleMode(hIn, 0);
    INPUT_RECORD ir;
    DWORD read;
    while (true) {
        ReadConsoleInputA(hIn, &ir, 1, &read);
        if (ir.EventType == KEY_EVENT && ir.Event.KeyEvent.bKeyDown)
            break;
    }
    SetConsoleMode(hIn, oldMode);
    std::cout << "\n";
    ExitProcess(code);
}

static void QuietExit(int code = 0) {
    ExitProcess(code);
}

// 用 Notepad 打开文件 (不依赖 shell32)
static void OpenInNotepad(const std::string& filePath) {
    std::string cmd = "notepad.exe \"" + filePath + "\"";
    std::vector<char> buf(cmd.begin(), cmd.end());
    buf.push_back('\0');
    STARTUPINFOA si{};
    si.cb = sizeof(si);
    PROCESS_INFORMATION pi{};
    if (CreateProcessA(nullptr, buf.data(), nullptr, nullptr,
                       FALSE, 0, nullptr, nullptr, &si, &pi)) {
        CloseHandle(pi.hProcess);
        CloseHandle(pi.hThread);
    }
}

// 环境检测失败后询问用户：(E) 编辑配置 或 (X) 退出
static void PromptEditOrExit(const std::string& iniPath) {
    cprint("\n (E) Edit config.ini   (X) Exit", Color::YELLOW);
    std::cout << "Choice: ";
    std::cout.flush();

    HANDLE hIn = GetStdHandle(STD_INPUT_HANDLE);
    DWORD oldMode;
    GetConsoleMode(hIn, &oldMode);
    SetConsoleMode(hIn, 0);

    char ch = 'x';
    INPUT_RECORD ir;
    DWORD read;
    while (true) {
        ReadConsoleInputA(hIn, &ir, 1, &read);
        if (ir.EventType == KEY_EVENT && ir.Event.KeyEvent.bKeyDown &&
            ir.Event.KeyEvent.uChar.AsciiChar != 0) {
            ch = ir.Event.KeyEvent.uChar.AsciiChar;
            break;
        }
    }
    SetConsoleMode(hIn, oldMode);
    std::cout << ch << "\n";

    if (ch == 'e' || ch == 'E') {
        cprint("-> Opening config.ini in Notepad. Restart after saving.", Color::YELLOW);
        OpenInNotepad(iniPath);
    } else {
        cprint("-> Exiting. Goodbye!", Color::YELLOW);
    }
    ExitProcess(1);
}

// ============================================================================
// 帮助文本
// ============================================================================
static void PrintEnvHelp() {
    std::cout << "\n";
    cprint("===================================================", Color::CYAN);
    cprint(" Environment Check Failed", Color::RED);
    cprint("===================================================", Color::CYAN);
    std::cout << "\n";
    cprint(" whispercli requires the following in config.ini:", Color::YELLOW);
    std::cout << "\n";
    std::cout << " [1] whisper-cli.exe\n";
    std::cout << "     Set: config.ini -> [LastSession] -> WhisperPath\n";
    std::cout << "     e.g. WhisperPath = C:\\Apps\\Whisper\\whisper-cli.exe\n";
    std::cout << "          (relative = same folder as whispercli.exe)\n";
    std::cout << "\n";
    std::cout << " [2] GGML Model (.bin)\n";
    std::cout << "     Set: config.ini -> [LastSession] -> ModelPath\n";
    std::cout << "     e.g. ModelPath = C:\\Apps\\Whisper\\models\\ggml-medium.en.bin\n";
    std::cout << "\n";
    std::cout << " [3] FFmpeg\n";
    std::cout << "     Set: config.ini -> [LastSession] -> FFmpegPath\n";
    std::cout << "     e.g. FFmpegPath = ffmpeg            (already in system PATH)\n";
    std::cout << "          FFmpegPath = C:\\ffmpeg\\bin\\ffmpeg.exe\n";
    std::cout << "\n";
    std::cout << "---------------------------------------------------\n";
    cprint(" Tip: Run the Whisper Launcher GUI to configure paths.", Color::YELLOW);
    cprint("===================================================", Color::CYAN);
    std::cout << "\n";
}

static void PrintHelp() {
    cprint("===================================================", Color::CYAN);
    cprint(" whispercli  --  Whisper Batch Transcription CLI   ", Color::CYAN);
    cprint("===================================================", Color::CYAN);
    std::cout << "\n";
    cprint("Usage:  whispercli [options]", Color::YELLOW);
    std::cout << "\n";
    std::cout << "  (no args)            Interactive menu, scans SourcePath from config.ini\n";
    std::cout << "  -o <file(s)>         Transcribe to .srt (default format)\n";
    std::cout << "  -t srt|vtt|txt       Output format — specify before -o\n";
    std::cout << "  -e                   Open config.ini in Notepad\n";
    std::cout << "  -h                   Show this help\n";
    std::cout << "\n";
    cprint("  -o accepts multiple files or glob patterns:", Color::CYAN);
    std::cout << "    *.mkv              all MKV files in current directory\n";
    std::cout << "    video.mp4          specific file\n";
    std::cout << "    C:\\Videos\\*.mp4    full path with wildcard\n";
    std::cout << "    *.mkv *.mp4        multiple patterns\n";
    std::cout << "\n";
    cprint("Examples:", Color::CYAN);
    cprint("  whispercli", Color::GREEN, false);
    std::cout << "                         interactive menu\n";
    cprint("  whispercli -o video.mkv", Color::GREEN, false);
    std::cout << "            single file -> video.srt\n";
    cprint("  whispercli -o *.mkv *.mp4", Color::GREEN, false);
    std::cout << "          all files -> .srt\n";
    cprint("  whispercli -t txt -o lecture.mp4", Color::GREEN, false);
    std::cout << "   specify format\n";
    cprint("  whispercli -e", Color::GREEN, false);
    std::cout << "                      edit config.ini\n";
    std::cout << "\n";
    cprint("Config file:  config.ini  (same directory as whispercli.exe)", Color::YELLOW);
    std::cout << "  [LastSession]   WhisperPath, FFmpegPath, ModelPath, SourcePath\n";
    std::cout << "                  Language, OutputFormat, Platform\n";
    std::cout << "  [AdvancedArgs]  ArgsCommon, ArgsSRT, ArgsTXT, ArgsVTT\n";
    std::cout << "  [Platform]      DisplayName, ExtraArgs\n";
    std::cout << "  [Filter]        SkipExtensions, TempMark\n";
    std::cout << "\n";
}

// ============================================================================
// 文件工具
// ============================================================================
static std::string GetExtLower(const std::string& filename) {
    auto pos = filename.rfind('.');
    if (pos == std::string::npos) return "";
    std::string ext = filename.substr(pos);
    std::transform(ext.begin(), ext.end(), ext.begin(), ::tolower);
    return ext;
}

static std::string FormatElapsed(double seconds) {
    int h = (int)(seconds / 3600);
    int m = (int)((seconds - h * 3600) / 60);
    int s = (int)(seconds) % 60;
    std::string out;
    if (h) out += std::to_string(h) + " hours ";
    if (m) out += std::to_string(m) + " min ";
    out += std::to_string(s) + " sec";
    return out;
}

static std::string GetCwd() {
    char buf[MAX_PATH] = {};
    GetCurrentDirectoryA(MAX_PATH, buf);
    return std::string(buf);
}

// 展开 glob 模式为绝对路径列表。无目录部分时解析为当前目录。
static std::vector<std::string> ExpandGlob(const std::string& pattern) {
    std::vector<std::string> result;

    std::string baseDir;
    std::string searchPath;
    auto lastSep = pattern.find_last_of("\\/");
    if (lastSep != std::string::npos) {
        baseDir    = pattern.substr(0, lastSep);
        searchPath = pattern;
    } else {
        baseDir    = GetCwd();
        searchPath = baseDir + "\\" + pattern;
    }

    WIN32_FIND_DATAA fd;
    HANDLE hFind = FindFirstFileA(searchPath.c_str(), &fd);
    if (hFind == INVALID_HANDLE_VALUE) return result;
    do {
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) continue;
        result.push_back(baseDir + "\\" + std::string(fd.cFileName));
    } while (FindNextFileA(hFind, &fd));
    FindClose(hFind);
    return result;
}

// ============================================================================
// 子进程执行器
//   stdout/stderr 各用独立线程消费防死锁；心跳线程每 5 秒打一个绿色 "."
// ============================================================================
static int RunProcess(const std::string& exe,
                      const std::vector<std::string>& args,
                      const std::string& stderrFile) {
    std::string cmdLine = "\"" + exe + "\"";
    for (const auto& a : args) {
        cmdLine += " ";
        if (a.find(' ') != std::string::npos)
            cmdLine += "\"" + a + "\"";
        else
            cmdLine += a;
    }

    HANDLE hStdOutR, hStdOutW, hStdErrR, hStdErrW;
    SECURITY_ATTRIBUTES sa{ sizeof(sa), nullptr, TRUE };
    CreatePipe(&hStdOutR, &hStdOutW, &sa, 0);
    CreatePipe(&hStdErrR, &hStdErrW, &sa, 0);
    SetHandleInformation(hStdOutR, HANDLE_FLAG_INHERIT, 0);
    SetHandleInformation(hStdErrR, HANDLE_FLAG_INHERIT, 0);

    STARTUPINFOA si{};
    si.cb         = sizeof(si);
    si.dwFlags    = STARTF_USESTDHANDLES;
    si.hStdOutput = hStdOutW;
    si.hStdError  = hStdErrW;
    si.hStdInput  = GetStdHandle(STD_INPUT_HANDLE);

    PROCESS_INFORMATION pi{};
    std::vector<char> cmd(cmdLine.begin(), cmdLine.end());
    cmd.push_back('\0');

    if (!CreateProcessA(nullptr, cmd.data(), nullptr, nullptr,
                        TRUE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) {
        CloseHandle(hStdOutR); CloseHandle(hStdOutW);
        CloseHandle(hStdErrR); CloseHandle(hStdErrW);
        return -1;
    }
    CloseHandle(hStdOutW);
    CloseHandle(hStdErrW);

    std::string stderrData;

    std::thread tOut([&]() {
        char buf[4096]; DWORD rd;
        while (ReadFile(hStdOutR, buf, sizeof(buf), &rd, nullptr) && rd > 0) {}
        CloseHandle(hStdOutR);
    });

    std::thread tErr([&]() {
        char buf[4096]; DWORD rd;
        while (ReadFile(hStdErrR, buf, sizeof(buf), &rd, nullptr) && rd > 0)
            stderrData.append(buf, rd);
        CloseHandle(hStdErrR);
    });

    std::atomic<bool> done(false);
    std::thread tHeart([&]() {
        while (!done.load()) {
            std::this_thread::sleep_for(std::chrono::seconds(5));
            if (!done.load())
                std::cout << Color::GREEN << "." << Color::RESET << std::flush;
        }
    });

    WaitForSingleObject(pi.hProcess, INFINITE);
    done.store(true);

    DWORD exitCode = 1;
    GetExitCodeProcess(pi.hProcess, &exitCode);
    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);

    tOut.join();
    tErr.join();
    tHeart.join();

    if (!stderrFile.empty() && !stderrData.empty()) {
        std::ofstream f(stderrFile, std::ios::trunc);
        f << stderrData;
    }

    return (int)exitCode;
}

// ============================================================================
// 键盘输入 (读取一行，转小写)
// ============================================================================
static std::string ReadLineNoCase() {
    std::string line;
    std::getline(std::cin, line);
    line.erase(0, line.find_first_not_of(" \t\r\n"));
    if (!line.empty())
        line.erase(line.find_last_not_of(" \t\r\n") + 1);
    std::transform(line.begin(), line.end(), line.begin(), ::tolower);
    return line;
}

// ============================================================================
// Whisper 参数构建
// ============================================================================
static std::vector<std::string> BuildWhisperArgs(
    const Config& cfg,
    const std::string& modelFilePath,
    const std::string& tempWav,
    const std::string& formatArg,
    const std::string& outPrefix)
{
    std::vector<std::string> args;
    args.push_back("-m"); args.push_back(modelFilePath);
    args.push_back("-f"); args.push_back(tempWav);
    args.push_back(formatArg);
    args.push_back("-l"); args.push_back(cfg.language);
    for (const auto& a : SplitArgs(cfg.argsCommon)) args.push_back(a);
    for (const auto& a : cfg.extraArgs)              args.push_back(a);
    args.push_back("-of"); args.push_back(outPrefix);
    return args;
}

// ============================================================================
// 批处理循环 (CLI 与 Interactive 共用)
// targetFiles 为绝对路径列表；输出文件与输入文件同目录
// ============================================================================
static void ProcessBatch(
    const Config& cfg,
    const std::vector<std::string>& targetFiles,
    const std::string& selectedModelPath,
    const std::string& formatArg,
    const std::string& targetExt)
{
    cprint("\n===================================================", Color::CYAN);
    cprint(" Processing queued tasks (Quiet Mode)...           ", Color::CYAN);
    cprint("===================================================", Color::CYAN);

    PreventSleep();
    if (g_sleepApiOk)
        cprint("[-] Insomnia Mode activated: Windows sleep prevention engaged.", Color::MAGENTA);
    else
        cprint("[!] Insomnia Mode unverified: Ensure your PC does not sleep manually.", Color::YELLOW);

    char tmpDir[MAX_PATH];
    GetTempPathA(MAX_PATH, tmpDir);
    std::string ffmpegErr = std::string(tmpDir) + "ffmpeg_err.txt";
    std::string wpErr     = std::string(tmpDir) + "whisper_runtime_err.txt";

    for (const auto& fullPath : targetFiles) {
        // 从完整路径分离目录、基础名、显示名
        std::string fileDir, baseName, displayName;
        {
            auto sep  = fullPath.find_last_of("\\/");
            std::string filename = (sep != std::string::npos)
                                   ? fullPath.substr(sep + 1)
                                   : fullPath;
            fileDir     = (sep != std::string::npos) ? fullPath.substr(0, sep) : GetCwd();
            displayName = filename;
            auto dot    = filename.rfind('.');
            baseName    = (dot != std::string::npos) ? filename.substr(0, dot) : filename;
        }

        std::cout << "\n";
        cprint("Processing Target: " + displayName, Color::YELLOW);

        std::string tempWav   = fileDir + "\\" + baseName + cfg.tempMark + ".wav";
        std::string finalOut  = fileDir + "\\" + baseName + targetExt;
        std::string outPrefix = fileDir + "\\" + baseName;

        auto timeStart = std::chrono::steady_clock::now();

        // [1/2] FFmpeg 提取音频
        std::cout << " -> [1/2] Extracting Audio... ";
        std::cout.flush();
        std::vector<std::string> fmArgs = {"-y", "-i", fullPath};
        for (const auto& a : SplitArgs(cfg.argsFfmpeg)) fmArgs.push_back(a);
        fmArgs.push_back(tempWav);
        int fmRet = RunProcess(cfg.ffmpegExe, fmArgs, ffmpegErr);
        if (fmRet != 0) {
            std::string rawErr;
            std::ifstream ef(ffmpegErr);
            if (ef) { std::ostringstream os; os << ef.rdbuf(); rawErr = os.str(); }
            WriteLog("[FFmpeg Error] Failed on " + displayName + ".", rawErr);
            cprint("[FAILED] Check logs.", Color::RED);
            DeleteFileA(tempWav.c_str());
            continue;
        }
        cprint("Done.", Color::GREEN);

        // [2/2] Whisper 推理
        if (!PathFileExistsA(tempWav.c_str())) {
            cprint(" [FAILED] Temp WAV missing after FFmpeg.", Color::RED);
            continue;
        }
        std::cout << " -> [2/2] Transcribing (" << cfg.platformDisplay << ") ";
        std::cout.flush();

        std::vector<std::string> wpArgs =
            BuildWhisperArgs(cfg, selectedModelPath, tempWav, formatArg, outPrefix);
        int wpRet = RunProcess(cfg.whisperExe, wpArgs, wpErr);

        auto timeEnd = std::chrono::steady_clock::now();
        double elapsed = std::chrono::duration<double>(timeEnd - timeStart).count();

        if (wpRet != 0) {
            std::string rawErr;
            std::ifstream ef(wpErr);
            if (ef) { std::ostringstream os; os << ef.rdbuf(); rawErr = os.str(); }
            WriteLog("[Whisper Error] Transcription crashed for " + displayName + ".", rawErr);
            cprint(" [FAILED] Transcription crashed. Details appended to log.", Color::RED);
        } else {
            if (PathFileExistsA(finalOut.c_str())) {
                cprint(" Done.", Color::GREEN);
                std::cout << "    [+] Output File : ";
                cprint(baseName + targetExt, Color::CYAN);
                std::cout << "    [+] Time Elapsed: ";
                cprint(FormatElapsed(elapsed), Color::CYAN);
            } else {
                WriteLog("[System Error] Whisper exited 0 but output missing: " + finalOut);
                cprint(" [FAILED] Error: Output file not found.", Color::RED);
            }
        }

        DeleteFileA(tempWav.c_str());
        DeleteFileA(ffmpegErr.c_str());
        DeleteFileA(wpErr.c_str());
    }

    AllowSleep();
    if (g_sleepApiOk)
        cprint("\n[-] Insomnia Mode released: Default Windows power settings restored.", Color::MAGENTA);
}

// ============================================================================
// main
// ============================================================================
int main(int argc, char* argv[]) {
    EnableAnsi();

    // 取 exe 所在目录
    char exePath[MAX_PATH];
    GetModuleFileNameA(nullptr, exePath, MAX_PATH);
    std::string scriptDir(exePath);
    auto slashPos = scriptDir.find_last_of("\\/");
    if (slashPos != std::string::npos) scriptDir = scriptDir.substr(0, slashPos);

    std::string iniPath = scriptDir + "\\config.ini";
    g_logFile           = scriptDir + "\\whisper_process.log";

    // --------------------------------------------------------------------------
    // 参数解析
    // --------------------------------------------------------------------------
    bool cliMode = false;
    std::string argFormat;                   // "" 表示使用默认 "srt"
    std::vector<std::string> inputPatterns;

    // Pass 1: -h、-e、-t (允许出现在 -o 之前的任意位置)
    for (int i = 1; i < argc; ++i) {
        std::string a(argv[i]);
        if (a == "-h" || a == "--help") {
            PrintHelp();
            return 0;
        }
        if (a == "-e" || a == "--edit") {
            cprint("Opening config.ini in Notepad...", Color::YELLOW);
            OpenInNotepad(iniPath);
            return 0;
        }
        if (a == "-t" && i + 1 < argc) {
            argFormat = argv[++i];
        }
    }

    // Pass 2: -o 之后的所有 arg 都视为文件模式
    for (int i = 1; i < argc; ++i) {
        std::string a(argv[i]);
        if (a == "-t") { ++i; continue; }
        if (a == "-o") {
            cliMode = true;
            for (int j = i + 1; j < argc; ++j)
                inputPatterns.push_back(argv[j]);
            break;
        }
    }

    // --------------------------------------------------------------------------
    // Pre-flight 环境检查 (CLI 与 Interactive 共用)
    // --------------------------------------------------------------------------
    cprint("===================================================", Color::CYAN);
    cprint(" Running Pre-flight Environment Checks...          ", Color::CYAN);
    cprint("===================================================", Color::CYAN);

    if (!PathFileExistsA(iniPath.c_str())) {
        cprint("[FATAL] config.ini not found: " + iniPath, Color::RED);
        cprint(" -> Run the Whisper Launcher GUI first, or create config.ini manually.", Color::YELLOW);
        PrintEnvHelp();
        PromptEditOrExit(iniPath);
    }
    Config cfg = LoadConfig(iniPath);

    // 相对路径 → 绝对路径
    if (PathIsRelativeA(cfg.whisperExe.c_str()))
        cfg.whisperExe = scriptDir + "\\" + cfg.whisperExe;

    // A. whisper-cli 路径
    if (!PathFileExistsA(cfg.whisperExe.c_str())) {
        cprint("[FATAL] whisper-cli.exe not found at: " + cfg.whisperExe, Color::RED);
        PrintEnvHelp();
        PromptEditOrExit(iniPath);
    }
    cprint(" [+] whisper-cli.exe            : FOUND", Color::GREEN);
    cprint(" [+] Platform                   : " + cfg.platformDisplay, Color::GREEN);

    // B. FFmpeg 两步检测
    {
        char foundPath[MAX_PATH] = {};
        bool inSystemPath = (SearchPathA(nullptr, "ffmpeg", ".exe",
                                         MAX_PATH, foundPath, nullptr) > 0);
        if (inSystemPath) {
            cfg.ffmpegExe = std::string(foundPath);
            cprint(" [+] FFmpeg (system PATH)        : " + cfg.ffmpegExe, Color::GREEN);
        } else {
            if (PathIsRelativeA(cfg.ffmpegExe.c_str()))
                cfg.ffmpegExe = scriptDir + "\\" + cfg.ffmpegExe;
            if (!PathFileExistsA(cfg.ffmpegExe.c_str())) {
                cprint("[FATAL] FFmpeg not found in PATH or at: " + cfg.ffmpegExe, Color::RED);
                WriteLog("[Fatal] FFmpeg not found.", cfg.ffmpegExe);
                PrintEnvHelp();
                PromptEditOrExit(iniPath);
            }
            cprint(" [+] FFmpeg (ini path)           : " + cfg.ffmpegExe, Color::GREEN);
        }
    }

    // C. 防睡眠 API
    InitSleepPrevention();

    // D. SourcePath 存在性验证（仅交互模式使用；为空时使用当前目录，无需检查）
    if (!cliMode && !cfg.sourcePath.empty()) {
        if (GetFileAttributesA(cfg.sourcePath.c_str()) == INVALID_FILE_ATTRIBUTES) {
            cprint(" [!] SourcePath not found       : " + cfg.sourcePath, Color::YELLOW);
            cprint("     -> Run 'whispercli -e' to update config.ini", Color::YELLOW);
        } else {
            cprint(" [+] SourcePath                 : " + cfg.sourcePath, Color::GREEN);
        }
    }

    // ==========================================================================
    // CLI 模式 (-o 已提供)
    // ==========================================================================
    if (cliMode) {

        // 验证 ModelPath
        if (cfg.modelPath.empty() || !PathFileExistsA(cfg.modelPath.c_str())) {
            cprint("[FATAL] ModelPath not set or file not found.", Color::RED);
            cprint("        Set [LastSession] ModelPath in config.ini.", Color::YELLOW);
            PromptEditOrExit(iniPath);
        }
        cprint(" [+] Model                      : " + cfg.modelPath, Color::GREEN);

        // 解析输出格式
        std::string fmt = argFormat.empty() ? "srt" : argFormat;
        std::string formatArg, targetExt;
        if (fmt == "txt") {
            formatArg = cfg.argsTXT; targetExt = ".txt";
        } else if (fmt == "vtt") {
            formatArg = cfg.argsVTT; targetExt = ".vtt";
        } else {
            formatArg = cfg.argsSRT; targetExt = ".srt";
        }
        cprint(" [+] Output format              : " + fmt, Color::GREEN);

        // 展开 glob 模式，收集目标文件
        std::vector<std::string> targetFiles;
        for (const auto& p : inputPatterns) {
            std::vector<std::string> expanded = ExpandGlob(p);
            // 若 FindFirstFile 未匹配，检查是否已是存在的文件路径
            if (expanded.empty()) {
                std::string absPath = PathIsRelativeA(p.c_str())
                                      ? GetCwd() + "\\" + p
                                      : p;
                if (PathFileExistsA(absPath.c_str()))
                    expanded.push_back(absPath);
            }
            for (const auto& f : expanded) {
                auto sep  = f.find_last_of("\\/");
                std::string fname = (sep != std::string::npos) ? f.substr(sep + 1) : f;
                std::string ext   = GetExtLower(fname);
                bool skip = false;
                for (const auto& se : cfg.skipExts)
                    if (ext == se) { skip = true; break; }
                if (!skip && fname.find(cfg.tempMark) == std::string::npos)
                    targetFiles.push_back(f);
            }
        }

        if (targetFiles.empty()) {
            cprint("\n[INFO] No matching media files found.", Color::YELLOW);
            QuietExit(0);
        }
        cprint(" [+] Files to process           : " + std::to_string(targetFiles.size()), Color::GREEN);

        ProcessBatch(cfg, targetFiles, cfg.modelPath, formatArg, targetExt);

        std::cout << "\n";
        cprint("===================================================", Color::CYAN);
        cprint(" All transcription jobs handled perfectly!         ", Color::CYAN);
        QuietExit(0);
    }

    // ==========================================================================
    // 交互模式 (无参数)
    // ==========================================================================

    // D. 确定扫描目录：SourcePath 有值则直接使用，为空则使用当前目录
    //    若 SourcePath 指向文件，取其父目录
    std::string scanDir;
    {
        const std::string& sp = cfg.sourcePath;
        if (!sp.empty()) {
            DWORD attr = GetFileAttributesA(sp.c_str());
            if (attr != INVALID_FILE_ATTRIBUTES && !(attr & FILE_ATTRIBUTE_DIRECTORY)) {
                // SourcePath 是具体文件 → 取父目录
                auto pos = sp.find_last_of("\\/");
                scanDir = (pos != std::string::npos) ? sp.substr(0, pos) : sp;
            } else {
                // 目录或路径不存在 → 直接使用（不存在时文件列表为空，提示用户检查路径）
                scanDir = sp;
            }
            cprint(" [+] Source directory           : " + scanDir, Color::GREEN);
        } else {
            scanDir = GetCwd();
            cprint(" [!] SourcePath not set — scanning current directory", Color::YELLOW);
        }
    }

    std::vector<std::string> allFiles;
    {
        WIN32_FIND_DATAA fd;
        std::string pattern = scanDir + "\\*";
        HANDLE hFind = FindFirstFileA(pattern.c_str(), &fd);
        if (hFind != INVALID_HANDLE_VALUE) {
            do {
                if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) continue;
                std::string name(fd.cFileName);
                std::string ext = GetExtLower(name);
                bool skip = false;
                for (const auto& se : cfg.skipExts)
                    if (ext == se) { skip = true; break; }
                if (!skip && name.find(cfg.tempMark) == std::string::npos)
                    allFiles.push_back(scanDir + "\\" + name);
            } while (FindNextFileA(hFind, &fd));
            FindClose(hFind);
        }
        std::sort(allFiles.begin(), allFiles.end());
    }
    cprint(" [+] Directory File Scanning    : PASSED", Color::GREEN);
    Sleep(1000);

    // --------------------------------------------------------------------------
    // Step 1: 文件选择
    // --------------------------------------------------------------------------
    cprint("===================================================", Color::CYAN);
    cprint(" Whisper Batch Transcription Task (Interactive)    ", Color::CYAN);
    cprint("===================================================", Color::CYAN);

    if (allFiles.empty()) {
        cprint("\n[INFO] No valid media files in: " + scanDir, Color::YELLOW);
        PauseAndExit(0);
    }

    cprint("\n[Step 1] Available Media Files in Directory:", Color::CYAN);
    cprint(" " + scanDir, Color::DCYAN);
    std::cout << "---------------------------------------------------\n";
    cprint(" [0] Process ALL files (Default)", Color::GREEN);
    for (size_t i = 0; i < allFiles.size(); ++i) {
        auto sep = allFiles[i].find_last_of("\\/");
        std::string dname = (sep != std::string::npos) ? allFiles[i].substr(sep + 1) : allFiles[i];
        std::cout << " [" << (i + 1) << "] " << dname << "\n";
    }
    cprint(" [x] Exit", Color::RED);
    std::cout << "---------------------------------------------------\n";
    std::cout << "Enter the file number to process [0]: ";

    std::vector<std::string> targetFiles;
    {
        std::string choice = ReadLineNoCase();
        if (choice.empty()) choice = "0";

        if (choice == "x") {
            cprint("-> Exiting. Goodbye!", Color::YELLOW);
            QuietExit(0);
        } else if (choice == "0") {
            targetFiles = allFiles;
            cprint("-> Selected Mode: Process ALL files.", Color::GREEN);
        } else {
            try {
                int idx = std::stoi(choice) - 1;
                if (idx >= 0 && idx < (int)allFiles.size()) {
                    auto sep = allFiles[idx].find_last_of("\\/");
                    std::string dname = (sep != std::string::npos)
                                        ? allFiles[idx].substr(sep + 1)
                                        : allFiles[idx];
                    targetFiles = { allFiles[idx] };
                    cprint("-> Selected File: " + dname, Color::GREEN);
                } else throw std::out_of_range("");
            } catch (...) {
                cprint(" [!] Invalid selection! Defaulting to ALL files.", Color::YELLOW);
                targetFiles = allFiles;
            }
        }
    }

    // --------------------------------------------------------------------------
    // Step 2: 格式选择
    // --------------------------------------------------------------------------
    cprint("\n[Step 2] Select Output Format:", Color::CYAN);
    std::cout << "---------------------------------------------------\n";
    cprint(" [1] .srt (Subtitles with timestamps) - Default", Color::GREEN);
    std::cout << " [2] .txt (Plain text without timestamps)\n";
    std::cout << " [3] .vtt (WebVTT subtitles)\n";
    cprint(" [x] Exit", Color::RED);
    std::cout << "---------------------------------------------------\n";
    std::cout << "Enter format choice [1]: ";

    std::string formatArg, targetExt;
    {
        std::string fmt = ReadLineNoCase();
        if (fmt.empty()) fmt = "1";

        if (fmt == "x") {
            cprint("-> Exiting. Goodbye!", Color::YELLOW);
            QuietExit(0);
        } else if (fmt == "2") {
            formatArg = cfg.argsTXT; targetExt = ".txt";
            cprint("-> Format Selected: Plain text (.txt)", Color::GREEN);
        } else if (fmt == "3") {
            formatArg = cfg.argsVTT; targetExt = ".vtt";
            cprint("-> Format Selected: WebVTT (.vtt)", Color::GREEN);
        } else {
            formatArg = cfg.argsSRT; targetExt = ".srt";
            cprint("-> Format Selected: Subtitles (.srt)", Color::GREEN);
        }
    }

    // --------------------------------------------------------------------------
    // Step 3: 模型选择
    // --------------------------------------------------------------------------
    // 从 ModelPath 推导模型目录和默认文件名
    std::string modelDir, defaultModelName;
    {
        auto pos = cfg.modelPath.find_last_of("\\/");
        if (!cfg.modelPath.empty() && pos != std::string::npos) {
            modelDir         = cfg.modelPath.substr(0, pos);
            defaultModelName = cfg.modelPath.substr(pos + 1);
        } else {
            modelDir         = scriptDir;
            defaultModelName = cfg.modelPath;
        }
    }

    std::vector<std::string> modelFiles;
    {
        WIN32_FIND_DATAA fd;
        std::string pattern = modelDir + "\\*.bin";
        HANDLE hFind = FindFirstFileA(pattern.c_str(), &fd);
        if (hFind != INVALID_HANDLE_VALUE) {
            do {
                if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY))
                    modelFiles.push_back(std::string(fd.cFileName));
            } while (FindNextFileA(hFind, &fd));
            FindClose(hFind);
        }
        std::sort(modelFiles.begin(), modelFiles.end());
    }

    if (modelFiles.empty()) {
        cprint("[FATAL] No .bin model files found in: " + modelDir, Color::RED);
        cprint("        Set [LastSession] ModelPath in config.ini.", Color::YELLOW);
        PromptEditOrExit(iniPath);
    }

    int defaultModelIdx = 1;
    for (int i = 0; i < (int)modelFiles.size(); ++i) {
        if (modelFiles[i] == defaultModelName) {
            defaultModelIdx = i + 1;
            break;
        }
    }

    cprint("\n[Step 3] Select Whisper Model:", Color::CYAN);
    cprint(" " + modelDir, Color::DCYAN);
    {
        std::string extraStr;
        if (cfg.extraArgs.empty()) {
            extraStr = "(none)";
        } else {
            for (const auto& a : cfg.extraArgs) extraStr += a + " ";
        }
        cprint(" Platform : " + cfg.platformDisplay +
               "  |  ExtraArgs : " + extraStr, Color::YELLOW);
    }
    std::cout << "---------------------------------------------------\n";
    for (size_t i = 0; i < modelFiles.size(); ++i) {
        bool isDefault = (modelFiles[i] == defaultModelName);
        std::string line = " [" + std::to_string(i + 1) + "] " + modelFiles[i];
        if (isDefault) {
            std::cout << Color::GREEN << line
                      << Color::YELLOW << "  [Default]" << Color::RESET << "\n";
        } else {
            std::cout << line << "\n";
        }
    }
    cprint(" [x] Exit", Color::RED);
    std::cout << "---------------------------------------------------\n";
    std::cout << "Enter model number [" << defaultModelIdx << "]: ";

    std::string selectedModelPath;
    {
        std::string choice = ReadLineNoCase();
        if (choice.empty()) choice = std::to_string(defaultModelIdx);

        if (choice == "x") {
            cprint("-> Exiting. Goodbye!", Color::YELLOW);
            QuietExit(0);
        }
        std::string selectedModel;
        try {
            int idx = std::stoi(choice) - 1;
            if (idx >= 0 && idx < (int)modelFiles.size())
                selectedModel = modelFiles[idx];
            else throw std::out_of_range("");
        } catch (...) {
            cprint(" [!] Invalid selection! Using default model.", Color::YELLOW);
            selectedModel = defaultModelName.empty() ? modelFiles[0] : defaultModelName;
        }
        cprint("-> Model Selected: " + selectedModel, Color::GREEN);
        selectedModelPath = modelDir + "\\" + selectedModel;
    }

    if (!PathFileExistsA(selectedModelPath.c_str())) {
        cprint("[FATAL] Selected model file not found: " + selectedModelPath, Color::RED);
        PauseAndExit(1);
    }

    ProcessBatch(cfg, targetFiles, selectedModelPath, formatArg, targetExt);

    std::cout << "\n";
    cprint("===================================================", Color::CYAN);
    cprint(" All transcription jobs handled perfectly!         ", Color::CYAN);
    QuietExit(0);
    return 0;
}
