@echo off
rem Run this every time you add or change a file in server_files\files (or server_files\allow).
pushd "%~dp0"
if not exist MegaLifeLauncher.exe (
    echo Run build.bat first.
    popd
    pause
    exit /b 1
)
start "" /wait MegaLifeLauncher.exe --make-manifest "%~dp0server_files"
popd
