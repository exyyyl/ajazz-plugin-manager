using System;
using System.Runtime.InteropServices;
using System.Text;

public static class ObsLaunch {
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CommandLineToArgvW(string commandLine,out int count);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    public static string Encode(string arguments) {return Convert.ToBase64String(Encoding.UTF8.GetBytes(arguments));}
    public static string Decode(string encoded) {
        if(encoded.Length>22000)throw new ArgumentException("Слишком длинные параметры OBS.");
        return new UTF8Encoding(false,true).GetString(Convert.FromBase64String(encoded));
    }
    static string Quote(string argument) {
        var result=new StringBuilder("\"");int slashes=0;
        foreach(char c in argument) {
            if(c=='\\'){slashes++;continue;}
            if(c=='\"')result.Append('\\',slashes*2+1);else result.Append('\\',slashes);
            result.Append(c);slashes=0;
        }
        result.Append('\\',slashes*2);return result.Append('"').ToString();
    }
    public static string Arguments(string startup) {
        if(startup.Length>16384 || startup.IndexOf('\0')>=0)throw new ArgumentException("Некорректные параметры OBS.");
        int count;IntPtr argv=CommandLineToArgvW("obs64.exe "+startup,out count);
        if(argv==IntPtr.Zero)throw new InvalidOperationException("Не удалось прочитать параметры OBS.");
        var result=new StringBuilder();
        try {for(int i=1;i<count;i++) {
            string argument=Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv,i*IntPtr.Size));
            if(argument.TrimStart('-','/').StartsWith("remote-debugging",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Параметры канала управления OBS задаются только плагином.");
            result.Append(Quote(argument)).Append(' ');
        }}finally {LocalFree(argv);}
        return result.Append("--remote-debugging-port="+ObsGuard.Port+" --remote-debugging-address=127.0.0.1").ToString();
    }
}
