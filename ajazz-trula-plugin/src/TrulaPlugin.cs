using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// .NET Framework 4.8 with the official Microsoft WebView2 SDK/runtime.
public static class Json {
    public static string Encode(object v) { return new JavaScriptSerializer().Serialize(v); }
    public static Dictionary<string, object> Decode(string s) { return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(s); }
    public static Dictionary<string, object> Map(object v) { return v as Dictionary<string, object> ?? new Dictionary<string, object>(); }
    public static Dictionary<string, object> Child(Dictionary<string, object> v, string k) { object x; return v.TryGetValue(k, out x) ? Map(x) : new Dictionary<string, object>(); }
    public static string Text(Dictionary<string, object> v, string k, string fallback = "") { object x; return v.TryGetValue(k, out x) ? Convert.ToString(x) : fallback; }
}

public static class Diagnostics {
    public static void Write(string value) {
        try {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data"); Directory.CreateDirectory(root);
            string file=Path.Combine(root,"commands.log");
            if(File.Exists(file) && new FileInfo(file).Length>1048576) File.Delete(file);
            File.AppendAllText(file,DateTime.UtcNow.ToString("O")+" "+value+Environment.NewLine);
        } catch { }
    }
}

public static class Commands {
    public static Dictionary<string,object> Configure(string action, Dictionary<string,object> settings) {
        var effective = Json.Decode(Json.Encode(settings));
        if (action == "com.projectdeck.trula.playback") effective["command"] = "togglePlay";
        if (action == "com.projectdeck.trula.stream" || action == "com.projectdeck.trula.monitor") {
            effective["command"]="mute"; effective["media"]="all";
            effective["output"] = action.EndsWith(".stream") ? "source" : "panel";
        }
        return effective;
    }
    public static readonly Dictionary<string, string[]> Flags = new Dictionary<string, string[]> {
        {"autostart", new[]{"panel","isAutostart","АВТОСТАРТ"}},
        {"autoplay", new[]{"panel","isAutoplay","АВТОПЕРЕХОД"}},
        {"redemptions", new[]{"panel","isRedemptionsEnabled","ЗАКАЗЫ"}},
        {"playlist", new[]{"panel","isPlaylistEnabled","ПЛЕЙЛИСТ"}},
        {"shuffle", new[]{"panel","isPlaylistShuffle","СЛУЧАЙНО"}},
        {"playlistVisible", new[]{"source","isPlaylistVisible","ВИД ПЛЕЙЛ."}},
        {"titleVisible", new[]{"source","isTitleVisible","НАЗВАНИЕ"}}
    };
    public static string Token(string text) {
        text = text.Trim();
        if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
            Uri u;
            if (!Uri.TryCreate(text, UriKind.Absolute, out u) || u.Host != "trula.io" || u.Port != 443 || u.UserInfo != "") throw new Exception("Нужна ссылка панели trula.io.");
            text = System.Web.HttpUtility.ParseQueryString(u.Query)["token"] ?? "";
        }
        Guid g;
        if (!Guid.TryParseExact(text, "D", out g)) throw new Exception("Укажите token или ссылку панели Trula.");
        return g.ToString();
    }
    public static string Hash(string token) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(token))).Replace("-", "").ToLowerInvariant(); }
    public static List<string[]> VolumeFields(Dictionary<string, object> cfg) {
        var output = Json.Text(cfg, "output", "all"); var media = Json.Text(cfg, "media", "all");
        if (!new[]{"all","panel","source"}.Contains(output) || !new[]{"all","orders","playlist"}.Contains(media)) throw new Exception("Неверная область звука.");
        var result = new List<string[]>();
        foreach (string scope in new[]{"panel","source"}) if (output == "all" || output == scope)
            foreach (string field in new[]{"orderVolume","playlistVolume"}) if (media == "all" || (media == "orders" ? field == "orderVolume" : field == "playlistVolume")) result.Add(new[]{scope,field});
        return result;
    }
    public static int Volume(Dictionary<string, object> state, string[] field) {
        var section = Json.Child(state, field[0]); object x;
        if (!section.TryGetValue(field[1], out x) || x == null) throw new Exception("Trula не вернула уровень громкости.");
        int v = Convert.ToInt32(x); if (v < 0 || v > 100) throw new Exception("Неизвестный уровень громкости."); return v;
    }
    static void Put(Dictionary<string, object> patch, string[] field, object value) {
        if (!patch.ContainsKey(field[0])) patch[field[0]] = new Dictionary<string, object>();
        Json.Child(patch, field[0])[field[1]] = value;
    }
    // Mutates only the candidate restore dictionary. Caller commits it before a mute request.
    public static Dictionary<string, object> Plan(Dictionary<string, object> state, Dictionary<string, object> cfg, Dictionary<string, object> restore) {
        var patch = new Dictionary<string, object>(); var command = Json.Text(cfg, "command", "mute");
        if (Flags.ContainsKey(command)) {
            var f = Flags[command]; object current;
            if (!Json.Child(state, f[0]).TryGetValue(f[1], out current) || !(current is bool)) throw new Exception("Переключатель отсутствует в ответе Trula.");
            Put(patch, f, !(bool)current); return patch;
        }
        if (!new[]{"mute","volumeUp","volumeDown"}.Contains(command)) throw new Exception("Неизвестная команда.");
        var fields = VolumeFields(cfg); var muted = fields.All(f => Volume(state, f) == 0);
        int step; if (!int.TryParse(Json.Text(cfg,"step","5"), out step) || step < 1 || step > 100) throw new Exception("Шаг должен быть от 1 до 100.");
        foreach (var f in fields) {
            int v = Volume(state, f); var key = f[0] + "." + f[1];
            if (command == "mute") {
                if (!muted) { restore[key] = v; Put(patch, f, 0); }
                else {
                    object prior; if (!restore.TryGetValue(key, out prior)) throw new Exception("Нет сохранённой громкости. Сначала задайте звук в Trula.");
                    int n = Convert.ToInt32(prior); if (n < 0 || n > 100) throw new Exception("Некорректная сохранённая громкость.");
                    Put(patch, f, n);
                }
            } else Put(patch, f, Math.Max(0, Math.Min(100, v + (command == "volumeUp" ? step : -step))));
        }
        return patch;
    }
    public static string Label(Dictionary<string, object> state, Dictionary<string, object> cfg) {
        string c = Json.Text(cfg,"command","mute");
        if (Flags.ContainsKey(c)) { var f = Flags[c]; object b; if (!Json.Child(state,f[0]).TryGetValue(f[1],out b) || !(b is bool)) throw new Exception("Нет переключателя."); return f[2] + "\n" + ((bool)b ? "ВКЛ" : "ВЫКЛ"); }
        var vs = VolumeFields(cfg).Select(f=>Volume(state,f)).ToArray();
        return (c == "volumeUp" ? "ГРОМЧЕ" : c == "volumeDown" ? "ТИШЕ" : Json.Text(cfg,"output")=="source" ? "СТРИМ" : Json.Text(cfg,"output")=="panel" ? "У СЕБЯ" : "ЗВУК") + "\n" + (vs.All(v=>v==0) ? "ВЫКЛ" : vs.Min()==vs.Max() ? vs[0]+"%" : vs.Min()+"–"+vs.Max()+"%");
    }
}

