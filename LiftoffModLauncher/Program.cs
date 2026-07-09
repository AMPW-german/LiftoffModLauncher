using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LiftoffModLauncher
{
    internal class Program
    {
        private enum MenuOption
        {
            LaunchLiftoff,
            LaunchLiftoffWithMods,
            UpdateMovingObjectsMod,
            Exit
        }

        private static void EnableMods(string gameDirectory)
        {
            // Check for BepInEx in the game directory
            // If it is not present, check if a "_{name}" directory exists and rename it

            string bepInExDirectory = Path.Combine(gameDirectory, "BepInEx");
            string bepInExSaveDirectory = Path.Combine(gameDirectory, "_BepInEx");

            if (!Directory.Exists(bepInExDirectory))
            {
                if (Directory.Exists(bepInExSaveDirectory)) Directory.Move(bepInExSaveDirectory, bepInExDirectory);
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: BepInEx directory not found in Liftoff directory.");
                    Console.ReadLine();
                    throw new DirectoryNotFoundException("BepInEx directory not found in Liftoff directory.");
                }
            }
        }

        private static void DisableMods(string gameDirectory)
        {
            try
            {
                // Delete the _BepInEx directory if it exists
                // Rename the BepInEx directory to _BepInEx
                string bepInExSaveDirectory = Path.Combine(gameDirectory, "_BepInEx");

                if (Directory.Exists(bepInExSaveDirectory)) Directory.Delete(bepInExSaveDirectory, true);

                Directory.Move(Path.Combine(gameDirectory, "BepInEx"), bepInExSaveDirectory);
            }
            catch (Exception ex)
            {
                Console.Clear();
                Console.SetCursorPosition(0, 0);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error disabling mods: {ex.Message}");
                Console.ReadLine();
                throw;
            }
        }

        private static void RestoreLegacyBootstrapFiles(string gameDirectory)
        {
            string doorstopConfigFile = Path.Combine(gameDirectory, "doorstop_config.ini");
            string doorstopConfigSaveFile = Path.Combine(gameDirectory, "_doorstop_config.ini");
            string winhttpFile = Path.Combine(gameDirectory, "winhttp.dll");
            string winhttpSaveFile = Path.Combine(gameDirectory, "_winhttp.dll");

            if (!File.Exists(doorstopConfigFile) && File.Exists(doorstopConfigSaveFile))
            {
                File.Move(doorstopConfigSaveFile, doorstopConfigFile);
            }

            if (!File.Exists(winhttpFile) && File.Exists(winhttpSaveFile))
            {
                File.Move(winhttpSaveFile, winhttpFile);
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

        public static async Task<string?> GetLatestReleaseTagAsync(
        string repoPath,
        CancellationToken cancellationToken = default)
        {
            using var http = new HttpClient();

            // Required by GitHub API
            http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("LiftoffModLauncher", "1.0"));

            // Recommended API version header
            http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
            http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var url = $"https://api.github.com/repos/{repoPath}/releases/latest";
            using var response = await http.GetAsync(url, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // No releases (or repo not found / no access)
                return null;
            }

            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return doc.RootElement.GetProperty("tag_name").GetString();
        }

        public static async Task UpdateMovingObjectsMod(string gameDirectory, string repoPath, CancellationToken cancellationToken = default)
        {
            string modUpdateDirectory = Path.Combine(gameDirectory, "modUpdate");
            string bepInExDirectory = Path.Combine(gameDirectory, "BepInEx");
            string bepInExSaveDirectory = Path.Combine(gameDirectory, "_BepInEx");

            if (!Directory.Exists(bepInExDirectory) && Directory.Exists(bepInExSaveDirectory)) bepInExDirectory = bepInExSaveDirectory;

            Console.Clear();
            Console.SetCursorPosition(0, 0);
            Console.WriteLine("Updating Moving Objects Mod...");

            try
            {
                if (Directory.Exists(modUpdateDirectory)) Directory.Delete(modUpdateDirectory, true);

                Directory.CreateDirectory(modUpdateDirectory);

                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue("LiftoffModLauncher", "1.0"));
                http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
                http.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                var latestReleaseUrl = $"https://api.github.com/repos/{repoPath}/releases/latest";
                using var latestResponse = await http.GetAsync(latestReleaseUrl, cancellationToken);
                latestResponse.EnsureSuccessStatusCode();

                await using var latestJsonStream = await latestResponse.Content.ReadAsStreamAsync(cancellationToken);
                using var latestJson = await JsonDocument.ParseAsync(latestJsonStream, cancellationToken: cancellationToken);

                JsonElement? selectedAsset = null;
                foreach (var asset in latestJson.RootElement.GetProperty("assets").EnumerateArray())
                {
                    var assetName = asset.GetProperty("name").GetString() ?? string.Empty;
                    if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                        !assetName.Contains("source", StringComparison.OrdinalIgnoreCase))
                    {
                        selectedAsset = asset;
                        break;
                    }
                }

                if (selectedAsset is null)
                {
                    throw new InvalidOperationException("No zip artifact found in the latest release.");
                }

                var assetNameValue = selectedAsset.Value.GetProperty("name").GetString() ?? "latest-release.zip";
                var downloadUrl = selectedAsset.Value.GetProperty("browser_download_url").GetString()
                    ?? throw new InvalidOperationException("No download URL found for selected release artifact.");

                var zipFilePath = Path.Combine(modUpdateDirectory, assetNameValue);
                using var downloadResponse = await http.GetAsync(downloadUrl, cancellationToken);
                downloadResponse.EnsureSuccessStatusCode();

                Console.WriteLine("Downloading latest release...");
                await using (var targetFile = File.Create(zipFilePath))
                {
                    await downloadResponse.Content.CopyToAsync(targetFile, cancellationToken);
                }
                Console.WriteLine("Finished downloading latest release. Replacing old files...");

                string extractDirectory = Path.Combine(modUpdateDirectory, "extracted");
                Directory.CreateDirectory(extractDirectory);
                ZipFile.ExtractToDirectory(zipFilePath, extractDirectory, true);

                string? movingObjectsDllPath = Directory.GetFiles(extractDirectory, "Liftoff.MovingObjects.dll", SearchOption.AllDirectories).FirstOrDefault();
                string? movingObjectsPatcherDllPath = Directory.GetFiles(extractDirectory, "Liftoff.MovingObjects.Patcher.dll", SearchOption.AllDirectories).FirstOrDefault();

                if (movingObjectsDllPath is null)
                {
                    throw new FileNotFoundException("Liftoff.MovingObjects.dll was not found in the downloaded artifact.");
                }

                if (movingObjectsPatcherDllPath is null)
                {
                    throw new FileNotFoundException("Liftoff.MovingObjects.Patcher.dll was not found in the downloaded artifact.");
                }

                string pluginsDirectory = Path.Combine(bepInExDirectory, "plugins");
                string patchersDirectory = Path.Combine(bepInExDirectory, "patchers");
                Directory.CreateDirectory(pluginsDirectory);
                Directory.CreateDirectory(patchersDirectory);

                File.Copy(movingObjectsDllPath, Path.Combine(pluginsDirectory, "Liftoff.MovingObjects.dll"), true);
                File.Copy(movingObjectsPatcherDllPath, Path.Combine(patchersDirectory, "Liftoff.MovingObjects.Patcher.dll"), true);
                Console.WriteLine("Finished updating Moving Objects Mod.");
                Thread.Sleep(2000); // Wait for 2 seconds before clearing the console
            }
            catch (Exception ex)
            {
                Console.Clear();
                Console.SetCursorPosition(0, 0);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error updating Moving Objects Mod: {ex.Message}");
                Console.ReadLine();
                throw;
            }
            finally
            {
                if (Directory.Exists(modUpdateDirectory)) Directory.Delete(modUpdateDirectory, true);
            }
        }

        private static async Task Main(string[] args)
        {
            // Basic interface with three selectable options
            // Navigation via arrow keys and selection via enter key

            // TODO: Read the file version from BepInEx/plugins/Liftoff.MovingObjects.dll and compare it to the newest version on GitHub
            // If the version is outdated, add an update option to the menu and display a warning message
            // Note: the version on JMTs website is often newer than the one on GitHub but no version number is provided there

            string liftoffDirectory = Path.GetDirectoryName(args[0]);
            RestoreLegacyBootstrapFiles(liftoffDirectory);
            bool movingObjectsUpdateAvailable = false;

            string bepInExDirectory = Path.Combine(liftoffDirectory, "BepInEx");
            string bepInExSaveDirectory = Path.Combine(liftoffDirectory, "_BepInEx");
            if (!Directory.Exists(bepInExDirectory) && Directory.Exists(bepInExSaveDirectory)) bepInExDirectory = bepInExSaveDirectory;

            if (File.Exists(Path.Combine(bepInExDirectory, "plugins/Liftoff.MovingObjects.dll")))
            {
                // Check if the version is outdated
                string currentVersionString = FileVersionInfo.GetVersionInfo(Path.Combine(bepInExDirectory, "plugins/Liftoff.MovingObjects.dll"))?.FileVersion ?? "Not found";
                Version currentVersion = new Version(currentVersionString);
                currentVersion = new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build); // Ignore the revision number for comparison

                var tag = await GetLatestReleaseTagAsync("geekhostuk/Liftoff.MovingObjects");

                if (tag != null)
                {
                    tag = tag.Replace("v", ""); // Remove the 'v' prefix if present
                    Version latestVersion = new Version(tag);

                    movingObjectsUpdateAvailable = latestVersion > currentVersion;
                }
            }

            SortedDictionary<MenuOption, string> menuOptions = new SortedDictionary<MenuOption, string>
            {
                { MenuOption.LaunchLiftoff, "Launch Liftoff" },
                { MenuOption.LaunchLiftoffWithMods, "Launch Liftoff with Mods" },
                { MenuOption.Exit, "Exit" }
            };

            if (movingObjectsUpdateAvailable) menuOptions.Add(MenuOption.UpdateMovingObjectsMod, "Update Moving Objects Mod (GitHub only)");

            ConsoleColor selectedColor = ConsoleColor.Red;

            Console.WriteLine("Liftoff Launcher!!!");
            Console.WriteLine("Select an option:");
            Console.ForegroundColor = selectedColor;
            Console.WriteLine($"1. {menuOptions.First().Value}");
            Console.ResetColor();
            for (int i = 1; i < menuOptions.Count; i++)
            {
                Console.WriteLine($"{i + 1}. {menuOptions.ElementAt(i).Value}");
            }

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
                            Console.Write($"{currentSelection + 1}. {menuOptions.ElementAt(currentSelection).Value}");
                            currentSelection--;
                            Console.SetCursorPosition(0, selectionOffset + currentSelection);
                            Console.ForegroundColor = selectedColor;
                            Console.Write($"{currentSelection + 1}. {menuOptions.ElementAt(currentSelection).Value}");
                            Console.ResetColor();
                        }
                        break;
                    case ConsoleKey.DownArrow:
                        if (currentSelection < menuOptions.Count - 1)
                        {
                            Console.SetCursorPosition(0, selectionOffset + currentSelection);
                            Console.Write($"{currentSelection + 1}. {menuOptions.ElementAt(currentSelection).Value}"); // Reset previous selection color
                            currentSelection++;
                            Console.SetCursorPosition(0, selectionOffset + currentSelection);
                            Console.ForegroundColor = selectedColor;
                            Console.Write($"{currentSelection + 1}. {menuOptions.ElementAt(currentSelection).Value}");
                            Console.ResetColor();
                        }
                        break;
                    case ConsoleKey.Enter:
                        switch (menuOptions.ElementAt(currentSelection).Key)
                        {
                            case MenuOption.LaunchLiftoff:
                                DisableMods(liftoffDirectory);
                                StartGame(args[0]);
                                return;
                            case MenuOption.LaunchLiftoffWithMods:
                                EnableMods(liftoffDirectory);
                                StartGame(args[0]);
                                return;
                            case MenuOption.UpdateMovingObjectsMod:
                                await UpdateMovingObjectsMod(liftoffDirectory, "geekhostuk/Liftoff.MovingObjects");
                                Console.Clear();
                                Console.SetCursorPosition(0, 0);
                                menuOptions.Remove(MenuOption.UpdateMovingObjectsMod);
                                Console.WriteLine("Liftoff Launcher!!!");
                                Console.WriteLine("Select an option:");
                                Console.ForegroundColor = selectedColor;
                                Console.WriteLine($"1. {menuOptions.First().Value}");
                                Console.ResetColor();
                                for (int i = 1; i < menuOptions.Count; i++)
                                {
                                    Console.WriteLine($"{i + 1}. {menuOptions.ElementAt(i).Value}");
                                }

                                Console.SetCursorPosition(0, Console.WindowHeight - 3);
                                Console.WriteLine("Use arrow keys to navigate and enter to select.");
                                Console.WriteLine("Made by AMPW. Distributed under the GPLv3 license. See LICENSE.txt for details.");

                                currentSelection = 0;
                                Console.SetCursorPosition(0, selectionOffset + currentSelection);
                                break;
                            case MenuOption.Exit:
                            default:
                                return;
                        }
                        break;
                    default:
                        break;
                }
            }
        }
    }
}
