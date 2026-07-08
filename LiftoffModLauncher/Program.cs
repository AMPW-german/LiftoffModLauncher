using System.Diagnostics;

namespace LiftoffModLauncher
{
    internal class Program
    {
        private static void EnableMods(string gamePath)
        {
            // Check for BepInEx, doorstop_config.ini and winhttp.dll in the game directory
            // If they are not present, check if a "_{name}" file/directory exists and rename it

            string liftoffDirectory = Path.GetDirectoryName(gamePath);

            if (!Directory.Exists(Path.Combine(liftoffDirectory, "BepInEx")))
            {
                if (Directory.Exists(Path.Combine(liftoffDirectory, "_BepInEx"))) Directory.Move(Path.Combine(liftoffDirectory, "_BepInEx"), Path.Combine(liftoffDirectory, "BepInEx"));
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: BepInEx directory not found in Liftoff directory.");
                    Console.ReadLine();
                    throw new DirectoryNotFoundException("BepInEx directory not found in Liftoff directory.");
                }
            }

            if (!File.Exists(Path.Combine(liftoffDirectory, "doorstop_config.ini")))
            {
                if (File.Exists(Path.Combine(liftoffDirectory, "_doorstop_config.ini"))) File.Move(Path.Combine(liftoffDirectory, "_doorstop_config.ini"), Path.Combine(liftoffDirectory, "doorstop_config.ini"));
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: doorstop_config.ini not found in Liftoff directory.");
                    Console.ReadLine();
                    throw new FileNotFoundException("doorstop_config.ini not found in Liftoff directory.");
                }
            }

            if (!File.Exists(Path.Combine(liftoffDirectory, "winhttp.dll")))
            {
                if (File.Exists(Path.Combine(liftoffDirectory, "_winhttp.dll"))) File.Move(Path.Combine(liftoffDirectory, "_winhttp.dll"), Path.Combine(liftoffDirectory, "winhttp.dll"));
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: winhttp.dll not found in Liftoff directory.");
                    Console.ReadLine();
                    throw new FileNotFoundException("winhttp.dll not found in Liftoff directory.");
                }
            }
        }

        private static void DisableMods(string gamePath)
        {
            // Delete the _BepInEx directory, _doorstop_config.ini and _winhttp.dll if they exist
            // Rename the BepInEx directory, doorstop_config.ini and winhttp.dll to _BepInEx, _doorstop_config.ini and _winhttp.dll respectively
            string liftoffDirectory = Path.GetDirectoryName(gamePath);

            Directory.Delete(Path.Combine(liftoffDirectory, "_BepInEx"), true);
            File.Delete(Path.Combine(liftoffDirectory, "_doorstop_config.ini"));
            File.Delete(Path.Combine(liftoffDirectory, "_winhttp.dll"));

            Directory.Move(Path.Combine(liftoffDirectory, "BepInEx"), Path.Combine(liftoffDirectory, "_BepInEx"));
            File.Move(Path.Combine(liftoffDirectory, "doorstop_config.ini"), Path.Combine(liftoffDirectory, "_doorstop_config.ini"));
            File.Move(Path.Combine(liftoffDirectory, "winhttp.dll"), Path.Combine(liftoffDirectory, "_winhttp.dll"));
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

        static void Main(string[] args)
        {
            // Basic interface with three selectable options
            // Navigation via arrow keys and selection via enter key

            ConsoleColor selectedColor = ConsoleColor.Red;

            Console.WriteLine("Liftoff Launcher!!!");
            Console.WriteLine("Select an option:");
            Console.ForegroundColor = selectedColor;
            Console.Write("1");
            Console.ResetColor();
            Console.WriteLine(". Launch Liftoff");
            Console.WriteLine("2. Launch Liftoff with Mods");
            Console.WriteLine("3. Exit");
            Console.SetCursorPosition(0, Console.WindowHeight - 3);
            Console.WriteLine("Use arrow keys to navigate and enter to select.");
            Console.WriteLine("Made by AMPW. Distributed under the GPLv3 license. See LICENSE.txt for details.");

            int currentSelection = 0;
            int selectionOffset = 2; // Offset for the selection indicator
            Console.SetCursorPosition(0, selectionOffset + currentSelection);

            while (true)
            {
                ConsoleKeyInfo keyInfo = Console.ReadKey(true);

                switch (keyInfo.Key)
                {
                    case ConsoleKey.UpArrow:
                        if (currentSelection > 0)
                        {
                            Console.SetCursorPosition(0, selectionOffset + currentSelection);
                            Console.Write(currentSelection + 1); // Reset previous selection color
                            currentSelection--;
                            Console.SetCursorPosition(0, selectionOffset + currentSelection);
                            Console.ForegroundColor = selectedColor;
                            Console.Write(currentSelection + 1);
                            Console.ResetColor();
                        }
                        break;
                    case ConsoleKey.DownArrow:
                        if (currentSelection < 2)
                        {
                            Console.SetCursorPosition(0, selectionOffset + currentSelection);
                            Console.Write(currentSelection + 1); // Reset previous selection color
                            currentSelection++;
                            Console.SetCursorPosition(0, selectionOffset + currentSelection);
                            Console.ForegroundColor = selectedColor;
                            Console.Write(currentSelection + 1);
                            Console.ResetColor();
                        }
                        break;
                    case ConsoleKey.Enter:
                        switch (currentSelection)
                        {
                            case 0:
                                Console.WriteLine("Starting liftoff");
                                DisableMods(args[0]);
                                StartGame(args[0]);
                                return;
                            case 1:
                                Console.WriteLine("Starting liftoff with mods");
                                EnableMods(args[0]);
                                StartGame(args[0]);
                                return;
                            case 2:
                            default:
                                return;
                        }
                    default:
                        break;
                }
            }
        }
    }
}
