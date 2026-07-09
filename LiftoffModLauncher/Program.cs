using System.Diagnostics;

namespace LiftoffModLauncher
{
    internal class Program
    {
        // Files/directories that make up the mod loader. BepInEx is a directory,
        // the other two are files, but Enable/Disable treat them uniformly.
        private static readonly string[] ModItems = { "BepInEx", "doorstop_config.ini", "winhttp.dll" };

        private static bool PathExists(string path) => Directory.Exists(path) || File.Exists(path);

        private static void MovePath(string source, string dest)
        {
            if (Directory.Exists(source)) Directory.Move(source, dest);
            else File.Move(source, dest);
        }

        private static void DeletePath(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
        }

        // True when the mod loader is currently active (any active item present).
        private static bool ModsEnabled(string liftoffDirectory)
        {
            foreach (string name in ModItems)
            {
                if (PathExists(Path.Combine(liftoffDirectory, name))) return true;
            }
            return false;
        }

        private static void EnableMods(string liftoffDirectory)
        {
            // For each item: if the active name is already present it's enabled;
            // otherwise restore it from its "_" prefixed backup. Missing both is an error.
            foreach (string name in ModItems)
            {
                string active = Path.Combine(liftoffDirectory, name);
                string backup = Path.Combine(liftoffDirectory, "_" + name);

                if (PathExists(active)) continue; // already enabled

                if (PathExists(backup))
                {
                    MovePath(backup, active);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error: neither '{name}' nor '_{name}' found in Liftoff directory.");
                    Console.ResetColor();
                    throw new FileNotFoundException($"'{name}' not found in Liftoff directory.");
                }
            }
        }

        private static void DisableMods(string liftoffDirectory)
        {
            // For each item: if it isn't active there's nothing to disable; otherwise
            // clear any stale backup and move the active item aside to "_" prefixed.
            // Guarded so a first run (no backups yet) or an already-disabled state
            // does not throw.
            foreach (string name in ModItems)
            {
                string active = Path.Combine(liftoffDirectory, name);
                string backup = Path.Combine(liftoffDirectory, "_" + name);

                if (!PathExists(active)) continue; // already disabled

                DeletePath(backup); // remove stale backup if present
                MovePath(active, backup);
            }
        }

        private static void StartGame(string gamePath)
        {
            // Game remains running, no interference with steam
            Process.Start(new ProcessStartInfo
            {
                FileName = gamePath,
                UseShellExecute = false
            });
        }

        static int Main(string[] args)
        {
            // The game executable path is passed by Steam via %command%. When run
            // without it (e.g. the user double-clicked the launcher), act as a setup
            // helper: print the exact launch-options line with this launcher's own
            // absolute path filled in, so it can be copied straight into Steam.
            if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
            {
                string launcherPath = Environment.ProcessPath ?? "LiftoffModLauncher-win-x64.exe";

                Console.WriteLine("Liftoff Mod Launcher - Setup");
                Console.WriteLine();
                Console.WriteLine("This launcher is meant to be started by Steam, not on its own.");
                Console.WriteLine("Copy the line below into Steam > Liftoff > Properties > Launch Options:");
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\"{launcherPath}\" %command%");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine("Then launch Liftoff from Steam as usual and this menu will appear.");
                Console.WriteLine("Press Enter to exit.");
                Console.ReadLine();
                return 0;
            }

            string gamePath = args[0];
            string? liftoffDirectory = Path.GetDirectoryName(Path.GetFullPath(gamePath));

            if (string.IsNullOrEmpty(liftoffDirectory) || !File.Exists(gamePath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: game executable not found at '{gamePath}'.");
                Console.ResetColor();
                Console.WriteLine("Press Enter to exit.");
                Console.ReadLine();
                return 1;
            }

            // Basic interface with three selectable options.
            // Navigation via arrow keys, number keys, or enter.
            ConsoleColor selectedColor = ConsoleColor.Red;
            string[] options = { "Launch Liftoff", "Launch Liftoff with Mods", "Exit" };
            int selectionOffset = 3; // First option row.

            Console.Clear();
            Console.WriteLine("Liftoff Launcher!!!");
            Console.WriteLine($"Mods are currently {(ModsEnabled(liftoffDirectory) ? "ENABLED" : "disabled")}.");
            Console.WriteLine("Select an option:");
            for (int i = 0; i < options.Length; i++)
            {
                Console.WriteLine($"{i + 1}. {options[i]}");
            }
            Console.WriteLine();
            Console.WriteLine("Use arrow keys or number keys to navigate and Enter to select.");
            Console.WriteLine("Made by AMPW. Distributed under the GPLv3 license. See LICENSE.txt for details.");

            int currentSelection = 0;

            void Render()
            {
                for (int i = 0; i < options.Length; i++)
                {
                    Console.SetCursorPosition(0, selectionOffset + i);
                    if (i == currentSelection) Console.ForegroundColor = selectedColor;
                    Console.Write($"{i + 1}. {options[i]}");
                    Console.ResetColor();
                }
            }

            Render();

            while (true)
            {
                ConsoleKeyInfo keyInfo = Console.ReadKey(true);

                switch (keyInfo.Key)
                {
                    case ConsoleKey.UpArrow:
                        if (currentSelection > 0) currentSelection--;
                        Render();
                        break;
                    case ConsoleKey.DownArrow:
                        if (currentSelection < options.Length - 1) currentSelection++;
                        Render();
                        break;
                    case ConsoleKey.D1:
                    case ConsoleKey.NumPad1:
                        currentSelection = 0;
                        Render();
                        break;
                    case ConsoleKey.D2:
                    case ConsoleKey.NumPad2:
                        currentSelection = 1;
                        Render();
                        break;
                    case ConsoleKey.D3:
                    case ConsoleKey.NumPad3:
                        currentSelection = 2;
                        Render();
                        break;
                    case ConsoleKey.Enter:
                        Console.SetCursorPosition(0, selectionOffset + options.Length + 1);
                        try
                        {
                            switch (currentSelection)
                            {
                                case 0:
                                    Console.WriteLine("Starting Liftoff...");
                                    DisableMods(liftoffDirectory);
                                    StartGame(gamePath);
                                    return 0;
                                case 1:
                                    Console.WriteLine("Starting Liftoff with mods...");
                                    EnableMods(liftoffDirectory);
                                    StartGame(gamePath);
                                    return 0;
                                default:
                                    return 0;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Failed to launch: {ex.Message}");
                            Console.ResetColor();
                            Console.WriteLine("Press Enter to exit.");
                            Console.ReadLine();
                            return 1;
                        }
                    default:
                        break;
                }
            }
        }
    }
}
