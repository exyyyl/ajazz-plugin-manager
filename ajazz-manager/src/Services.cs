using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Web.Script.Serialization;

namespace AjazzManager
{
    internal sealed class AppPaths
    {
        public readonly string AppBase = AppDomain.CurrentDomain.BaseDirectory;
        public readonly string AjazzRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HotSpot", "StreamDock");
        public readonly string ElgatoPlugins = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Elgato", "StreamDeck", "Plugins");
        public readonly string ManagerRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AjazzPluginManager");

        public string AjazzPlugins { get { return Path.Combine(AjazzRoot, "plugins"); } }
        public string AjazzProfiles { get { return Path.Combine(AjazzRoot, "profiles"); } }
        public string ElgatoIconPacks { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Elgato", "StreamDeck", "IconPacks"); } }
        public string BackupRoot { get { return Path.Combine(ManagerRoot, "Backups"); } }
        public string IconLibrary { get { return Path.Combine(ManagerRoot, "IconLibrary"); } }
        public string LogPath { get { return Path.Combine(ManagerRoot, "manager.log"); } }
        public string PackagedPlugins { get { return Path.Combine(AppBase, "packages"); } }

        public string AjazzExe
        {
            get
            {
                string x86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
                string x64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string[] candidates = new string[]
                {
                    String.IsNullOrEmpty(x86) ? "" : Path.Combine(x86, "HotSpot", "Stream Dock AJAZZ.exe"),
                    String.IsNullOrEmpty(x64) ? "" : Path.Combine(x64, "HotSpot", "Stream Dock AJAZZ.exe")
                };
                return candidates.FirstOrDefault(delegate(string path) { return !String.IsNullOrEmpty(path) && File.Exists(path); });
            }
        }

        public void EnsureManagerFolders()
        {
            Directory.CreateDirectory(ManagerRoot);
            Directory.CreateDirectory(BackupRoot);
            Directory.CreateDirectory(IconLibrary);
        }
    }

    internal sealed class ManagerLog
    {
        private readonly AppPaths paths;
        private readonly bool enabled;
        private readonly object sync = new object();

        public ManagerLog(AppPaths paths) : this(paths, true)
        {
        }

        public ManagerLog(AppPaths paths, bool enabled)
        {
            this.paths = paths;
            this.enabled = enabled;
            if (enabled) paths.EnsureManagerFolders();
        }

        public void Write(string message)
        {
            if (!enabled) return;
            lock (sync)
            {
                File.AppendAllText(paths.LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine, Encoding.UTF8);
            }
        }
    }

    internal sealed class ManifestReader
    {
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();

        public ManifestSummary Read(string pluginFolder)
        {
            string path = Path.Combine(pluginFolder, "manifest.json");
            if (!File.Exists(path)) throw new InvalidDataException("В пакете отсутствует manifest.json: " + pluginFolder);

            using (FileStream stream = File.OpenRead(path))
            {
                byte[] signature = new byte[6];
                if (stream.Read(signature, 0, signature.Length) == signature.Length
                    && Encoding.ASCII.GetString(signature) == "ELGATO") return ReadProtected(pluginFolder);
            }

            Dictionary<string, object> data = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            object value;
            ManifestSummary result = new ManifestSummary();
            result.Id = data.TryGetValue("UUID", out value) ? Convert.ToString(value) : "";
            result.Name = data.TryGetValue("Name", out value) ? Convert.ToString(value) : Path.GetFileName(pluginFolder);
            result.Version = data.TryGetValue("Version", out value) ? Convert.ToString(value) : "—";
            result.CodePath = data.TryGetValue("CodePath", out value) ? Convert.ToString(value) : "";
            result.HasIconEditor = data.ContainsKey("IconEditorPath");
            result.ActionCount = 0;
            if (data.TryGetValue("Actions", out value))
            {
                ArrayList list = value as ArrayList;
                object[] array = value as object[];
                if (list != null) result.ActionCount = list.Count;
                if (array != null) result.ActionCount = array.Length;
            }
            return result;
        }

