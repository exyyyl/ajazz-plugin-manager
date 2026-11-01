using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
public static class Credentials {
    static string FilePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "connection.bin"); } }
    public static string Read() {
        if (!File.Exists(FilePath)) return "";
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser));
    }
    public static void Save(string value) {
        string token = Commands.Token(value);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        string temporary = FilePath + ".tmp";
        File.WriteAllBytes(temporary, ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
        if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null); else File.Move(temporary, FilePath);
    }
}
