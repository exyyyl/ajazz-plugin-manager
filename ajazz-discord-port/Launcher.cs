using System;
using System.Diagnostics;
using System.IO;
using System.Text;
class Launcher {
    static string Quote(string value) {
        var result = new StringBuilder("\""); int slashes = 0;
        foreach (char c in value) {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { result.Append('\\', slashes * 2 + 1); result.Append(c); }
            else { result.Append('\\', slashes); result.Append(c); }
            slashes = 0;
        }
        result.Append('\\', slashes * 2); return result.Append('"').ToString();
    }
    static int Main(string[] args) {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        var command = new StringBuilder(Quote(Path.Combine(root, "bridge.cjs")));
        foreach (string arg in args) command.Append(" ").Append(Quote(arg));
        using (var child = Process.Start(new ProcessStartInfo {
            FileName = Path.Combine(root, "node.exe"), Arguments = command.ToString(),
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true
        })) { child.WaitForExit(); return child.ExitCode; }
    }
}