        private ManifestSummary ReadProtected(string pluginFolder)
        {
            string folderName = new DirectoryInfo(pluginFolder).Name;
            string id = folderName.EndsWith(".sdPlugin", StringComparison.OrdinalIgnoreCase)
                ? folderName.Substring(0, folderName.Length - ".sdPlugin".Length) : folderName;
            string localizationPath = Path.Combine(pluginFolder, "en.json");
            Dictionary<string, object> localization = File.Exists(localizationPath)
                ? json.Deserialize<Dictionary<string, object>>(File.ReadAllText(localizationPath, Encoding.UTF8))
                : new Dictionary<string, object>();
            object value;

            string executable = Directory.GetFiles(pluginFolder, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
            string version = "защищён";
            if (!String.IsNullOrWhiteSpace(executable))
            {
                try
                {
                    FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(executable);
                    if (!String.IsNullOrWhiteSpace(versionInfo.FileVersion)) version = versionInfo.FileVersion;
                }
                catch { }
            }

            return new ManifestSummary
            {
                Id = id,
                Name = localization.TryGetValue("Name", out value) ? Convert.ToString(value) : id,
                Version = version,
                CodePath = String.IsNullOrWhiteSpace(executable) ? "" : Path.GetFileName(executable),
                ActionCount = localization.Keys.Count(delegate(string key) { return key.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase); }),
                IsProtected = true
            };
        }
    }

    internal sealed class PluginService
    {
        public const string TwitchId = "com.elgato.twitch.sdPlugin";
        private readonly AppPaths paths;
        private readonly ManagerLog log;
        private readonly ManifestReader manifests = new ManifestReader();

        public PluginService(AppPaths paths, ManagerLog log)
        {
            this.paths = paths;
            this.log = log;
        }

