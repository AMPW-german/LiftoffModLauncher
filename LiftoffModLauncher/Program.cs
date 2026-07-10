using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LiftoffModLauncher
{
    internal class Program
    {
        // Files/directories that make up the mod loader. BepInEx is a directory,
        // the other two are files, but Enable/Disable treat them uniformly.
        private static readonly string[] ModItems = { "BepInEx", "doorstop_config.ini", "winhttp.dll" };

        // The Moving Objects mod: the plugin DLL (whose version we read) plus the
        // GitHub repo we check for the latest release.
        private const string ModDllName = "Liftoff.MovingObjects.dll";
        private const string ReleasesApiUrl =
            "https://api.github.com/repos/geekhostuk/Liftoff.MovingObjects/releases/latest";

        // Shared HttpClient. GitHub's API rejects requests without a User-Agent.
        private static readonly HttpClient Http = new HttpClient();

        // Serialises all console writes: the background update check rewrites the
        // hint line while the input loop redraws the menu on another thread.
        private static readonly object ConsoleLock = new object();

        // Set by the background check, read by the input loop when the user presses U.
        private static volatile bool _updateAvailable;
        private static volatile string? _updateZipUrl;

        static Program()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("LiftoffModLauncher");
            Http.Timeout = TimeSpan.FromSeconds(15);
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

        // ---- Mod version check --------------------------------------------------

        // The BepInEx tree is renamed to "_BepInEx" while mods are disabled, so look
        // in both. Returns the path to the installed plugin DLL, or null if absent.
        private static string? FindModDll(string liftoffDirectory)
        {
            foreach (string modRoot in new[] { "BepInEx", "_BepInEx" })
            {
                string candidate = Path.Combine(liftoffDirectory, modRoot, "plugins", ModDllName);
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

        private static string FormatVersion(Version? v) =>
            v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";

        // Fetch the latest GitHub release. Returns the tag plus the download URL of the
        // .zip asset (null if the release has no zip). Returns null on any failure so
        // the caller never has to handle exceptions or blocks.
        private static async Task<(string Tag, string? ZipUrl)?> FetchLatestReleaseAsync()
        {
            try
            {
                using HttpResponseMessage response = await Http.GetAsync(ReleasesApiUrl);
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
                    foreach (JsonElement asset in assets.EnumerateArray())
                    {
                        string? name = asset.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
                        if (name != null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            zipUrl = asset.TryGetProperty("browser_download_url", out JsonElement u)
                                ? u.GetString()
                                : null;
                            break;
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

        // Download the release zip, extract it, and copy every file from the archive's
        // BepInEx/ tree over the install's active mod folder. Each overwritten file is
        // backed up to "<file>.bak" first and restored if anything fails.
        private static async Task InstallUpdateAsync(string liftoffDirectory, string zipUrl)
        {
            string modRootName = Directory.Exists(Path.Combine(liftoffDirectory, "BepInEx"))
                ? "BepInEx"
                : "_BepInEx";
            string modRoot = Path.Combine(liftoffDirectory, modRootName);

            string tempDir = Path.Combine(Path.GetTempPath(), "LiftoffModUpdate_" + Guid.NewGuid().ToString("N"));
            string zipPath = Path.Combine(tempDir, "update.zip");
            string extractDir = Path.Combine(tempDir, "extract");
            var backups = new List<(string Target, string Backup)>();

            try
            {
                Directory.CreateDirectory(tempDir);

                byte[] data = await Http.GetByteArrayAsync(zipUrl);
                await File.WriteAllBytesAsync(zipPath, data);

                Directory.CreateDirectory(extractDir);
                ZipFile.ExtractToDirectory(zipPath, extractDir);

                // The release zip mirrors the install layout with a top-level BepInEx folder.
                string sourceBep = Path.Combine(extractDir, "BepInEx");
                if (!Directory.Exists(sourceBep))
                    throw new InvalidDataException("Downloaded archive did not contain a BepInEx folder.");

                foreach (string sourceFile in Directory.GetFiles(sourceBep, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(sourceBep, sourceFile);
                    string target = Path.Combine(modRoot, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                    if (File.Exists(target))
                    {
                        string backup = target + ".bak";
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Move(target, backup);
                        backups.Add((target, backup));
                    }

                    File.Copy(sourceFile, target, overwrite: true);
                }

                // Success: discard the backups.
                foreach ((string _, string backup) in backups)
                {
                    if (File.Exists(backup)) File.Delete(backup);
                }
            }
            catch
            {
                // Roll back any files we replaced before the failure.
                foreach ((string target, string backup) in backups)
                {
                    try
                    {
                        if (File.Exists(target)) File.Delete(target);
                        if (File.Exists(backup)) File.Move(backup, target);
                    }
                    catch
                    {
                        // Best effort; keep restoring the rest.
                    }
                }
                throw;
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
                catch { /* temp cleanup is best effort */ }
            }
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

            // Read the installed mod version now; the latest version is fetched in the
            // background so an offline/slow network never delays the menu.
            string? localDllPath = FindModDll(liftoffDirectory);
            Version? localVersion = localDllPath != null ? ReadLocalVersion(localDllPath) : null;

            // Basic interface with three selectable options.
            // Navigation via arrow keys, number keys, or enter.
            ConsoleColor selectedColor = ConsoleColor.Red;
            string[] options = { "Launch Liftoff", "Launch Liftoff with Mods", "Exit" };
            const int modVersionRow = 2;  // Row of the Moving Objects hint line.
            int selectionOffset = 4;      // First option row (shifted down by the hint line).

            Console.Clear();
            Console.WriteLine("Liftoff Launcher!!!");
            Console.WriteLine($"Mods are currently {(ModsEnabled(liftoffDirectory) ? "ENABLED" : "disabled")}.");
            Console.WriteLine("Moving Objects: checking for updates...");
            Console.WriteLine("Select an option:");
            for (int i = 0; i < options.Length; i++)
            {
                Console.WriteLine($"{i + 1}. {options[i]}");
            }
            Console.WriteLine();
            Console.WriteLine("Use arrow keys or number keys to navigate and Enter to select.");
            Console.WriteLine("Press U to install a mod update when one is available.");
            Console.WriteLine("Made by AMPW. Distributed under the GPLv3 license. See LICENSE.txt for details.");

            int currentSelection = 0;

            // Rewrite the Moving Objects hint line without disturbing the menu below it.
            void WriteHintLine(string text)
            {
                lock (ConsoleLock)
                {
                    int prevLeft = Console.CursorLeft;
                    int prevTop = Console.CursorTop;
                    Console.SetCursorPosition(0, modVersionRow);
                    Console.Write(new string(' ', Math.Max(0, Console.WindowWidth - 1)));
                    Console.SetCursorPosition(0, modVersionRow);
                    if (_updateAvailable) Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write(text);
                    Console.ResetColor();
                    Console.SetCursorPosition(prevLeft, prevTop);
                }
            }

            void Render()
            {
                lock (ConsoleLock)
                {
                    for (int i = 0; i < options.Length; i++)
                    {
                        Console.SetCursorPosition(0, selectionOffset + i);
                        if (i == currentSelection) Console.ForegroundColor = selectedColor;
                        Console.Write($"{i + 1}. {options[i]}");
                        Console.ResetColor();
                    }
                }
            }

            Render();

            // Background: fetch the latest release and update the hint line when done.
            _ = Task.Run(async () =>
            {
                (string Tag, string? ZipUrl)? release = await FetchLatestReleaseAsync();

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
                        hint = $"Moving Objects: UPDATE available v{FormatVersion(latest)} " +
                               $"(installed v{FormatVersion(localVersion)}) - press U to install";
                    }
                    else
                    {
                        hint = $"Moving Objects: update v{FormatVersion(latest)} available " +
                               $"(installed v{FormatVersion(localVersion)}) - no download asset";
                    }
                }

                WriteHintLine(hint);
            });

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
                    case ConsoleKey.U:
                        if (_updateAvailable && _updateZipUrl != null)
                        {
                            int statusRow = selectionOffset + options.Length + 1;
                            lock (ConsoleLock)
                            {
                                Console.SetCursorPosition(0, statusRow);
                                Console.Write(new string(' ', Math.Max(0, Console.WindowWidth - 1)));
                                Console.SetCursorPosition(0, statusRow);
                                Console.Write("Downloading and installing update...");
                            }

                            try
                            {
                                InstallUpdateAsync(liftoffDirectory, _updateZipUrl).GetAwaiter().GetResult();

                                _updateAvailable = false;
                                _updateZipUrl = null;

                                string? newDll = FindModDll(liftoffDirectory);
                                Version? newVer = newDll != null ? ReadLocalVersion(newDll) : localVersion;
                                WriteHintLine($"Moving Objects: up to date (v{FormatVersion(newVer)})");

                                lock (ConsoleLock)
                                {
                                    Console.SetCursorPosition(0, statusRow);
                                    Console.Write(new string(' ', Math.Max(0, Console.WindowWidth - 1)));
                                    Console.SetCursorPosition(0, statusRow);
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.Write("Update installed successfully.");
                                    Console.ResetColor();
                                }
                            }
                            catch (Exception ex)
                            {
                                lock (ConsoleLock)
                                {
                                    Console.SetCursorPosition(0, statusRow);
                                    Console.Write(new string(' ', Math.Max(0, Console.WindowWidth - 1)));
                                    Console.SetCursorPosition(0, statusRow);
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.Write($"Update failed: {ex.Message}");
                                    Console.ResetColor();
                                }
                            }
                        }
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
