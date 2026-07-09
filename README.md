# LiftoffModLauncher

Simple launcher for Liftoff: Drone Racing to easily start the game with and without mods.

### Disclaimer
Only the windows build is tested and supported. The Linux and MacOS builds are untested and may not work.
Please report any issues you encounter on the [Issues](https://github.com/AMPW-german/LiftoffModLauncher/issues) page.

## Installation

1. Download the latest release for your OS from the [Releases](https://github.com/AMPW-german/LiftoffModLauncher/releases) page.
2. Extract the downloaded archive to a folder of your choice (can be in the same folder as the game).
3. **Double-click the launcher executable once.** Because it wasn't started by Steam, it shows a setup screen and prints the exact launch-options line for *your* install, already containing the full path and quotes, e.g.:\
   `"D:\SteamLibrary\steamapps\common\Liftoff\LiftoffModLauncher-win-x64.exe" %command%`
4. Open Steam and go to your Library. Right-click on Liftoff: Drone Racing and select "Properties".
5. In the "General" tab, paste that line into the "Launch Options" input field.

> **Note:** A full path is required — a relative path such as `.\LiftoffModLauncher-win-x64.exe` will not work, because Steam does not set the launcher's working directory to the game folder. Using the line the launcher prints in step 3 avoids having to type the path by hand.

![Launcher preview](LauncherExample.png)

## Building from source
Open a terminal in the root folder of the repository and run run the build.bat script. It builds the project for all platforms.