        public List<PluginEntry> Discover()
        {
            Dictionary<string, PluginEntry> entries = new Dictionary<string, PluginEntry>(StringComparer.OrdinalIgnoreCase);
            AddSources(entries, paths.PackagedPlugins, true, "Встроенный пакет");
            AddSources(entries, paths.ElgatoPlugins, false, "Elgato Stream Deck");

            if (Directory.Exists(paths.AjazzPlugins))
            {
                foreach (string folder in Directory.GetDirectories(paths.AjazzPlugins, "*.sdPlugin"))
                {
                    string id = Path.GetFileName(folder);
                    ManifestSummary manifest;
                    try { manifest = manifests.Read(folder); }
                    catch { continue; }

                    PluginEntry entry;
                    if (!entries.TryGetValue(id, out entry))
                    {
                        entry = CreateEntry(id, folder, manifest, false, "Установлен в AJAZZ");
                        entry.Compatibility = CompatibilityLevel.InstalledOnly;
                        entries.Add(id, entry);
                    }
                    entry.IsInstalled = true;
                    entry.InstalledVersion = manifest.Version;
                }
            }

            return entries.Values.OrderBy(delegate(PluginEntry p) { return p.Compatibility; })
                .ThenBy(delegate(PluginEntry p) { return p.Name; }, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public PluginEntry CreateExternalEntry(string folder)
        {
            string fullPath = Path.GetFullPath(folder);
            ManifestSummary manifest = manifests.Read(fullPath);
            string id = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!id.EndsWith(".sdPlugin", StringComparison.OrdinalIgnoreCase))
            {
                if (String.IsNullOrWhiteSpace(manifest.Id)) throw new InvalidDataException("Не удалось определить UUID плагина. Выберите папку с окончанием .sdPlugin.");
                id = manifest.Id.EndsWith(".sdPlugin", StringComparison.OrdinalIgnoreCase) ? manifest.Id : manifest.Id + ".sdPlugin";
            }
            PluginEntry entry = CreateEntry(id, fullPath, manifest, false, "Выбранная папка");
            string installed = Path.Combine(paths.AjazzPlugins, id);
            if (Directory.Exists(installed))
            {
                entry.IsInstalled = true;
                try { entry.InstalledVersion = manifests.Read(installed).Version; }
                catch { entry.InstalledVersion = "неизвестно"; }
            }
            return entry;
        }

        private void AddSources(Dictionary<string, PluginEntry> entries, string root, bool packaged, string label)
        {
            if (!Directory.Exists(root)) return;
            foreach (string folder in Directory.GetDirectories(root, "*.sdPlugin"))
            {
                string id = Path.GetFileName(folder);
                if (entries.ContainsKey(id)) continue;
                try
                {
                    ManifestSummary manifest = manifests.Read(folder);
                    entries.Add(id, CreateEntry(id, folder, manifest, packaged, label));
                }
                catch (Exception ex)
                {
                    log.Write("Пропущен некорректный пакет " + folder + ": " + ex.Message);
                }
            }
        }

        private PluginEntry CreateEntry(string id, string folder, ManifestSummary manifest, bool packaged, string label)
        {
            return new PluginEntry
            {
                Id = id,
                Name = String.IsNullOrWhiteSpace(manifest.Name) ? id : manifest.Name,
                Version = manifest.Version,
                SourcePath = folder,
                SourceLabel = label,
                CodePath = manifest.CodePath,
                ActionCount = manifest.ActionCount,
                IsIconEditor = manifest.HasIconEditor,
                IsProtected = manifest.IsProtected,
                IsPackaged = packaged,
                Compatibility = id.Equals(TwitchId, StringComparison.OrdinalIgnoreCase) && packaged
                    ? CompatibilityLevel.Supported
                    : manifest.IsProtected ? CompatibilityLevel.Protected
                    : manifest.ActionCount == 0 ? CompatibilityLevel.Unsupported : CompatibilityLevel.Experimental
            };
        }

        public string Install(PluginEntry entry)
        {
            if (entry == null || String.IsNullOrWhiteSpace(entry.SourcePath)) throw new InvalidOperationException("Не выбран источник плагина.");
            if (String.IsNullOrWhiteSpace(paths.AjazzExe)) throw new InvalidOperationException("Stream Dock AJAZZ не найден.");
            ManifestSummary sourceManifest = manifests.Read(entry.SourcePath);
            if (sourceManifest.ActionCount < 1)
            {
                throw new InvalidDataException(sourceManifest.HasIconEditor
                    ? "Это редактор иконок Elgato, а не плагин с действиями. Используйте вкладку «Иконки» менеджера."
                    : "В пакете не найдено ни одного действия Stream Deck.");
            }

            Directory.CreateDirectory(paths.AjazzPlugins);
            string target = SafePluginTarget(entry.Id);
            string backup = null;
            StopAjazz();
            try
            {
                if (Directory.Exists(target)) backup = MoveToBackup(target, entry.Id);
                CopyDirectory(entry.SourcePath, target);

                if (entry.Id.Equals(TwitchId, StringComparison.OrdinalIgnoreCase)) PrepareTwitchInstall(target, backup);
                else PreserveData(target, backup);

                ManifestSummary installed = manifests.Read(target);
                if (installed.ActionCount < 1) throw new InvalidDataException("Проверка установленного плагина не пройдена.");
                log.Write("Установлен " + entry.Id + " " + installed.Version);
                return backup;
            }
            catch
            {
                if (Directory.Exists(target)) Directory.Delete(target, true);
                if (!String.IsNullOrEmpty(backup) && Directory.Exists(backup)) Directory.Move(backup, target);
                throw;
            }
            finally
            {
                StartAjazz();
            }
        }

        public string Uninstall(string pluginId)
        {
            string target = SafePluginTarget(pluginId);
            if (!Directory.Exists(target)) throw new DirectoryNotFoundException("Плагин уже удалён.");
            StopAjazz();
            try
            {
                string backup = MoveToBackup(target, pluginId);
                log.Write("Удалён с резервной копией " + pluginId);
                return backup;
            }
            finally { StartAjazz(); }
        }

        public void Restore(BackupEntry backup)
        {
            if (backup == null || !Directory.Exists(backup.Path)) throw new DirectoryNotFoundException("Резервная копия не найдена.");
            string target = SafePluginTarget(backup.PluginId);
            string currentBackup = null;
            StopAjazz();
            try
            {
                if (Directory.Exists(target)) currentBackup = MoveToBackup(target, backup.PluginId);
                CopyDirectory(backup.Path, target);
                manifests.Read(target);
                log.Write("Восстановлена резервная копия " + backup.Path);
            }
            catch
            {
                if (Directory.Exists(target)) Directory.Delete(target, true);
                if (!String.IsNullOrEmpty(currentBackup) && Directory.Exists(currentBackup)) Directory.Move(currentBackup, target);
                throw;
            }
            finally { StartAjazz(); }
        }

        public List<BackupEntry> GetBackups()
        {
            List<BackupEntry> result = new List<BackupEntry>();
            string legacyRoot = Path.Combine(paths.AjazzRoot, "plugin-backups");
            string[] roots = new string[] { paths.BackupRoot, legacyRoot };
            foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(root)) continue;
                foreach (string folder in Directory.GetDirectories(root))
                {
                    try
                    {
                        ManifestSummary manifest = manifests.Read(folder);
                        string id = manifest.Id;
                        if (String.IsNullOrWhiteSpace(id))
                        {
                            id = new DirectoryInfo(folder).Name;
                            int marker = id.LastIndexOf("--", StringComparison.Ordinal);
                            if (marker > 0) id = id.Substring(0, marker);
                        }
                        if (!id.EndsWith(".sdPlugin", StringComparison.OrdinalIgnoreCase)) id += ".sdPlugin";
                        result.Add(new BackupEntry
                        {
                            PluginId = id,
                            PluginName = manifest.Name,
                            Version = manifest.Version,
                            Path = folder,
                            Created = Directory.GetLastWriteTime(folder)
                        });
                    }
                    catch { }
                }
            }
            return result.OrderByDescending(delegate(BackupEntry b) { return b.Created; }).ToList();
        }

