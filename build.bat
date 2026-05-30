@echo off
:: ===========================================================
:: whispercli build script (TDM-GCC / MinGW)
:: ===========================================================
title Building whispercli...

:: Compiler path - change this line if needed
set MINGW_BIN=c:\PROGRA~2\Dev-Cpp\MinGW64\bin

:: Switch to the script directory so all paths are relative
cd /d "%~dp0"

:: Check g++
"%MINGW_BIN%\g++.exe" --version >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [FATAL] g++ not found at: %MINGW_BIN%
    pause
    exit /b 1
)
echo [Build] Compiler: %MINGW_BIN%\g++.exe

:: Check windres and whispercli.ico
"%MINGW_BIN%\windres.exe" --version >nul 2>&1
if %ERRORLEVEL% NEQ 0 goto compile_no_icon
if not exist "whispercli.ico" (
    echo [WARN] whispercli.ico not found, building without icon...
    goto compile_no_icon
)

:: Step 1: Compile resource file
echo [Build] Compiling resource file...
"%MINGW_BIN%\windres.exe" whispercli.rc -O coff -o app.res.o
if %ERRORLEVEL% NEQ 0 (
    echo [WARN] Resource compilation failed, building without icon...
    goto compile_no_icon
)

:: Step 2: Compile with icon
echo [Build] Compiling whispercli.cpp with icon...
"%MINGW_BIN%\g++.exe" -std=c++14 -mconsole -O2 -o whispercli.exe whispercli.cpp app.res.o -lkernel32 -lshlwapi
goto done

:compile_no_icon
echo [Build] Compiling whispercli.cpp without icon...
"%MINGW_BIN%\g++.exe" -std=c++14 -mconsole -O2 -o whispercli.exe whispercli.cpp -lkernel32 -lshlwapi

:done
if %ERRORLEVEL% EQU 0 (
    echo [Build] SUCCESS - whispercli.exe generated.
    if exist app.res.o del app.res.o
) else (
    echo [Build] FAILED - check errors above.
)
pause
