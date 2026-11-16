@echo off
rem Makes release\MegaLife_PC_Files.zip, release\MegaLife_Launcher.zip and release\pc.json
rem from server_files (files, plugins, img). Run build.bat first.
pushd "%~dp0"
if not exist MegaLifeLauncher.exe (
    echo Run build.bat first.
    popd
    pause
    exit /b 1
)
start "" /wait MegaLifeLauncher.exe --make-release "%~dp0server_files"
popd