        private string SafePluginTarget(string pluginId)
        {
            if (String.IsNullOrWhiteSpace(pluginId) || pluginId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || pluginId.Contains(".."))
                throw new InvalidDataException("Некорректный идентификатор плагина.");
            string root = Path.GetFullPath(paths.AjazzPlugins) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(Path.Combine(paths.AjazzPlugins, pluginId));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Небезопасный путь установки.");
            return target;
        }

        private string MoveToBackup(string target, string pluginId)
        {
            Directory.CreateDirectory(paths.BackupRoot);
            string backup = Path.Combine(paths.BackupRoot, pluginId + "--" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.Move(target, backup);
            return backup;
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            foreach (string folder in Directory.GetDirectories(source))
                CopyDirectory(folder, Path.Combine(destination, Path.GetFileName(folder)));
        }

        private static void PreserveData(string target, string backup)
        {
            if (String.IsNullOrEmpty(backup)) return;
            string oldData = Path.Combine(backup, "data");
            string newData = Path.Combine(target, "data");
            if (Directory.Exists(oldData) && !Directory.Exists(newData)) CopyDirectory(oldData, newData);
        }

        private static string ReadClientId(string pluginFolder)
        {
            if (String.IsNullOrEmpty(pluginFolder)) return "";
            string config = Path.Combine(pluginFolder, "auth-config.json");
            if (!File.Exists(config)) return "";
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> data = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(config));
                object value;
                return data.TryGetValue("clientId", out value) ? Convert.ToString(value) : "";
            }
            catch { return ""; }
        }

        private static void PrepareTwitchInstall(string target, string backup)
        {
            string copiedData = Path.Combine(target, "data");
            if (Directory.Exists(copiedData)) Directory.Delete(copiedData, true);
            string copiedLogs = Path.Combine(target, "logs");
            if (Directory.Exists(copiedLogs)) Directory.Delete(copiedLogs, true);

            bool preserve = false;
            if (!String.IsNullOrEmpty(backup))
            {
                string credential = Path.Combine(backup, "data", "auth.bin");
                preserve = File.Exists(credential) && ReadClientId(backup) == ReadClientId(target);
                if (preserve)
                {
                    Directory.CreateDirectory(copiedData);
                    File.Copy(credential, Path.Combine(copiedData, "auth.bin"), true);
                }
            }
            string marker = Path.Combine(target, "first-run-auth");
            if (preserve && File.Exists(marker)) File.Delete(marker);
            if (!preserve) File.WriteAllText(marker, "");
        }

        public void StopAjazz()
        {
            KillByName("Stream Dock AJAZZ");
            KillByName("TwitchLauncher");
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='node.exe'"))
                {
                    foreach (ManagementObject process in searcher.Get())
                    {
                        string command = Convert.ToString(process["CommandLine"]);
                        if (command.IndexOf("com.elgato.twitch.sdPlugin", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Process.GetProcessById(Convert.ToInt32(process["ProcessId"])).Kill();
                        }
                    }
                }
            }
            catch { }
            System.Threading.Thread.Sleep(700);
        }

