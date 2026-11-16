@echo off
rem Builds MegaLifeLauncher.exe with the C# compiler that ships with Windows (no Visual Studio needed).
setlocal
pushd "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist src\Secret.cs (
    echo src\Secret.cs is missing: copy Secret.cs.example to src\Secret.cs and put the server key in it.
    popd
    pause
    exit /b 1
)
if not exist "%CSC%" (
    echo .NET Framework 4 compiler not found on this PC.
    popd
    pause
    exit /b 1
)
"%CSC%" /nologo /target:winexe /platform:x86 /optimize+ /out:MegaLifeLauncher.exe /win32icon:src\app.ico /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /resource:src\phone.png,MegaLife.phone.png /resource:logo.png,MegaLife.logo.png src\*.cs > build_log.txt 2>&1
set "RC=%ERRORLEVEL%"
type build_log.txt
echo.
if not "%RC%"=="0" (
    echo BUILD FAILED - send build_log.txt
) else (
    echo BUILD OK - MegaLifeLauncher.exe
)
popd
pause
exit /b %RC%
