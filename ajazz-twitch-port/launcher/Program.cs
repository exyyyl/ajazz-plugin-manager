using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

internal static class Program
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AjazzTwitchPlugin.DeviceOAuth.v1");
    private static string PluginDirectory { get { return AppDomain.CurrentDomain.BaseDirectory; } }
    private static string CredentialPath { get { return Path.Combine(PluginDirectory, "data", "auth.bin"); } }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--credential-read")
            {
                return ReadCredential();
            }

            if (args.Length == 1 && args[0] == "--credential-write")
            {
                return WriteCredential();
            }

            if (args.Length == 1 && args[0] == "--credential-delete")
            {
                return DeleteCredential();
            }

            return LaunchNode(args);
        }
        catch (Exception exception)
        {
            WriteError(exception);
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int ReadCredential()
    {
        if (!File.Exists(CredentialPath))
        {
            return 2;
        }

        byte[] encrypted = File.ReadAllBytes(CredentialPath);
        byte[] clear = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        Console.OutputEncoding = Encoding.UTF8;
        Console.Write(Encoding.UTF8.GetString(clear));
        Array.Clear(clear, 0, clear.Length);
        return 0;
    }

    private static int WriteCredential()
    {
        string json = Console.In.ReadToEnd();
        if (String.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("Credential input is empty.");
        }

        byte[] clear = Encoding.UTF8.GetBytes(json);
        byte[] encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
        Array.Clear(clear, 0, clear.Length);

        string dataDirectory = Path.GetDirectoryName(CredentialPath);
        Directory.CreateDirectory(dataDirectory);
        string temporaryPath = CredentialPath + ".tmp";
        File.WriteAllBytes(temporaryPath, encrypted);

        if (File.Exists(CredentialPath))
        {
            string backupPath = CredentialPath + ".bak";
            File.Replace(temporaryPath, CredentialPath, backupPath, true);
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }
        else
        {
            File.Move(temporaryPath, CredentialPath);
        }

        return 0;
    }

    private static int DeleteCredential()
    {
        if (File.Exists(CredentialPath))
        {
            File.Delete(CredentialPath);
        }

        return 0;
    }

    private static int LaunchNode(string[] args)
    {
        string nodePath = ResolveNodePath();
        string scriptPath = Path.Combine(PluginDirectory, "bin", "plugin.js");

        if (!File.Exists(nodePath))
        {
            throw new FileNotFoundException("Bundled Node.js runtime was not found.", nodePath);
        }

        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Twitch plugin entry point was not found.", scriptPath);
        }

        var commandArguments = new List<string>();
        commandArguments.Add(scriptPath);
        commandArguments.AddRange(args);

        var startInfo = new ProcessStartInfo
        {
            FileName = nodePath,
            Arguments = JoinArguments(commandArguments),
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = PluginDirectory
        };

        using (Process child = Process.Start(startInfo))
        {
            if (child == null)
            {
                throw new InvalidOperationException("Node.js process could not be started.");
            }

            child.WaitForExit();
            return child.ExitCode;
        }
    }

    private static string ResolveNodePath()
    {
        string bundled = Path.Combine(PluginDirectory, "runtime", "node.exe");
        if (File.Exists(bundled))
        {
            return bundled;
        }

        string configured = Environment.GetEnvironmentVariable("AJAZZ_TWITCH_NODE");
        if (!String.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "nodejs",
            "node.exe");
    }

    private static string JoinArguments(IEnumerable<string> values)
    {
        var output = new StringBuilder();
        foreach (string value in values)
        {
            if (output.Length > 0)
            {
                output.Append(' ');
            }

            output.Append(QuoteArgument(value));
        }

        return output.ToString();
    }

    private static string QuoteArgument(string value)
    {
        if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
        {
            return value;
        }

        var output = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                output.Append('\\', backslashes * 2 + 1);
                output.Append('"');
                backslashes = 0;
                continue;
            }

            output.Append('\\', backslashes);
            backslashes = 0;
            output.Append(character);
        }

        output.Append('\\', backslashes * 2);
        output.Append('"');
        return output.ToString();
    }

    private static void WriteError(Exception exception)
    {
        try
        {
            string message = DateTimeOffset.Now.ToString("O") + " " + exception + Environment.NewLine;
            File.AppendAllText(Path.Combine(PluginDirectory, "launcher-error.log"), message);
        }
        catch
        {
        }
    }
}
