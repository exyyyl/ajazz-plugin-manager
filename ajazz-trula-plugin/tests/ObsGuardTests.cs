using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Security.AccessControl;
using System.Security.Principal;

// Compile with OBS_GUARD_TEST: isolated policy GUIDs and TCP 27936. Run elevated
// to install/remove WFP objects. No real CDP traffic or credentials are used.
class ObsGuardTests {
    static bool Connect(string address,int port) {
        try { using(var socket=new TcpClient(IPAddress.Parse(address).AddressFamily)) {
            var pending=socket.ConnectAsync(address,port);return pending.Wait(1500) && socket.Connected;
        }}catch{return false;}
    }
    static void Check(bool condition,string name) {Console.WriteLine((condition?"PASS ":"FAIL ")+name);if(!condition)throw new Exception(name);}
    static bool ForeignConnect(string address) {
        string command="$ErrorActionPreference='Stop';try{$c=New-Object Net.Sockets.TcpClient([Net.Sockets.AddressFamily]::"+(address.Contains(":")?"InterNetworkV6":"InterNetwork")+");$t=$c.ConnectAsync('"+address+"',"+ObsGuard.Port+");if($t.Wait(1500) -and $c.Connected){exit 0}else{exit 7}}catch{exit 7}";
        using(var process=Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command))) {UseShellExecute=false,CreateNoWindow=true})) {
            if(!process.WaitForExit(10000)){process.Kill();throw new Exception("Probe timeout");}return process.ExitCode==0;
        }
    }
    static bool OtherUserConnect(string address) {
        string name="AjazzTrulaGuardTest-"+Guid.NewGuid().ToString("N"),exe=ObsGuard.PluginPath.Replace("'","''");
        string script="$ErrorActionPreference='Stop';$n='"+name+"';try{$a=New-ScheduledTaskAction -Execute '"+exe+"' -Argument '--probe "+address+"';$p=New-ScheduledTaskPrincipal -UserId 'S-1-5-19' -LogonType ServiceAccount;Register-ScheduledTask -TaskName $n -Action $a -Principal $p -Force|Out-Null;Start-ScheduledTask -TaskName $n;$until=[DateTime]::UtcNow.AddSeconds(15);do{Start-Sleep -Milliseconds 200;$i=Get-ScheduledTaskInfo -TaskName $n;$s=(Get-ScheduledTask -TaskName $n).State}while([DateTime]::UtcNow -lt $until -and ($i.LastRunTime.Year -lt 2020 -or $s -in @('Running','Queued')));if($i.LastTaskResult -eq 0){exit 0};if($i.LastTaskResult -eq 7){exit 7};exit 2}catch{exit 2}finally{Unregister-ScheduledTask -TaskName $n -Confirm:$false -ErrorAction SilentlyContinue}";
        using(var process=Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script))) {UseShellExecute=false,CreateNoWindow=true})) {
            if(!process.WaitForExit(22000)){process.Kill();throw new Exception("Other-account probe timeout");}
            if(process.ExitCode!=0 && process.ExitCode!=7)throw new Exception("Other-account probe did not execute");return process.ExitCode==0;
        }
    }
    static int Main(string[] args) {
        if(args.Length==2 && args[0]=="--probe")return Connect(args[1],ObsGuard.Port)?0:7;
        if(args.Length==1 && args[0]=="--cleanup"){ObsGuard.RemoveForTests();return 0;}
        if(args.Length==1 && args[0]=="--check"){try{ObsGuard.Require();Console.WriteLine("PASS non-elevated policy readback");return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
        var output=new StringWriter();Console.SetOut(output);int result=1;
        var four=new TcpListener(IPAddress.Loopback,ObsGuard.Port);var six=new TcpListener(IPAddress.IPv6Loopback,ObsGuard.Port);
        var originalFileAcl=File.GetAccessControl(ObsGuard.PluginPath);string directory=Path.GetDirectoryName(ObsGuard.PluginPath);var originalDirectoryAcl=Directory.GetAccessControl(directory);
        try {
            var account=new SecurityIdentifier("S-1-5-19");
            var fileAcl=File.GetAccessControl(ObsGuard.PluginPath);fileAcl.AddAccessRule(new FileSystemAccessRule(account,FileSystemRights.ReadAndExecute,AccessControlType.Allow));File.SetAccessControl(ObsGuard.PluginPath,fileAcl);
            var directoryAcl=Directory.GetAccessControl(directory);directoryAcl.AddAccessRule(new FileSystemAccessRule(account,FileSystemRights.ReadAndExecute,AccessControlType.Allow));Directory.SetAccessControl(directory,directoryAcl);
            ObsGuard.CheckAbi();ObsGuard.RemoveForTests();four.Start();six.Start();
            Check(ForeignConnect("127.0.0.1"),"original unprotected IPv4 trigger connects");
            Check(ForeignConnect("::1"),"original unprotected IPv6 trigger connects");
            Check(OtherUserConnect("127.0.0.1"),"other Windows account executes same binary and connects before protection");
            ObsGuard.Install(ObsGuard.CurrentSid);ObsGuard.Require();
            Check(!ForeignConnect("127.0.0.1"),"other application blocked IPv4");
            Check(!ForeignConnect("::1"),"other application blocked IPv6");
            Check(!OtherUserConnect("127.0.0.1"),"same binary under other Windows account blocked IPv4");
            Check(!OtherUserConnect("::1"),"same binary under other Windows account blocked IPv6");
            Check(Connect("127.0.0.1",ObsGuard.Port),"permitted application and owner IPv4");
            Check(Connect("::1",ObsGuard.Port),"permitted application and owner IPv6");
            // Engine sessions have closed: these connections use persistent policy.
            ObsGuard.Require();Check(!ForeignConnect("127.0.0.1"),"protection survives engine close/reopen");
            ObsGuard.Install(ObsGuard.CurrentSid);ObsGuard.Require();Check(Connect("127.0.0.1",ObsGuard.Port),"idempotent reinstall");
            try{ObsGuard.Install("S-1-5-21-1-2-3-1002");throw new Exception("owner replacement was accepted");}catch(InvalidOperationException){Check(true,"second owner rejected");}
            ObsGuard.RemoveOneForTests();
            try{ObsGuard.Require();throw new Exception("incomplete policy was accepted");}catch(InvalidOperationException){Check(true,"missing IPv6 block makes protection check fail closed");}
            ObsGuard.Install(ObsGuard.CurrentSid);ObsGuard.Require();
            result=0;
        }catch(Exception e){output.WriteLine(e.ToString());}
        finally {
            four.Stop();six.Stop();
            File.SetAccessControl(ObsGuard.PluginPath,originalFileAcl);Directory.SetAccessControl(directory,originalDirectoryAcl);
            try{if(args.Length<1 || args[0]!="--keep")ObsGuard.RemoveForTests();}catch(Exception e){output.WriteLine("Cleanup: "+e);result=1;}
            if(args.Length>1)using(var file=new StreamWriter(new FileStream(args[1],FileMode.CreateNew,FileAccess.Write,FileShare.Read)))file.Write(output.ToString());
        }
        return result;
    }
}