public class TrulaPlugin {
    const string Action = "com.projectdeck.trula.control";
    readonly ClientWebSocket socket = new ClientWebSocket();
    readonly SemaphoreSlim sendLock = new SemaphoreSlim(1), gate = new SemaphoreSlim(1);
    readonly Dictionary<string, Dictionary<string, object>> keys = new Dictionary<string, Dictionary<string, object>>();
    readonly Dictionary<string, string> actionIds = new Dictionary<string, string>();
    Dictionary<string, object> global = new Dictionary<string, object>();
    readonly Dictionary<string, DateTime> lastClick = new Dictionary<string, DateTime>();
    bool globalsLoaded;
    string uuid;
    WebViewBridge bridge;
    [STAThread]
    static void Main(string[] args) {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        System.Windows.Forms.Application.EnableVisualStyles();
        var plugin=new TrulaPlugin(); plugin.bridge=new WebViewBridge();
        var handle=plugin.bridge.Handle;
        var execution=plugin.Run(args);
        execution.ContinueWith(t=> { var ignored=t.Exception; if(plugin.bridge.IsHandleCreated) plugin.bridge.BeginInvoke((System.Action)(()=>System.Windows.Forms.Application.Exit())); });
        System.Windows.Forms.Application.Run(plugin.bridge);
    }
    async Task Send(string ev, string context, object payload) {
        var m = new Dictionary<string,object>{{"event",ev},{"context",context},{"payload",payload}};
        if (ev == "sendToPropertyInspector") m["action"] = actionIds.ContainsKey(context) ? actionIds[context] : Action;
        await Raw(m);
    }
    async Task Raw(object message) {
        byte[] b = Encoding.UTF8.GetBytes(Json.Encode(message)); await sendLock.WaitAsync();
        try { await socket.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None); } finally { sendLock.Release(); }
    }
    async Task Title(string context, string title, bool error = false) {
        await Send("setTitle", context, new {title, target=0});
        if (error) await Send("showAlert",context,new {});
    }
    async Task Status(string context, string message) { await Send("sendToPropertyInspector",context,new {message}); }
    async Task Refresh(string context, Dictionary<string,object> cfg, bool execute) {
        string failure = null;
        try {
            string token = Commands.Token(Json.Text(cfg,"token", Credentials.Read())); bridge.Allow(token);
            var state = bridge.Read(token);
            if (execute) {
                Diagnostics.Write("execute "+Json.Text(cfg,"command")+" output="+Json.Text(cfg,"output"));
                if (!globalsLoaded) throw new Exception("Загрузка настроек. Повторите нажатие.");
                string command = Json.Text(cfg,"command","mute");
                if (new[]{"togglePlay","nextTrack","prevTrack"}.Contains(command)) {
                    if (command=="togglePlay" && Json.Text(state,"hasTrack")=="False") throw new Exception("Очередь Trula пуста. Добавьте трек в панели.");
                    await bridge.Call(token,new {command});
                    state = bridge.Read(token);
                } else {
                string hash = Commands.Hash(token);
                var restores = Json.Child(global,"restore");
                var saved = Json.Child(restores,hash);
                var candidate = Json.Decode(Json.Encode(saved));
                var patch = Commands.Plan(state,cfg,candidate);
                // Save restoration data before silencing audio, including across app restarts.
                restores[hash] = candidate; global["restore"] = restores;
                await Send("setGlobalSettings",uuid,global);
                await bridge.Call(token,new {command="patch", patch});
                state = bridge.Read(token);
                // Do not claim success when an API ignores a field.
                foreach (var section in patch) foreach (var field in Json.Map(section.Value)) {
                    object actual;
                    if (!Json.Child(state,section.Key).TryGetValue(field.Key,out actual) || !Equals(actual,field.Value)) throw new Exception("Trula не подтвердила изменение.");
                }
                }
                await Send("showOk",context,new {});
                Diagnostics.Write("confirmed");
            }
            string cmd = Json.Text(cfg,"command","mute");
            string label = cmd=="togglePlay" ? "МУЗЫКА\n" + (Json.Text(state,"playing")=="True" ? "ИГРАЕТ" : "ПАУЗА") : cmd=="nextTrack" ? "СЛЕД.\nТРЕК" : cmd=="prevTrack" ? "ПРЕД.\nТРЕК" : Commands.Label(state,cfg);
            await Title(context,label); await Status(context,"Панель подключена. " + (execute ? "Команда выполнена." : "Настройки прочитаны."));
        } catch (Exception ex) {
            string message = ex is TaskCanceledException ? "Trula не ответила вовремя." : ex is HttpRequestException ? "Нет соединения с Trula." : ex.Message;
            // Never show arbitrary HTTP content or credentials.
            if (ex is System.Web.HttpException || ex is ArgumentException || ex is InvalidOperationException) message = "Не удалось прочитать ответ Trula.";
            failure = message;
        }
        if (failure != null) { await Title(context,"TRULA\nОШИБКА",execute); await Status(context,failure); }
        if (failure != null && execute) Diagnostics.Write("failed "+failure);
    }
    async Task Event(Dictionary<string,object> m) {
        string ev = Json.Text(m,"event"), ctx = Json.Text(m,"context"); var payload = Json.Child(m,"payload");
        string actionId = Json.Text(m,"action");
        if (!string.IsNullOrEmpty(actionId)) actionIds[ctx] = actionId;
        if (ev == "sendToPlugin" && payload.ContainsKey("saveConnection")) {
            bool saved = false;
            try { Credentials.Save(Json.Text(payload,"connection")); saved = true; } catch { }
            await Send("sendToPropertyInspector",ctx,new {message=saved?"Подключение сохранено для всех кнопок.":"Не удалось сохранить ссылку. Нужна ссылка панели trula.io с token.", saved});
            return;
        }
        if (ev == "sendToPlugin" && payload.ContainsKey("getConnection")) {
            string token = Credentials.Read();
            if (!string.IsNullOrEmpty(token)) bridge.Allow(token);
            await Send("sendToPropertyInspector",ctx,new {message=string.IsNullOrEmpty(token)?"Вставьте ссылку панели Trula один раз.":"Подключение сохранено. Плеер работает в отдельном окне Trula."});
            return;
        }
        if (ev=="sendToPlugin" && payload.ContainsKey("showPlayer")) { bridge.OpenPlayer(); return; }
        var effective = Commands.Configure(actionId,Json.Child(payload,"settings"));
        if (payload.ContainsKey("settings")) payload["settings"] = effective;
        if (ev == "didReceiveGlobalSettings") { global = Json.Child(payload,"settings"); globalsLoaded = true; return; }
        if (ev == "willDisappear") { keys.Remove(ctx); actionIds.Remove(ctx); lastClick.Remove(ctx); return; }
        if (ev == "willAppear" || ev == "didReceiveSettings") { keys[ctx] = Json.Child(payload,"settings"); await Refresh(ctx,keys[ctx],false); }
        if (ev == "sendToPlugin" && payload.ContainsKey("check")) { keys[ctx] = Json.Child(payload,"settings"); await Refresh(ctx,keys[ctx],false); }
        if (ev == "keyUp" || (ev=="sendToPlugin" && payload.ContainsKey("execute"))) {
            Diagnostics.Write("keyUp "+actionId);
            DateTime last; if (lastClick.TryGetValue(ctx,out last) && (DateTime.UtcNow-last).TotalMilliseconds < 350) return;
            lastClick[ctx] = DateTime.UtcNow; keys[ctx] = Json.Child(payload,"settings"); await Refresh(ctx,keys[ctx],true);
        }
    }
    async Task Poll() {
        while (socket.State == WebSocketState.Open) {
            await Task.Delay(7000); await gate.WaitAsync();
            try {
                foreach (var k in keys.ToArray()) if (!string.IsNullOrWhiteSpace(Json.Text(k.Value,"token", Credentials.Read()))) await Refresh(k.Key,k.Value,false);
            } finally { gate.Release(); }
        }
    }
    public async Task Run(string[] args) {
        var opts = new Dictionary<string,string>(); for (int i=0;i+1<args.Length;i+=2) opts[args[i].TrimStart('-')] = args[i+1];
        int port; if (!opts.ContainsKey("port") || !int.TryParse(opts["port"],out port) || port<1 || port>65535) return;
        uuid = opts["pluginUUID"];
            bridge.Start();
        await socket.ConnectAsync(new Uri("ws://127.0.0.1:"+port),CancellationToken.None);
        await Raw(new { @event=opts["registerEvent"], uuid });
        await Send("getGlobalSettings",uuid,new {});
        var poll = Poll();
        var buffer = new byte[8192];
        while (socket.State == WebSocketState.Open) {
            using (var ms = new MemoryStream()) {
                WebSocketReceiveResult r;
                do { r = await socket.ReceiveAsync(new ArraySegment<byte>(buffer),CancellationToken.None); if(r.MessageType==WebSocketMessageType.Close) return; ms.Write(buffer,0,r.Count); if(ms.Length>1024*1024) return; } while(!r.EndOfMessage);
                var m = Json.Decode(Encoding.UTF8.GetString(ms.ToArray()));
                await gate.WaitAsync(); try { await Event(m); } finally { gate.Release(); }
            }
        }
        await poll;
    }
}