        private static void KillByName(string name)
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                try { process.Kill(); process.WaitForExit(3000); }
                catch { }
            }
        }

        public void StartAjazz()
        {
            if (!String.IsNullOrWhiteSpace(paths.AjazzExe) && File.Exists(paths.AjazzExe))
            {
                ProcessStartInfo info = new ProcessStartInfo(paths.AjazzExe);
                info.WorkingDirectory = Path.GetDirectoryName(paths.AjazzExe);
                Process.Start(info);
            }
        }
    }

    internal sealed class IconService
    {
        private static readonly string[] Extensions = new string[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".svg", ".webp" };
        private readonly AppPaths paths;
        private readonly ManagerLog log;

        public IconService(AppPaths paths, ManagerLog log)
        {
            this.paths = paths;
            this.log = log;
        }

        public int ImportFiles(IEnumerable<string> files, string packName)
        {
            string safePack = MakeSafeName(String.IsNullOrWhiteSpace(packName) ? "Импорт " + DateTime.Now.ToString("yyyy-MM-dd HH-mm") : packName);
            string destination = Path.Combine(paths.IconLibrary, safePack);
            Directory.CreateDirectory(destination);
            int copied = 0;
            foreach (string file in files)
            {
                if (!File.Exists(file) || !Extensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                string target = UniquePath(destination, Path.GetFileName(file));
                File.Copy(file, target);
                copied++;
            }
            log.Write("Импортировано иконок: " + copied + " в " + destination);
            return copied;
        }

        public int ImportFolder(string folder)
        {
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
            return ImportFiles(Directory.GetFiles(folder, "*", SearchOption.AllDirectories), new DirectoryInfo(folder).Name);
        }

        public List<IconEntry> GetIcons()
        {
            List<IconEntry> result = new List<IconEntry>();
            AddLibraryIcons(result);
            AddElgatoIconPacks(result);
            AddAjazzProfileIcons(result);
            return result.OrderBy(delegate(IconEntry icon) { return icon.Source; }, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(delegate(IconEntry icon) { return icon.PackName; }, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(delegate(IconEntry icon) { return icon.Name; }, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private void AddLibraryIcons(List<IconEntry> result)
        {
            if (!Directory.Exists(paths.IconLibrary)) return;
            foreach (string file in EnumerateImages(paths.IconLibrary))
            {
                string relative = file.Substring(paths.IconLibrary.Length).TrimStart(Path.DirectorySeparatorChar);
                string pack = relative.Contains(Path.DirectorySeparatorChar.ToString())
                    ? relative.Split(Path.DirectorySeparatorChar)[0] : "Без набора";
                AddIcon(result, file, pack, "Моя библиотека");
            }
        }

        private void AddElgatoIconPacks(List<IconEntry> result)
        {
            if (!Directory.Exists(paths.ElgatoIconPacks)) return;
            foreach (string packFolder in Directory.GetDirectories(paths.ElgatoIconPacks, "*.sdIconPack"))
            {
                string packName = ReadPackName(packFolder);
                string iconsFolder = Path.Combine(packFolder, "icons");
                if (!Directory.Exists(iconsFolder)) continue;
                foreach (string file in EnumerateImages(iconsFolder)) AddIcon(result, file, packName, "Elgato Icon Pack");
            }
        }

        private void AddAjazzProfileIcons(List<IconEntry> result)
        {
            if (!Directory.Exists(paths.AjazzProfiles)) return;
            foreach (string file in EnumerateImages(paths.AjazzProfiles))
            {
                DirectoryInfo directory = new FileInfo(file).Directory;
                while (directory != null && !directory.Name.EndsWith(".sdProfile", StringComparison.OrdinalIgnoreCase)) directory = directory.Parent;
                string profile = directory == null ? "Профиль AJAZZ" : directory.Name.Substring(0, directory.Name.Length - ".sdProfile".Length);
                AddIcon(result, file, profile, "Профили AJAZZ");
            }
        }

        private static IEnumerable<string> EnumerateImages(string root)
        {
            return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Where(delegate(string file)
                {
                    return file.IndexOf(Path.DirectorySeparatorChar + ".history" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0
                        && Extensions.Contains(Path.GetExtension(file).ToLowerInvariant());
                });
        }

        private static void AddIcon(List<IconEntry> result, string file, string packName, string source)
        {
            FileInfo info = new FileInfo(file);
            result.Add(new IconEntry
            {
                Path = file,
                Name = Path.GetFileNameWithoutExtension(file),
                PackName = packName,
                Source = source,
                Extension = info.Extension.TrimStart('.').ToUpperInvariant(),
                Size = info.Length
            });
        }

        private static string ReadPackName(string packFolder)
        {
            string fallback = new DirectoryInfo(packFolder).Name.Replace(".sdIconPack", "");
            string manifest = Path.Combine(packFolder, "manifest.json");
            if (!File.Exists(manifest)) return fallback;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> data = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(manifest));
                object value;
                return data.TryGetValue("Name", out value) && !String.IsNullOrWhiteSpace(Convert.ToString(value)) ? Convert.ToString(value) : fallback;
            }
            catch { return fallback; }
        }

        private static string MakeSafeName(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return value.Trim();
        }

        private static string UniquePath(string folder, string fileName)
        {
            string candidate = Path.Combine(folder, fileName);
            string name = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int index = 2;
            while (File.Exists(candidate)) candidate = Path.Combine(folder, name + " (" + index++ + ")" + ext);
            return candidate;
        }
    }
}
