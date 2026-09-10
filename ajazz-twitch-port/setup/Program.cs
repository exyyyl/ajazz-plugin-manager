using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;

internal static class Program
{
    private const string PayloadResourceName = "AjazzTwitchInstaller.Payload.zip";

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string extractionRoot = Path.Combine(
            Path.GetTempPath(),
            "Ajazz-Twitch-Setup-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(extractionRoot);
            ExtractPayload(extractionRoot);
            ValidatePayload(extractionRoot);

            if (args.Length == 1 && String.Equals(args[0], "--verify", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Встроенный установочный пакет проверен.");
                return 0;
            }

            Console.WriteLine("Запускаю установку Twitch-плагина для AJAZZ…");
            string installerPath = Path.Combine(extractionRoot, "Install-Ajazz-Twitch.ps1");
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + QuoteArgument(installerPath),
                WorkingDirectory = extractionRoot,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            using (Process installer = Process.Start(startInfo))
            {
                if (installer == null)
                {
                    throw new InvalidOperationException("Не удалось запустить PowerShell-установщик.");
                }
                installer.WaitForExit();
                if (installer.ExitCode != 0)
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Установка завершилась с ошибкой. Нажмите Enter, чтобы закрыть окно.");
                    Console.ReadLine();
                }
                return installer.ExitCode;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Ошибка установки: " + exception.Message);
            if (!(args.Length == 1 && String.Equals(args[0], "--verify", StringComparison.OrdinalIgnoreCase)))
            {
                Console.Error.WriteLine("Нажмите Enter, чтобы закрыть окно.");
                Console.ReadLine();
            }
            return 1;
        }
        finally
        {
            DeleteTemporaryDirectory(extractionRoot);
        }
    }

    private static void ExtractPayload(string destination)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        using (Stream payload = assembly.GetManifestResourceStream(PayloadResourceName))
        {
            if (payload == null)
            {
                throw new InvalidDataException("В установщике отсутствует встроенный пакет.");
            }

            string archivePath = Path.Combine(destination, "payload.zip");
            using (FileStream archive = File.Create(archivePath))
            {
                payload.CopyTo(archive);
            }
            ZipFile.ExtractToDirectory(archivePath, destination);
            File.Delete(archivePath);
        }
    }

    private static void ValidatePayload(string root)
    {
        string[] requiredFiles =
        {
            "Install-Ajazz-Twitch.ps1",
            Path.Combine("plugin", "manifest.json"),
            Path.Combine("plugin", "auth-config.json"),
            Path.Combine("plugin", "TwitchLauncher.exe"),
            Path.Combine("plugin", "runtime", "node.exe")
        };

        foreach (string relativePath in requiredFiles)
        {
            string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
            string safeRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                throw new InvalidDataException("Встроенный пакет повреждён: отсутствует " + relativePath);
            }
        }
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Directory.Delete(path, true);
                return;
            }
            catch
            {
                if (attempt == 2)
                {
                    return;
                }
                Thread.Sleep(250);
            }
        }
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
