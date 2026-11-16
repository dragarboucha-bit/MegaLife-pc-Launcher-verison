# Mega Life Launcher (PC)

Windows launcher for Mega Life Roleplay (SA-MP 0.3.7 + SampVoice): OFFICIAL / TEST server,
hourly server key, automatic files (skins, cars, textures, SampVoice) and automatic launcher update.

## How the update works
- The launcher reads **`pc.json`** in this repository at every start.
- **Files** (textures, skins, cars, SampVoice) come from the ZIP in `pack.url` (MediaFire link).
  Only files whose SHA-256 changed are installed.
- **Launcher**: when `launcher.build` is bigger than the build of the player's launcher, it downloads
  `launcher.url` (MediaFire link of `MegaLife_Launcher.zip`), checks the SHA-256 and replaces itself.
  `"force": true` = the old launcher can not be used any more.

## Publish an update (admin)
1. Put the files in `server_files\files`, `server_files\plugins`, `server_files\img` (as before).
2. New launcher? Change `AppBuild` (+1) and `AppVersion` in `src\Core.cs`, then run `build.bat`.
3. Run `make_release.bat` -> folder `release\`:
   - `MegaLife_PC_Files.zip` -> upload to MediaFire
   - `MegaLife_Launcher.zip` -> upload to MediaFire (this is also the download for new players)
   - `pc.json`
4. In `pc.json` paste the two MediaFire links: `"pack": {"url": ...}` and `"launcher": {"url": ...}`.
5. Replace `pc.json` in this repository (GitHub -> pc.json -> Edit -> paste -> Commit). Done:
   every player updates at the next start.

## Build
- `src\Secret.cs` holds the hourly server key and is **not** in this repository (see `.gitignore`).
  Copy `Secret.cs.example` to `src\Secret.cs` and fill it in (same key as LAUNCHER_SECRET in MLRP.pwn).
- `build.bat` (C# compiler of Windows / .NET Framework 4.5+, no Visual Studio needed).
- Logo, phone picture and icon are inside the exe: players only get `MegaLifeLauncher.exe`.
