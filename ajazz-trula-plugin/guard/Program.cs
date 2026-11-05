using System;
using System.Security.Principal;
using System.Windows.Forms;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

// Invoked once by the installer through UAC. Does not launch OBS or execute
// arbitrary commands, and derives the permitted executable from its own folder.
static class ObsGuardSetup {
    [STAThread] static int Main(string[] args) {
        try {
            ObsGuard.CheckAbi();
            if(args.Length==1 && args[0]=="--check") {ObsGuard.Require();return 0;}
            if(args.Length!=2 || args[0]!="--install")throw new ArgumentException("Usage: --install <Windows SID>");
            if(System.Diagnostics.Process.GetProcessesByName("obs64").Length!=0)throw new InvalidOperationException("Закройте OBS перед установкой защиты Trula и запустите установщик ещё раз. Это завершит старые незащищённые соединения.");
            ObsGuard.Install(args[1]);RemoveSharedLegacyArguments();return 0;
        } catch(Exception error) {
            if(args.Length==1 && args[0]=="--check")return 1;
            MessageBox.Show(error.Message,"Защита OBS — Trula",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;
        }
    }
    static object Property(object instance,string name,params object[] value) {
        return instance.GetType().InvokeMember(name,value.Length==0?BindingFlags.GetProperty:BindingFlags.SetProperty,null,instance,value);
    }
    static void RemoveSharedLegacyArguments() {
        // Never point a shared shortcut at a particular user's AppData executable.
        string root=Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        string obs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"obs-studio","bin","64bit","obs64.exe");
        object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        try {
            foreach(string path in Directory.GetFiles(root,"*.lnk",SearchOption.AllDirectories)) {
                object link=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});
                try {
                    if(!String.Equals(Convert.ToString(Property(link,"TargetPath")),obs,StringComparison.OrdinalIgnoreCase))continue;
                    string args=Convert.ToString(Property(link,"Arguments"));
                    if(!Regex.IsMatch(args,@"(?i)(?<!\S)--remote-debugging-port(?:=|\s+)""?17936(?:""|\s|$)"))continue;
                    string cleaned=Regex.Replace(args,@"(?i)(?<!\S)--remote-debugging-(?:port|address)(?:=|\s+)(?:""[^""]*""|\S+)","").Trim();
                    Property(link,"Arguments",cleaned);link.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,link,new object[0]);
                } finally {System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);}
            }
        } finally {System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
    }
}
