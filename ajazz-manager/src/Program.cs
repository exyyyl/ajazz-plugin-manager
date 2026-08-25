using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace AjazzManager
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length >= 2 && args[0].Equals("--self-test", StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode = RunSelfTest(args[1]);
                return;
            }
            if (args.Length >= 2 && args[0].Equals("--integration-test-twitch", StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode = RunTwitchIntegrationTest(args[1]);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                try
                {
                    string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AjazzPluginManager");
                    Directory.CreateDirectory(root);
                    File.AppendAllText(Path.Combine(root, "manager.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  UI CRASH: " + ex + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
                MessageBox.Show(ex.Message, "Ajazz Plugin Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static int RunSelfTest(string reportPath)
        {
            try
            {
                AppPaths paths = new AppPaths();
                ManagerLog log = new ManagerLog(paths, false);
                PluginService plugins = new PluginService(paths, log);
                IconService icons = new IconService(paths, log);
                System.Collections.Generic.List<PluginEntry> entries = plugins.Discover();
                bool twitch = entries.Exists(delegate(PluginEntry item)
                {
                    return item.Id.Equals(PluginService.TwitchId, StringComparison.OrdinalIgnoreCase) && item.IsPackaged;
                });

                StringBuilder result = new StringBuilder();
                result.AppendLine("Ajazz Plugin Manager self-test");
                result.AppendLine("AjazzExe=" + (paths.AjazzExe ?? ""));
                result.AppendLine("AjazzRoot=" + paths.AjazzRoot);
                result.AppendLine("PluginsDiscovered=" + entries.Count);
                result.AppendLine("BackupsDiscovered=" + plugins.GetBackups().Count);
                result.AppendLine("IconsDiscovered=" + icons.GetIcons().Count);
                result.AppendLine("PackagedTwitch=" + twitch);
                File.WriteAllText(reportPath, result.ToString(), Encoding.UTF8);
                return !String.IsNullOrWhiteSpace(paths.AjazzExe) && twitch ? 0 : 2;
            }
            catch (Exception ex)
            {
                File.WriteAllText(reportPath, ex.ToString(), Encoding.UTF8);
                return 1;
            }
        }

        private static int RunTwitchIntegrationTest(string reportPath)
        {
            try
            {
                AppPaths paths = new AppPaths();
                paths.EnsureManagerFolders();
                ManagerLog log = new ManagerLog(paths);
                PluginService service = new PluginService(paths, log);
                PluginEntry twitch = service.Discover().FirstOrDefault(delegate(PluginEntry item)
                {
                    return item.Id.Equals(PluginService.TwitchId, StringComparison.OrdinalIgnoreCase) && item.IsPackaged;
                });
                if (twitch == null) throw new InvalidOperationException("Встроенный Twitch-пакет не найден.");

                string authPath = Path.Combine(paths.AjazzPlugins, PluginService.TwitchId, "data", "auth.bin");
                byte[] authBefore = File.Exists(authPath) ? HashFile(authPath) : null;
                string backup = service.Install(twitch);
                byte[] authAfter = File.Exists(authPath) ? HashFile(authPath) : null;
                bool authPreserved = authBefore != null && authAfter != null && authBefore.SequenceEqual(authAfter);

                StringBuilder result = new StringBuilder();
                result.AppendLine("Twitch integration test");
                result.AppendLine("Installed=" + Directory.Exists(Path.Combine(paths.AjazzPlugins, PluginService.TwitchId)));
                result.AppendLine("BackupCreated=" + (!String.IsNullOrWhiteSpace(backup) && Directory.Exists(backup)));
                result.AppendLine("AuthExistedBefore=" + (authBefore != null));
                result.AppendLine("AuthPreserved=" + authPreserved);
                result.AppendLine("AjazzStarted=" + (System.Diagnostics.Process.GetProcessesByName("Stream Dock AJAZZ").Length > 0));
                File.WriteAllText(reportPath, result.ToString(), Encoding.UTF8);
                return authBefore == null || authPreserved ? 0 : 3;
            }
            catch (Exception ex)
            {
                File.WriteAllText(reportPath, ex.ToString(), Encoding.UTF8);
                return 1;
            }
        }

        private static byte[] HashFile(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path)) return hash.ComputeHash(stream);
        }
    }
}
