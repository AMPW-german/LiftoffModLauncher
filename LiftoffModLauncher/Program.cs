using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LiftoffModLauncher
{
    internal class Program
    {
        private static string liftoffDirectory = string.Empty;

        // Files/directories that make up the mod loader. BepInEx is a directory,
        // the other two are files, but Enable/Disable treat them uniformly.
        private static readonly string[] ModItems = { "BepInEx", "doorstop_config.ini", "winhttp.dll" };

        // The Moving Objects mod: the plugin DLL (whose version we read) plus the
        // GitHub repo we check for the latest release.
        private const string MovingObjectsDllName = "Liftoff.MovingObjects.dll";
        private const string MovingObjectsReleasesApiUrl = "https://api.github.com/repos/geekhostuk/Liftoff.MovingObjects/releases/latest";

        // Shared HttpClient. GitHub's API rejects requests without a User-Agent.
        private static HttpClient Http;

        // Serialises all console writes: the background update check rewrites the
        // hint line while the input loop redraws the menu on another thread.
        private static readonly object ConsoleLock = new object();

        // Set by the background check, read by the input loop when the user presses U.
        private static volatile bool _updateAvailable;
        private static volatile string? _updateZipUrl;

        private enum MenuOption
        {
            LaunchLiftoff,
            LaunchLiftoffWithMods,
            UpdateMovingObjectsMod,
            UpdateBepInEx,
            Exit,
        }

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
                    Console.Clear();
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error: neither '{name}' nor '_{name}' found in Liftoff directory.");
                    Console.ResetColor();
                    Console.Read();
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

        // The BepInEx tree is renamed to "_BepInEx" while mods are disabled, so look
        // in both. Returns the path to the installed plugin DLL, or null if absent.
        private static string? FindModDll(string liftoffDirectory, string dllName)
        {
            foreach (string modRoot in new[] { "BepInEx", "_BepInEx" })
            {
                string candidate = Path.Combine(liftoffDirectory, modRoot, "plugins", dllName);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        // Read the version embedded in the plugin DLL by inspecting its file metadata
        // (never loads/runs the assembly).
        private static Version? ReadLocalVersion(string dllPath)
        {
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(dllPath);
                return ParseVersion(info.FileVersion);
            }
            catch
            {
                return null;
            }
        }

        // Normalise a version string ("v1.3.6", "1.3.6.0", "1.3.6+githash") to a
        // Major.Minor.Build Version for comparison.
        private static Version? ParseVersion(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            Match m = Regex.Match(raw, @"\d+(?:\.\d+)*");
            if (!m.Success) return null;

            string[] parts = m.Value.Split('.');
            int major = parts.Length > 0 ? int.Parse(parts[0]) : 0;
            int minor = parts.Length > 1 ? int.Parse(parts[1]) : 0;
            int build = parts.Length > 2 ? int.Parse(parts[2]) : 0;
            return new Version(major, minor, build);
        }


        private static string FormatVersion(Version? v) => v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";

        // Fetch the latest GitHub release. Returns the tag plus the download URL of the
        // .zip asset (null if the release has no zip). Returns null on any failure so
        // the caller never has to handle exceptions or blocks.
        private static async Task<(string Tag, string? ZipUrl)?> FetchLatestReleaseAsync(string ReleaseUrl, string platform = "win_x64")
        {
            try
            {
                using HttpResponseMessage response = await Http.GetAsync(ReleaseUrl);
                response.EnsureSuccessStatusCode();

                await using Stream stream = await response.Content.ReadAsStreamAsync();
                using JsonDocument doc = await JsonDocument.ParseAsync(stream);
                JsonElement root = doc.RootElement;

                string? tag = root.TryGetProperty("tag_name", out JsonElement tagEl)
                    ? tagEl.GetString()
                    : null;
                if (string.IsNullOrEmpty(tag)) return null;

                string? zipUrl = null;
                if (root.TryGetProperty("assets", out JsonElement assets) &&
                    assets.ValueKind == JsonValueKind.Array)
                {
                    // If there's only one asset, check if it's a zip file and get its download URL
                    if (assets.GetArrayLength() == 1)
                    {
                        string? name = assets[0].TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
                        if (name != null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            zipUrl = assets[0].TryGetProperty("browser_download_url", out JsonElement u)
                                ? u.GetString()
                                : null;
                        }
                    }
                    else
                    {
                        foreach (JsonElement asset in assets.EnumerateArray())
                        {
                            string? name = asset.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
                            if (name != null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && name.Contains(platform, StringComparison.OrdinalIgnoreCase))
                            {
                                zipUrl = asset.TryGetProperty("browser_download_url", out JsonElement u)
                                    ? u.GetString()
                                    : null;
                                break;
                            }
                        }
                    }
                }

                return (tag, zipUrl);
            }
            catch
            {
                return null;
            }
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

            var backups = new List<(string Target, string Backup)>();
            var copiedTargets = new List<string>();

            try
            {
                if (Directory.Exists(modUpdateDirectory)) Directory.Delete(modUpdateDirectory, true);

                Directory.CreateDirectory(modUpdateDirectory);

                var latestReleaseUrl = $"https://api.github.com/repos/{repoPath}/releases/latest";
                using var latestResponse = await Http.GetAsync(latestReleaseUrl, cancellationToken);
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
                using var downloadResponse = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                downloadResponse.EnsureSuccessStatusCode();

                Console.WriteLine("Downloading latest release...");
                long? totalBytes = downloadResponse.Content.Headers.ContentLength;
                await using (var targetFile = File.Create(zipFilePath))
                {
                    await using Stream sourceStream = await downloadResponse.Content.ReadAsStreamAsync(cancellationToken);
                    byte[] buffer = new byte[81920];
                    long bytesReadTotal = 0;
                    DateTime lastProgressOutputUtc = DateTime.MinValue;

                    while (true)
                    {
                        int bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken);
                        if (bytesRead == 0) break;

                        await targetFile.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                        bytesReadTotal += bytesRead;

                        bool shouldUpdate = DateTime.UtcNow - lastProgressOutputUtc >= TimeSpan.FromMilliseconds(250);
                        if (!shouldUpdate && totalBytes.HasValue && bytesReadTotal == totalBytes.Value)
                        {
                            shouldUpdate = true;
                        }

                        if (!shouldUpdate) continue;

                        if (totalBytes.HasValue && totalBytes.Value > 0)
                        {
                            double percent = (double)bytesReadTotal * 100d / totalBytes.Value;
                            Console.Write($"\rDownloading latest release... {percent:0.0}% ({bytesReadTotal / 1024d / 1024d:0.00} MB / {totalBytes.Value / 1024d / 1024d:0.00} MB)   ");
                        }
                        else
                        {
                            Console.Write($"\rDownloading latest release... {bytesReadTotal / 1024d / 1024d:0.00} MB   ");
                        }

                        lastProgressOutputUtc = DateTime.UtcNow;
                    }
                }
                Console.WriteLine();
                Console.WriteLine("Finished downloading latest release. Replacing old files...");

                string extractDirectory = Path.Combine(modUpdateDirectory, "extracted");
                Directory.CreateDirectory(extractDirectory);
                ZipFile.ExtractToDirectory(zipFilePath, extractDirectory, true);

                string sourceBepInExDirectory = Path.Combine(extractDirectory, "BepInEx");
                if (!Directory.Exists(sourceBepInExDirectory))
                {
                    throw new InvalidDataException("Downloaded archive did not contain a BepInEx folder.");
                }

                string[] sourceFiles = Directory.GetFiles(sourceBepInExDirectory, "*", SearchOption.AllDirectories);
                if (sourceFiles.Length == 0)
                {
                    throw new InvalidDataException("Downloaded archive did not contain any files in BepInEx.");
                }

                for (int i = 0; i < sourceFiles.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string sourceFile = sourceFiles[i];
                    string relativePath = Path.GetRelativePath(sourceBepInExDirectory, sourceFile);
                    string targetFilePath = Path.Combine(bepInExDirectory, relativePath);
                    string? targetDirectory = Path.GetDirectoryName(targetFilePath);
                    if (!string.IsNullOrEmpty(targetDirectory)) Directory.CreateDirectory(targetDirectory);

                    if (File.Exists(targetFilePath))
                    {
                        string backupPath = targetFilePath + ".bak";
                        if (File.Exists(backupPath)) File.Delete(backupPath);
                        File.Move(targetFilePath, backupPath);
                        backups.Add((targetFilePath, backupPath));
                    }

                    File.Copy(sourceFile, targetFilePath, true);
                    copiedTargets.Add(targetFilePath);

                    Console.WriteLine($"[{i + 1}/{sourceFiles.Length}] Updated {relativePath}");
                }

                foreach ((string _, string backup) in backups)
                {
                    if (File.Exists(backup)) File.Delete(backup);
                }

                Console.WriteLine("Finished updating Moving Objects Mod.");
                Thread.Sleep(2000); // Wait for 2 seconds before clearing the console
                CheckMovingObjectsUpdate();
            }
            catch (Exception ex)
            {
                for (int i = copiedTargets.Count - 1; i >= 0; i--)
                {
                    string target = copiedTargets[i];
                    try
                    {
                        if (File.Exists(target)) File.Delete(target);
                    }
                    catch
                    {
                        // Best effort rollback.
                    }
                }

                for (int i = backups.Count - 1; i >= 0; i--)
                {
                    (string target, string backup) = backups[i];
                    try
                    {
                        if (File.Exists(target)) File.Delete(target);
                        if (File.Exists(backup)) File.Move(backup, target);
                    }
                    catch
                    {
                        // Best effort rollback.
                    }
                }

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

        static SortedDictionary<MenuOption, string> menuOptions = new SortedDictionary<MenuOption, string>
        {
            { MenuOption.LaunchLiftoff, "Launch Liftoff" },
            { MenuOption.LaunchLiftoffWithMods, "Launch Liftoff with Mods" },
            { MenuOption.Exit, "Exit" },
            { MenuOption.UpdateMovingObjectsMod, "Moving Objects Update" },
        };


        static List<MenuOption> disabledOptions = new List<MenuOption>
        {
            MenuOption.UpdateMovingObjectsMod
        };

        static int currentSelection = 0;
        static ConsoleColor selectedColor = ConsoleColor.Red;
        static ConsoleColor disabledColor = ConsoleColor.DarkGray;

        private static void RenderMenu()
        {
            Console.Clear();
            Console.WriteLine("Liftoff Launcher!!!");
            Console.WriteLine($"Mods are currently {(ModsEnabled(liftoffDirectory) ? "ENABLED" : "disabled")}.");
            Console.WriteLine("Select an option:");
            Console.ForegroundColor = selectedColor;
            Console.WriteLine($"1. {menuOptions.First().Value}");
            Console.ResetColor();
            for (int i = 1; i < menuOptions.Count; i++)
            {
                if (disabledOptions.Contains(menuOptions.ElementAt(i).Key))
                {
                    Console.ForegroundColor = disabledColor;
                    Console.WriteLine($"{i + 1}. {menuOptions.ElementAt(i).Value}");
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine($"{i + 1}. {menuOptions.ElementAt(i).Value}");
                }
            }
            Console.SetCursorPosition(0, Console.WindowHeight - 3);
            Console.WriteLine("Use arrow keys to navigate and enter to select.");
            Console.WriteLine("Distributed under the GPLv3 license. See LICENSE.txt for details.");
        }

        private static void CheckMovingObjectsUpdate()
        {
            // Read the installed mod version now; the latest version is fetched in the
            // background so an offline/slow network never delays the menu.
            string? localDllPath = FindModDll(liftoffDirectory, MovingObjectsDllName);
            Version? localVersion = localDllPath != null ? ReadLocalVersion(localDllPath) : null;
            Version? latestVersion;

            // Read the file version from BepInEx/plugins/Liftoff.MovingObjects.dll and compare it to the newest version on GitHub
            // If the version is outdated, add an update option to the menu and display a warning message
            // Note: the version on JMTs website is often newer than the one on GitHub but no version number is provided there
            // Background: fetch the latest release and update disabledOptions
            _ = Task.Run(async () =>
            {
                (string Tag, string? ZipUrl)? release = await FetchLatestReleaseAsync(MovingObjectsReleasesApiUrl);

                string hint;
                if (release is null)
                {
                    hint = localVersion != null
                        ? $"Moving Objects: v{FormatVersion(localVersion)} (couldn't check for updates)"
                        : "Moving Objects: not installed (couldn't check for updates)";
                }
                else
                {
                    Version? latest = ParseVersion(release.Value.Tag);
                    if (localVersion is null)
                    {
                        hint = $"Moving Objects: not installed (latest v{FormatVersion(latest)})";
                    }
                    else if (latest is null || localVersion >= latest)
                    {
                        hint = $"Moving Objects: up to date (v{FormatVersion(localVersion)})";
                    }
                    else if (release.Value.ZipUrl != null)
                    {
                        _updateZipUrl = release.Value.ZipUrl;
                        _updateAvailable = true;
                        hint = $"Moving Objects: update (v{FormatVersion(localVersion)} -> v{FormatVersion(latest)})";
                        disabledOptions.Remove(MenuOption.UpdateMovingObjectsMod);
                    }
                    else
                    {
                        hint = $"Moving Objects: update (v{FormatVersion(localVersion)} -> v{FormatVersion(latest)}) - no release asset found";
                    }
                }

                menuOptions[MenuOption.UpdateMovingObjectsMod] = hint;
                RenderMenu();
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

            Http = new HttpClient();
            Http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("LiftoffModLauncher", "1.0"));
            Http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
            Http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            liftoffDirectory = Path.GetDirectoryName(args[0]) ?? throw new InvalidOperationException("Unable to determine Liftoff directory.");

            // Basic interface with three selectable options
            // Navigation via arrow keys and selection via enter key

            CheckMovingObjectsUpdate();

            RenderMenu();

            int selectionOffset = 3; // Offset for the selection indicator
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
                            do currentSelection--;
                            while (currentSelection > 0 && disabledOptions.Contains(menuOptions.ElementAt(currentSelection).Key));
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
                            do currentSelection++;
                            while (currentSelection < menuOptions.Count - 1 && disabledOptions.Contains(menuOptions.ElementAt(currentSelection).Key));
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
                                return 0;
                            case MenuOption.LaunchLiftoffWithMods:
                                EnableMods(liftoffDirectory);
                                StartGame(args[0]);
                                return 0;
                            case MenuOption.UpdateMovingObjectsMod:
                                UpdateMovingObjectsMod(liftoffDirectory, "geekhostuk/Liftoff.MovingObjects").GetAwaiter().GetResult();
                                currentSelection = 0;
                                Console.SetCursorPosition(0, selectionOffset + currentSelection);
                                break;
                            case MenuOption.Exit:
                            default:
                                return 0;
                        }
                        break;
                    default:
                        break;
                }
            }
        }
    }
}
