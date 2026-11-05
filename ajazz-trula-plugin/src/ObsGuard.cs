using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

// The CDP server has no authentication. Enforce its boundary in the Windows
// kernel, independently of the plugin/server lifetime. This is not a firewall
// allow rule: a lower-weight catch-all BLOCK in the same sublayer is essential.
public static class ObsGuard {
#if OBS_GUARD_TEST
    public const int Port = 27936;
#else
    public const int Port = 17936;
#endif
    public const string Missing = "Не установлена защита соединения с OBS. Повторно запустите установщик Trula и подтвердите настройку Windows.";
    static Guid PolicyKey(string text) {
        var bytes=new Guid(text).ToByteArray();
#if OBS_GUARD_TEST
        bytes[15]^=128;
#endif
        return new Guid(bytes);
    }
    static readonly Guid SubLayer = PolicyKey("59ebf857-b7bb-48fd-9a44-a1b563998446");
    static readonly Guid[] Keys = {
        PolicyKey("c8cfe925-f8d2-4e8d-b03a-b4f71e299601"), PolicyKey("c8cfe925-f8d2-4e8d-b03a-b4f71e299602"),
        PolicyKey("c8cfe925-f8d2-4e8d-b03a-b4f71e299603"), PolicyKey("c8cfe925-f8d2-4e8d-b03a-b4f71e299604"),
        PolicyKey("c8cfe925-f8d2-4e8d-b03a-b4f71e299605"), PolicyKey("c8cfe925-f8d2-4e8d-b03a-b4f71e299606") };
    static readonly Guid Connect4 = new Guid("c38d57d1-05a7-4c33-904f-7fbceee60e82"), Connect6 = new Guid("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
    static readonly Guid Accept4 = new Guid("e1cd9fe7-f4b5-4273-96c0-592e487b8650"), Accept6 = new Guid("a3b42c97-9f04-4672-b87e-cee9c483257f");
    static readonly Guid RemotePort = new Guid("c35a604d-d22b-4e1a-91b4-68f674ee674b"), LocalPort = new Guid("0c1ba1af-5765-453f-af22-a8f791ac775b");
    static readonly Guid Protocol = new Guid("3971ef2b-623e-4f9a-8cb1-6e79b806b9a7"), Flags = new Guid("632ce23b-5167-435c-86d7-e903684aa80c");
    static readonly Guid AppId = new Guid("d78e1e87-8644-4ea5-9437-d809ecefc971"), UserId = new Guid("af043a0a-b34d-4f86-979c-c90371af6e66");
    const uint NotFound = 0x80320003, SubLayerNotFound = 0x80320007;
    const uint Block = 0x1001, Permit = 0x1002;
    public static string CurrentSid { get { using(var id=WindowsIdentity.GetCurrent()) return id.User.Value; } }
    public static string PluginPath { get {
#if OBS_GUARD_TEST
        return System.Reflection.Assembly.GetExecutingAssembly().Location;
#else
        return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"TrulaPlugin.exe"));
#endif
    } }

    [StructLayout(LayoutKind.Sequential)] struct Display { public IntPtr name, description; }
    [StructLayout(LayoutKind.Sequential)] struct Blob { public uint size; public IntPtr data; }
    [StructLayout(LayoutKind.Explicit,Size=16)] struct Value {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public byte u8;
        [FieldOffset(8)] public ushort u16;
        [FieldOffset(8)] public uint u32;
        [FieldOffset(8)] public IntPtr pointer;
    }
    [StructLayout(LayoutKind.Sequential)] struct Condition { public Guid key; public uint match; public Value value; }
    [StructLayout(LayoutKind.Sequential)] struct Action { public uint type; public Guid key; }
    [StructLayout(LayoutKind.Explicit,Size=16)] struct Context { [FieldOffset(0)] public ulong raw; [FieldOffset(0)] public Guid provider; }
    [StructLayout(LayoutKind.Sequential)] struct Filter {
        public Guid key; public Display display; public uint flags; public IntPtr provider;
        public Blob data; public Guid layer, sublayer; public Value weight; public uint count;
        public IntPtr conditions; public Action action; public Context context; public IntPtr reserved;
        public ulong id; public Value effectiveWeight;
    }
    [StructLayout(LayoutKind.Sequential)] struct Layer {
        public Guid key; public Display display; public uint flags; public IntPtr provider; public Blob data; public ushort weight;
    }
    [DllImport("fwpuclnt.dll",CharSet=CharSet.Unicode)] static extern uint FwpmEngineOpen0(string server,uint auth,IntPtr identity,IntPtr session,out IntPtr engine);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmEngineClose0(IntPtr engine);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmEngineGetSecurityInfo0(IntPtr engine,uint info,out IntPtr owner,out IntPtr group,out IntPtr dacl,out IntPtr sacl,out IntPtr sd);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmEngineSetSecurityInfo0(IntPtr engine,uint info,IntPtr owner,IntPtr group,IntPtr dacl,IntPtr sacl);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmTransactionBegin0(IntPtr engine,uint flags);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmTransactionCommit0(IntPtr engine);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmTransactionAbort0(IntPtr engine);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmSubLayerAdd0(IntPtr engine,ref Layer layer,byte[] sd);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmSubLayerGetByKey0(IntPtr engine,ref Guid key,out IntPtr layer);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmSubLayerDeleteByKey0(IntPtr engine,ref Guid key);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmFilterAdd0(IntPtr engine,ref Filter filter,byte[] sd,out ulong id);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmFilterGetByKey0(IntPtr engine,ref Guid key,out IntPtr filter);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmFilterDeleteByKey0(IntPtr engine,ref Guid key);
    [DllImport("fwpuclnt.dll",CharSet=CharSet.Unicode)] static extern uint FwpmGetAppIdFromFileName0(string path,out IntPtr id);
    [DllImport("fwpuclnt.dll")] static extern void FwpmFreeMemory0(ref IntPtr memory);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmSubLayerGetSecurityInfoByKey0(IntPtr engine,ref Guid key,uint info,out IntPtr owner,out IntPtr group,out IntPtr dacl,out IntPtr sacl,out IntPtr sd);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmFilterGetSecurityInfoByKey0(IntPtr engine,ref Guid key,uint info,out IntPtr owner,out IntPtr group,out IntPtr dacl,out IntPtr sacl,out IntPtr sd);
    [DllImport("advapi32.dll")] static extern uint GetSecurityDescriptorLength(IntPtr sd);

    sealed class Engine : IDisposable {
        public IntPtr Handle;
        public Engine() { Check(FwpmEngineOpen0(null,10,IntPtr.Zero,IntPtr.Zero,out Handle)); }
        public void Dispose() { if(Handle!=IntPtr.Zero) { FwpmEngineClose0(Handle); Handle=IntPtr.Zero; } }
    }
    sealed class Memory : IDisposable {
        readonly List<IntPtr> owned=new List<IntPtr>();
        public IntPtr Bytes(byte[] bytes) { var p=Marshal.AllocHGlobal(bytes.Length);owned.Add(p);Marshal.Copy(bytes,0,p,bytes.Length);return p; }
        public IntPtr Item<T>(T item) where T:struct { var p=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(T)));owned.Add(p);Marshal.StructureToPtr(item,p,false);return p; }
        public IntPtr Items(Condition[] items) {
            int size=Marshal.SizeOf(typeof(Condition)); var p=Marshal.AllocHGlobal(size*items.Length);owned.Add(p);
            for(int i=0;i<items.Length;i++)Marshal.StructureToPtr(items[i],IntPtr.Add(p,i*size),false);return p;
        }
        public IntPtr Text(string text) { var p=Marshal.StringToHGlobalUni(text);owned.Add(p);return p; }
        public Blob Data(byte[] bytes) { return new Blob {size=(uint)bytes.Length,data=Bytes(bytes)}; }
        public Value BlobValue(uint type,byte[] bytes) { return new Value {type=type,pointer=Item(Data(bytes))}; }
        public void Dispose() { foreach(var p in owned)Marshal.FreeHGlobal(p); }
    }
    static void Check(uint code) { if(code!=0)throw new InvalidOperationException("Windows WFP: 0x"+code.ToString("X8")); }
    static byte[] Descriptor(string sddl) { var sd=new RawSecurityDescriptor(sddl);var bytes=new byte[sd.BinaryLength];sd.GetBinaryForm(bytes,0);return bytes; }
    static byte[] ObjectAcl(string sid) { return Descriptor("O:BAG:BAD:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;0x00020080;;;"+sid+")"); }
    static byte[] UserAcl(string sid) { return Descriptor("D:(A;;CC;;;"+sid+")"); }
    static void GrantEngineOpen(IntPtr engine,string sid) {
        // Non-administrators cannot open BFE by default. Grant only OPEN on the
        // engine, not enumeration, read or write access to other firewall objects.
        // The six filters and sublayer separately grant this SID read-only access.
        IntPtr owner,group,dacl,sacl,sd;
        Check(FwpmEngineGetSecurityInfo0(engine,4,out owner,out group,out dacl,out sacl,out sd));
        try {
            var bytes=new byte[GetSecurityDescriptorLength(sd)];Marshal.Copy(sd,bytes,0,bytes.Length);
            var descriptor=new RawSecurityDescriptor(bytes,0);var acl=descriptor.DiscretionaryAcl;
            if(acl==null)throw new InvalidOperationException("Missing BFE ACL");
            foreach(GenericAce entry in acl) {
                var ace=entry as CommonAce;
                if(ace!=null && ace.AceQualifier==AceQualifier.AccessAllowed && ace.SecurityIdentifier.Value==sid && (ace.AccessMask&0x40)!=0)return;
            }
            acl.InsertAce(acl.Count,new CommonAce(AceFlags.None,AceQualifier.AccessAllowed,0x40,new SecurityIdentifier(sid),false,null));
            var encoded=new byte[acl.BinaryLength];acl.GetBinaryForm(encoded,0);
            using(var m=new Memory())Check(FwpmEngineSetSecurityInfo0(engine,4,IntPtr.Zero,IntPtr.Zero,m.Bytes(encoded),IntPtr.Zero));
        } finally {FwpmFreeMemory0(ref sd);}
    }
    static byte[] App(string path) {
        IntPtr p;Check(FwpmGetAppIdFromFileName0(Path.GetFullPath(path),out p));
        try {return ReadBlob((Blob)Marshal.PtrToStructure(p,typeof(Blob)));} finally {FwpmFreeMemory0(ref p);}
    }
    static byte[] ReadBlob(Blob blob) {
        if(blob.size>65536 || (blob.size>0 && blob.data==IntPtr.Zero))throw new InvalidOperationException("Invalid WFP data");
        var bytes=new byte[blob.size];if(bytes.Length>0)Marshal.Copy(blob.data,bytes,0,bytes.Length);return bytes;
    }
    static byte[] Metadata(string sid,string path) { return Encoding.UTF8.GetBytes("AjazzTrula.ObsGuard.v1\n"+sid+"\n"+Path.GetFullPath(path).ToLowerInvariant()); }
    static Condition Number(Guid key,uint type,uint value,uint match=0) { return new Condition {key=key,match=match,value=new Value {type=type,u32=value}}; }
    static Condition[] Conditions(Memory m,bool allow,bool inbound,string sid,string path) {
        var list=new List<Condition> { Number(Protocol,1,6), Number(inbound?LocalPort:RemotePort,2,Port), Number(Flags,3,1,inbound?8u:6u) };
        if(allow) {
            list.Add(new Condition {key=AppId,value=m.BlobValue(12,App(path))});
            list.Add(new Condition {key=UserId,value=m.BlobValue(14,UserAcl(sid))});
        }
        return list.ToArray();
    }
    static Filter MakeFilter(Memory m,int index,string sid,string path) {
        bool inbound=index>=4,allow=index%2==0 && !inbound;
        var conditions=Conditions(m,allow,inbound,sid,path);
        return new Filter {key=Keys[index],display=new Display {name=m.Text("Trula OBS: "+(allow?"owner plugin only":inbound?"block network":"block other clients"))},
            flags=1,data=m.Data(Metadata(sid,path)),layer=inbound?(index==4?Accept4:Accept6):(index<2?Connect4:Connect6),sublayer=SubLayer,
            weight=new Value {type=4,pointer=m.Item(allow?100UL:1UL)},count=(uint)conditions.Length,conditions=m.Items(conditions),action=new Action {type=allow?Permit:Block}};
    }
    static void ValidateAcl(IntPtr engine,Guid key,string sid,bool layer) {
        IntPtr owner,group,dacl,sacl,sd;
        Check(layer?FwpmSubLayerGetSecurityInfoByKey0(engine,ref key,7,out owner,out group,out dacl,out sacl,out sd):FwpmFilterGetSecurityInfoByKey0(engine,ref key,7,out owner,out group,out dacl,out sacl,out sd));
        try {
            var bytes=new byte[GetSecurityDescriptorLength(sd)];Marshal.Copy(sd,bytes,0,bytes.Length);
            var descriptor=new RawSecurityDescriptor(bytes,0);
            if(descriptor.Owner==null || descriptor.Owner.Value!="S-1-5-32-544" || descriptor.DiscretionaryAcl==null)throw new InvalidOperationException("Invalid WFP owner");
            bool ownerRead=false,system=false,admins=false;
            foreach(GenericAce generic in descriptor.DiscretionaryAcl) {
                var ace=generic as CommonAce;
                if(ace==null || ace.AceQualifier!=AceQualifier.AccessAllowed || ace.IsCallback || ace.AceFlags!=AceFlags.None)throw new InvalidOperationException("Invalid WFP ACL");
                string subject=ace.SecurityIdentifier.Value;
                if(subject==sid) {if(ace.AccessMask!=0x20080)throw new InvalidOperationException("Writable WFP policy");ownerRead=true;}
                else if(subject=="S-1-5-18")system=true;
                else if(subject=="S-1-5-32-544")admins=true;
                else throw new InvalidOperationException("Unexpected WFP trustee");
            }
            if(!ownerRead||!system||!admins)throw new InvalidOperationException("Incomplete WFP ACL");
        } finally {FwpmFreeMemory0(ref sd);}
    }
    static bool SameValue(Value left,Value right) {
        if(left.type!=right.type)return false;
        if(left.type==4)return Marshal.ReadInt64(left.pointer)==Marshal.ReadInt64(right.pointer);
        if(left.type==12 || left.type==14)return ReadBlob((Blob)Marshal.PtrToStructure(left.pointer,typeof(Blob))).SequenceEqual(ReadBlob((Blob)Marshal.PtrToStructure(right.pointer,typeof(Blob))));
        return left.u32==right.u32;
    }
    static void Verify(IntPtr engine,string sid,string path) {
        IntPtr p;var key=SubLayer;Check(FwpmSubLayerGetByKey0(engine,ref key,out p));
        try {
            var layer=(Layer)Marshal.PtrToStructure(p,typeof(Layer));
            if(layer.key!=SubLayer || layer.flags!=1 || layer.provider!=IntPtr.Zero || layer.weight!=0xF000 || !ReadBlob(layer.data).SequenceEqual(Metadata(sid,path)))throw new InvalidOperationException("Unexpected OBS policy owner or path");
        } finally {FwpmFreeMemory0(ref p);}
        ValidateAcl(engine,SubLayer,sid,true);
        using(var m=new Memory())for(int index=0;index<Keys.Length;index++) {
            key=Keys[index];Check(FwpmFilterGetByKey0(engine,ref key,out p));
            try {
                var actual=(Filter)Marshal.PtrToStructure(p,typeof(Filter));var expected=MakeFilter(m,index,sid,path);
                // BFE may add INDEXED (0x40); all other flags are security relevant.
                if(actual.key!=expected.key || (actual.flags&~0x40u)!=1 || actual.provider!=IntPtr.Zero || actual.layer!=expected.layer || actual.sublayer!=SubLayer || actual.action.type!=expected.action.type || actual.action.key!=Guid.Empty || actual.context.raw!=0 || actual.reserved!=IntPtr.Zero || actual.count!=expected.count || !SameValue(actual.weight,expected.weight) || !ReadBlob(actual.data).SequenceEqual(Metadata(sid,path)))throw new InvalidOperationException("Invalid OBS filter");
                int size=Marshal.SizeOf(typeof(Condition));
                for(int i=0;i<actual.count;i++) {
                    var a=(Condition)Marshal.PtrToStructure(IntPtr.Add(actual.conditions,i*size),typeof(Condition));
                    var e=(Condition)Marshal.PtrToStructure(IntPtr.Add(expected.conditions,i*size),typeof(Condition));
                    if(a.key!=e.key || a.match!=e.match || !SameValue(a.value,e.value))throw new InvalidOperationException("Invalid OBS filter condition");
                }
            } finally {FwpmFreeMemory0(ref p);}
            ValidateAcl(engine,key,sid,false);
        }
    }
    public static void Require() {
        try { using(var engine=new Engine())Verify(engine.Handle,CurrentSid,PluginPath); }
        catch(Exception error) { throw new InvalidOperationException(Missing,error); }
    }
    public static void Install(string sid) {
        // Only the elevated helper can provision policy. One owner per fixed port;
        // never accumulate permits belonging to different Windows accounts.
        new SecurityIdentifier(sid);
        if(sid=="S-1-5-18" || sid=="S-1-5-32-544")throw new InvalidOperationException("Expected a personal Windows account");
        if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))throw new InvalidOperationException("Administrator approval is required");
        string path=PluginPath;if(!File.Exists(path))throw new FileNotFoundException("TrulaPlugin.exe not found");
        using(var engine=new Engine()) {
            IntPtr p;var key=SubLayer;uint found=FwpmSubLayerGetByKey0(engine.Handle,ref key,out p);
            if(found==0) {
                try {var existing=(Layer)Marshal.PtrToStructure(p,typeof(Layer));if(!ReadBlob(existing.data).SequenceEqual(Metadata(sid,path)))throw new InvalidOperationException("Порт OBS уже защищён для другой учётной записи или установки Trula. Обратитесь к администратору.");}
                finally {FwpmFreeMemory0(ref p);}
            } else if(found!=SubLayerNotFound)Check(found);
            Check(FwpmTransactionBegin0(engine.Handle,0));bool committed=false;
            try {
                foreach(var filterKey in Keys) {key=filterKey;uint result=FwpmFilterDeleteByKey0(engine.Handle,ref key);if(result!=NotFound)Check(result);}
                key=SubLayer;uint removed=FwpmSubLayerDeleteByKey0(engine.Handle,ref key);if(removed!=SubLayerNotFound)Check(removed);
                using(var m=new Memory()) {
                    var layer=new Layer {key=SubLayer,display=new Display {name=m.Text("AJAZZ Trula protected OBS control")},flags=1,data=m.Data(Metadata(sid,path)),weight=0xF000};
                    Check(FwpmSubLayerAdd0(engine.Handle,ref layer,ObjectAcl(sid)));
                    for(int i=0;i<Keys.Length;i++){var filter=MakeFilter(m,i,sid,path);ulong id;Check(FwpmFilterAdd0(engine.Handle,ref filter,ObjectAcl(sid),out id));}
                }
                Verify(engine.Handle,sid,path);Check(FwpmTransactionCommit0(engine.Handle));committed=true;
            } finally {if(!committed)FwpmTransactionAbort0(engine.Handle);}
            Verify(engine.Handle,sid,path);
            GrantEngineOpen(engine.Handle,sid);
        }
    }
    public static void CheckAbi() {
        if(IntPtr.Size!=8 || Marshal.SizeOf(typeof(Filter))!=200 || Marshal.SizeOf(typeof(Condition))!=40 || Marshal.SizeOf(typeof(Layer))!=72 || Marshal.OffsetOf(typeof(Filter),"context").ToInt32()!=152 || Marshal.OffsetOf(typeof(Filter),"effectiveWeight").ToInt32()!=184)throw new InvalidOperationException("Unsupported WFP ABI");
    }
#if OBS_GUARD_TEST
    // Test builds have different keys and port; never compiled into the payload.
    public static void RemoveOneForTests() {
        using(var engine=new Engine()){var key=Keys[3];Check(FwpmFilterDeleteByKey0(engine.Handle,ref key));}
    }
    public static void RemoveForTests() {
        using(var engine=new Engine()) {
            foreach(var keyValue in Keys){var key=keyValue;uint result=FwpmFilterDeleteByKey0(engine.Handle,ref key);if(result!=NotFound)Check(result);}
            var layer=SubLayer;uint removed=FwpmSubLayerDeleteByKey0(engine.Handle,ref layer);if(removed!=SubLayerNotFound)Check(removed);
        }
    }
#endif
}
