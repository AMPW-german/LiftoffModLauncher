# LiftoffModLauncher

Simple launcher for Liftoff: Drone Racing to easily start the game with and without mods.

## Features

- **Launch with or without mods** from a single menu — toggling mods on or off before the game starts.
- **Setup helper:** when you run the launcher directly (not via Steam) it prints the exact Steam launch-options line for *your* install, with the full path and quotes already filled in.
- **BepInEx and Moving Objects installer/updater**: on startup the launcher checks the installed
  [Liftoff.MovingObjects](https://github.com/geekhostuk/Liftoff.MovingObjects) mod and BepInEx (5.x) against the latest
  release and lets you update or install it in place. See below.

## Mod update checking

When the launcher opens it reads the locally installed versions of both **BepInEx** and
`Liftoff.MovingObjects.dll`, then compares them against the latest GitHub releases:

- [BepInEx releases](https://github.com/BepInEx/BepInEx/releases)
- [Liftoff.MovingObjects releases](https://github.com/geekhostuk/Liftoff.MovingObjects/releases)

The check runs in the background, so the menu appears immediately even with a slow or offline connection.

Instead of a separate hotkey, update/install actions are shown directly as dedicated selectable menu entries:

- `Moving Objects: up to date (v...)` or `Moving Objects: update (v... -> v...)`
- `Moving Objects: not installed (install latest v...?)`
- `BepInEx: up to date (v...)` or `BepInEx: update (v... -> v...)`
- `BepInEx: not installed (install latest v...?)`
- `... (couldn't check for updates)` when GitHub cannot be reached

When an update or install is available, select the corresponding menu option and press **Enter**.
The launcher downloads the release ZIP, extracts it, replaces files in place, and rolls back from backup
automatically if anything fails.

![Update available in the launcher](UpdateCheckExample.png)

### Disclaimer
Only the windows build is tested and supported. The Linux and MacOS builds are untested and probably won't work.\
Please report any issues you encounter on the [Issues](https://github.com/AMPW-german/LiftoffModLauncher/issues) page.\
The launcher is not affiliated with or endorsed by LuGus Studios. It is a third-party tool created by the community to enhance the modding experience for Liftoff: Drone Racing.\
The launcher has a size of ~70-80MB due to the .NET runtime being bundled with it. It is a standalone application and does not require any additional dependencies to run.

## Installation

1. Download the latest release for your OS from the [Releases](https://github.com/AMPW-german/LiftoffModLauncher/releases) page.
2. Extract the downloaded archive to a folder of your choice (can be in the same folder as the game).
3. **Double-click the launcher executable once.** Because it wasn't started by Steam, it shows a setup screen and prints the exact launch-options line for *your* install, already containing the full path and quotes, e.g.:\
   `"D:\SteamLibrary\steamapps\common\Liftoff\LiftoffModLauncher-win-x64.exe" %command%`
4. Open Steam and go to your Library. Right-click on Liftoff: Drone Racing and select "Properties".
5. In the "General" tab, paste that line into the "Launch Options" input field.

> **Note:** A full path is required — a relative path such as `.\LiftoffModLauncher-win-x64.exe` will not work, because Steam does not set the launcher's working directory to the game folder. Using the line the launcher prints in step 3 avoids having to type the path by hand.

## Building from source
Open a terminal in the root folder of the repository and run run the build.bat script. It builds the project for all platforms.
