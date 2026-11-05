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

// .NET Framework 4.8; headless controller of the existing OBS dock.
public static class Json {
    public static string Encode(object v) { return new JavaScriptSerializer().Serialize(v); }
    public static Dictionary<string, object> Decode(string s) { return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(s); }
    public static Dictionary<string, object> Map(object v) { return v as Dictionary<string, object> ?? new Dictionary<string, object>(); }
    public static Dictionary<string, object> Child(Dictionary<string, object> v, string k) { object x; return v.TryGetValue(k, out x) ? Map(x) : new Dictionary<string, object>(); }
    public static string Text(Dictionary<string, object> v, string k, string fallback = "") { object x; return v.TryGetValue(k, out x) ? Convert.ToString(x) : fallback; }
    public static object[] Array(object value) {var list=value as System.Collections.IList;return list==null?new object[0]:list.Cast<object>().ToArray();}
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
    public static string ConnectionMessage(bool configured,bool ready,string detail) {
        if(!configured) return "Подключите Trula кнопкой «Подключить Trula».";
        return ready?"Trula подключена к OBS.":"Ссылка сохранена. "+detail;
    }
    public static Dictionary<string,object> Configure(string action, Dictionary<string,object> settings) {
        var effective = Json.Decode(Json.Encode(settings));
        if (action == "com.projectdeck.trula.connect") effective["command"] = "connect";
        if (action == "com.projectdeck.trula.requests") effective["command"] = "acceptRequest";
        if (action == "com.projectdeck.trula.chat") effective["command"] = "sendTrackToChat";
        if(new[]{"com.projectdeck.trula.streamlevels","com.projectdeck.trula.monitorlevels"}.Contains(action)) {
            effective["command"]="volumeToggle";effective["media"]="all";
            effective["output"]=action.EndsWith(".streamlevels")?"source":"panel";
        }
        if(action=="com.projectdeck.trula.volumestep") {
            effective["command"]=Json.Text(settings,"direction","up")=="down"?"volumeDown":"volumeUp";
            effective["media"]="all";effective["output"]=Json.Text(settings,"volumeOutput","source")=="panel"?"panel":"source";
        }
        if (action == "com.projectdeck.trula.playback") {
            string selected=Json.Text(settings,"musicCommand","togglePlay");
            effective["command"] = new[]{"togglePlay","nextTrack","prevTrack","skipOrder"}.Contains(selected)?selected:"togglePlay";
        }
        if (action == "com.projectdeck.trula.next") effective["command"] = "nextTrack";
        if (action == "com.projectdeck.trula.previous") effective["command"] = "prevTrack";
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
    public static int Level(Dictionary<string,object> cfg,string key,int fallback) {
        int value;if(!int.TryParse(Json.Text(cfg,key,fallback.ToString()),out value) || value<0 || value>100)throw new Exception("Громкость должна быть от 0 до 100%.");return value;
    }
    public static Dictionary<string, object> Plan(Dictionary<string, object> state, Dictionary<string, object> cfg, Dictionary<string, object> restore) {
        var patch = new Dictionary<string, object>(); var command = Json.Text(cfg, "command", "mute");
        if (Flags.ContainsKey(command)) {
            var f = Flags[command]; object current;
            if (!Json.Child(state, f[0]).TryGetValue(f[1], out current) || !(current is bool)) throw new Exception("Переключатель отсутствует в ответе Trula.");
            Put(patch, f, !(bool)current); return patch;
        }
        if(command=="volumeToggle") {
            int first=Level(cfg,"firstLevel",50),second=Level(cfg,"secondLevel",10);
            if(first==second)throw new Exception("Задайте два разных уровня громкости.");
            var selected=VolumeFields(cfg);int target=selected.All(f=>Volume(state,f)==first)?second:first;
            foreach(var field in selected)Put(patch,field,target);return patch;
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
    public static Dictionary<string,object> Preview(Dictionary<string,object> state,Dictionary<string,object> cfg,Dictionary<string,object> restore) {
        var result=Json.Decode(Json.Encode(state));
        var patch=Plan(state,cfg,Json.Decode(Json.Encode(restore)));
        foreach(var section in patch)foreach(var field in Json.Map(section.Value))Json.Child(result,section.Key)[field.Key]=field.Value;
        return result;
    }
    public static bool Audible(Dictionary<string,object> state,Dictionary<string,object> cfg) {
        var fields=VolumeFields(cfg);
        object playlist;
        if(Json.Text(state,"hasTrack")=="True" && state.TryGetValue("isPlaylistTrack",out playlist) && playlist is bool) {
            string field=(bool)playlist?"playlistVolume":"orderVolume";
            return fields.Where(f=>f[1]==field).Any(f=>Volume(state,f)>0);
        }
        return fields.Any(f=>Volume(state,f)>0);
    }
    public static string StepState(int[] volumes,string command) {
        if(volumes.All(v=>v==0))return "min";
        if(volumes.All(v=>v==100))return "max";
        return command=="volumeDown"?"down":"up";
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
    readonly Dictionary<string, string> artwork = new Dictionary<string, string>();
    int queuedClicks;
    Dictionary<string, object> global = new Dictionary<string, object>();
    bool globalsLoaded;
    string obsStatus="Связь с OBS ещё не проверена.";
    bool obsReady;
    readonly HashSet<string> uiRequests = new HashSet<string>();
    string uuid;
    ObsBridge bridge;
    readonly Dictionary<string,Dictionary<string,object>> confirmedStates=new Dictionary<string,Dictionary<string,object>>();
    readonly Dictionary<string,DateTime> confirmedTimes=new Dictionary<string,DateTime>();
    readonly Dictionary<string,string> connectionFailures=new Dictionary<string,string>();
    readonly Dictionary<string,DateTime> failureTimes=new Dictionary<string,DateTime>();
    void Remember(string token,Dictionary<string,object> state) {confirmedStates[token]=state;confirmedTimes[token]=DateTime.UtcNow;connectionFailures.Remove(token);failureTimes.Remove(token);}
    async Task PaintGroup(string token,Dictionary<string,object> state,bool error=false) {
        string shared=Credentials.Read();
        foreach(var key in keys.ToArray()) {
            if(Json.Text(key.Value,"token",shared)==token)await Artwork(key.Key,key.Value,state,error);
        }
    }
    async Task<Dictionary<string,object>> ReadObs(string token,bool force=false) {
        DateTime time;string failure;
        if(connectionFailures.TryGetValue(token,out failure) && failureTimes.TryGetValue(token,out time) && (DateTime.UtcNow-time).TotalSeconds<1)throw new Exception(failure);
        Dictionary<string,object> cached;
        if(!force && confirmedStates.TryGetValue(token,out cached) && confirmedTimes.TryGetValue(token,out time) && (DateTime.UtcNow-time).TotalMilliseconds<350)return cached;
        Exception unavailable=null;
        try {var state=await bridge.Read(token);Remember(token,state);obsReady=true;obsStatus="Trula подключена к OBS.";return state;}
        catch(Exception ex) {
            obsReady=false;obsStatus=ex.Message;confirmedStates.Remove(token);confirmedTimes.Remove(token);
            connectionFailures[token]=ex.Message;failureTimes[token]=DateTime.UtcNow;
            unavailable=ex;
        }
        await PaintGroup(token,null,true);throw unavailable;
    }
    [STAThread]
    static void Main(string[] args) {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        var plugin=new TrulaPlugin(); plugin.bridge=new ObsBridge();
        try { plugin.Run(args).GetAwaiter().GetResult(); } catch { Diagnostics.Write("connection closed"); }
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
    async Task Status(string context, string message) { await Send("sendToPropertyInspector",context,new {message,connected=!string.IsNullOrEmpty(Credentials.Read()),trulaReady=obsReady,twitchAccount=TwitchChat.AccountLabel(),twitchConnected=TwitchChat.IsConnected(),twitchRequestsAllowed=TwitchChat.RequestsAllowed(),requestReady=TwitchRequests.Ready,requestStatus=TwitchRequests.Status,rewards=TwitchRequests.Rewards,requestDetail=keys.ContainsKey(context) && Json.Text(keys[context],"command")=="acceptRequest"?TwitchRequests.Detail(Json.Text(keys[context],"rewardId")):""}); }
    async Task Artwork(string context,Dictionary<string,object> cfg,Dictionary<string,object> state,bool error=false) {
        string command=Json.Text(cfg,"command"), output=Json.Text(cfg,"output");
        if(command=="connect") {
            bool configured=!string.IsNullOrEmpty(Credentials.Read());string accountSignature="connect|"+configured+"|"+obsReady,accountPrior;
            if(!artwork.TryGetValue(context,out accountPrior) || accountPrior!=accountSignature) {
                await Send("setImage",context,new {image=KeyArtwork.Data("connect",configured,"",configured && !obsReady),target=0});
                artwork[context]=accountSignature;
            }
            return;
        }
        if(command=="sendTrackToChat" && !TwitchChat.IsConnected())error=true;
        if(command=="acceptRequest" && (!TwitchRequests.Ready || string.IsNullOrEmpty(Json.Text(cfg,"rewardId"))))error=true;
        string kind=command=="acceptRequest"?"request":command=="sendTrackToChat"?"chat":command=="skipOrder"?"skip":command=="nextTrack"?"next":command=="prevTrack"?"previous":command=="togglePlay"?"music":command=="mute" && output=="source"?"stream":command=="mute" && output=="panel"?"monitor":"control";
        bool active=!error; string caption="ГОТОВО";
        if(new[]{"volumeToggle","volumeUp","volumeDown"}.Contains(command)) kind=output=="panel"?"monitor-volume":"stream-volume";
        if(error) caption="НЕТ СВЯЗИ";
        else if(kind=="music") {active=Json.Text(state,"playing")=="True";caption=active?"ИГРАЕТ":"ПАУЗА";}
        else if(new[]{"stream","monitor","stream-volume","monitor-volume"}.Contains(kind)) {
            var volumes=Commands.VolumeFields(cfg).Select(f=>Convert.ToInt32(Json.Child(state,f[0])[f[1]])).ToArray();
            active=Commands.Audible(state,cfg);caption=!active?"БЕЗ ЗВУКА":volumes.Min()==volumes.Max()?volumes[0]+"%":volumes.Min()+"–"+volumes.Max()+"%";
            if(command=="volumeToggle") caption=volumes.All(v=>v==Commands.Level(cfg,"firstLevel",50))?"first":"alternate";
            if(command=="volumeUp" || command=="volumeDown") { kind=output=="panel"?"monitor-step":"stream-step"; caption=Commands.StepState(volumes,command); active=true; }
        }
        string signature=kind+"|"+active+"|"+error+"|"+caption;
        string prior; if(artwork.TryGetValue(context,out prior) && prior==signature) return;
        await Send("setImage",context,new {image=KeyArtwork.Data(kind,active,caption,error),target=0});
        artwork[context]=signature;
    }
    async Task Refresh(string context, Dictionary<string,object> cfg, bool execute) {
        if(Json.Text(cfg,"command")=="connect") {
            if(execute) ConnectionWindow.Open();
            bool configured=!string.IsNullOrEmpty(Credentials.Read());
            await Artwork(context,cfg,null);
            await Status(context,Commands.ConnectionMessage(configured,obsReady,obsStatus));return;
        }
        string failure = null,previewToken=null;
        var timing=System.Diagnostics.Stopwatch.StartNew();
        try {
            if(string.IsNullOrWhiteSpace(Json.Text(cfg,"token",Credentials.Read()))) throw new Exception("Сначала подключите Trula кнопкой «Подключить Trula».");
            string token = Commands.Token(Json.Text(cfg,"token", Credentials.Read()));
            string operation=Json.Text(cfg,"command");
            Dictionary<string,object> recent;DateTime observed;
            if(execute && globalsLoaded && new[]{"mute","volumeToggle","volumeUp","volumeDown"}.Contains(operation)
                && confirmedStates.TryGetValue(token,out recent) && confirmedTimes.TryGetValue(token,out observed) && (DateTime.UtcNow-observed).TotalSeconds<2) {
                Dictionary<string,object> predicted=null;
                try {predicted=Commands.Preview(recent,cfg,Json.Child(Json.Child(global,"restore"),Commands.Hash(token)));}catch { }
                if(predicted!=null) {previewToken=token;await PaintGroup(token,predicted);await Artwork(context,cfg,predicted);Diagnostics.Write("preview ms="+timing.ElapsedMilliseconds);}
            }
            var state = await ReadObs(token,execute);
            if (execute) {
                Diagnostics.Write("execute "+Json.Text(cfg,"command")+" output="+Json.Text(cfg,"output"));
                if (!globalsLoaded) throw new Exception("Загрузка настроек. Повторите нажатие.");
                string command = Json.Text(cfg,"command","mute");
                if(command=="acceptRequest") {
                    var candidate=TwitchRequests.Candidate(Json.Text(cfg,"rewardId"));
                    state=await bridge.Call(token,new {command="enqueue",patch=new {url=Json.Text(candidate,"url"),requestId=Json.Text(candidate,"id")}});
                    if(Json.Text(state,"queuedRequestId")!=Json.Text(candidate,"id"))throw new Exception("Добавление заявки в очередь не подтверждено.");
                    TwitchRequests.Complete(Json.Text(candidate,"id"));
                }
                else if(command=="sendTrackToChat") {await TwitchChat.Send(state,cfg);}
                else if (new[]{"togglePlay","nextTrack","prevTrack","skipOrder"}.Contains(command)) {
                    if (command=="togglePlay" && Json.Text(state,"hasTrack")=="False") throw new Exception("Очередь Trula пуста. Добавьте трек в панели.");
                    state = await bridge.Call(token,new {command});
                } else {
                string hash = Commands.Hash(token);
                var restores = Json.Child(global,"restore");
                var saved = Json.Child(restores,hash);
                var candidate = Json.Decode(Json.Encode(saved));
                var patch = Commands.Plan(state,cfg,candidate);
                // Save restoration data before silencing audio, including across app restarts.
                restores[hash] = candidate; global["restore"] = restores;
                await Send("setGlobalSettings",uuid,global);
                state = await bridge.Call(token,new {command="patch", patch});
                // Do not claim success when an API ignores a field.
                foreach (var section in patch) foreach (var field in Json.Map(section.Value)) {
                    object actual;
                    if (!Json.Child(state,section.Key).TryGetValue(field.Key,out actual) || !Equals(actual,field.Value)) throw new Exception("Trula не подтвердила изменение.");
                }
                }
                Remember(token,state);Diagnostics.Write("confirmed ms="+timing.ElapsedMilliseconds);
            }
            await Artwork(context,cfg,state);
            if(execute) foreach(var key in keys.ToArray()) {
                if(key.Key==context || Json.Text(key.Value,"command")=="connect")continue;
                if(Json.Text(key.Value,"token",Credentials.Read())==token)await Artwork(key.Key,key.Value,state);
            }
            await Status(context,execute && Json.Text(cfg,"command")=="sendTrackToChat"?"Трек отправлен в чат Twitch.":execute && Json.Text(cfg,"command")=="acceptRequest"?"Заявка добавлена в очередь следующих треков.":execute?"Готово — состояние обновлено в OBS.":"Подключено к Trula в OBS.");
        } catch (Exception ex) {
            bool chat=Json.Text(cfg,"command")=="sendTrackToChat";
            string message = ex is TaskCanceledException ? chat?"Twitch не подтвердил отправку. Проверьте чат перед повторным нажатием.":"Trula не ответила вовремя." : ex is HttpRequestException ? chat?"Нет соединения с Twitch.":"Нет соединения с Trula." : ex.Message;
            // Never show arbitrary HTTP content or credentials.
            if (ex is System.Web.HttpException || ex is ArgumentException || ex is InvalidOperationException) message = "Не удалось прочитать ответ Trula.";
            failure = message;
        }
        if (failure != null) {
            if(previewToken!=null) {
                Dictionary<string,object> rollback=null;
                try {rollback=await ReadObs(previewToken);}catch {confirmedStates.Remove(previewToken);confirmedTimes.Remove(previewToken);}
                await PaintGroup(previewToken,rollback,rollback==null);
            }
            await Artwork(context,cfg,null,true); await Status(context,failure);
        }
        if (failure != null && execute) Diagnostics.Write("failed "+failure);
    }
    async Task Event(Dictionary<string,object> m) {
        string ev = Json.Text(m,"event"), ctx = Json.Text(m,"context"); var payload = Json.Child(m,"payload");
        string actionId = Json.Text(m,"action");
        if (!string.IsNullOrEmpty(actionId)) actionIds[ctx] = actionId;
        if(ev=="didReceiveSettings") {
            var received=Json.Child(payload,"settings");var request=Json.Child(received,"uiRequest");
            if(request.Count>0) {
                received.Remove("uiRequest");await Send("setSettings",ctx,received);
                string id=Json.Text(request,"id"),command=Json.Text(request,"command");
                double created;double now=(DateTime.UtcNow-new DateTime(1970,1,1)).TotalMilliseconds;
                bool fresh;lock(uiRequests) {fresh=uiRequests.Add(id);if(uiRequests.Count>100) {uiRequests.Clear();uiRequests.Add(id);}}
                if(double.TryParse(Json.Text(request,"created"),out created) && Math.Abs(now-created)<30000 && fresh) {
                    if(command=="openConnection") {ConnectionWindow.Open();await Status(ctx,"Окно подключения открыто.");}
                    else if(command=="openTwitch") {TwitchWindow.Open();await Status(ctx,"Подтвердите подключение Twitch в браузере.");}
                    else if(command=="disconnectTwitch") {await TwitchChat.Disconnect();await Status(ctx,"Twitch отключён.");}
                    else if(command=="resetConnection") {
                        Credentials.Reset();artwork.Clear();
                        foreach(var key in keys.ToArray()) {
                            if(key.Value.Remove("token")) await Send("setSettings",key.Key,key.Value);
                            await Refresh(key.Key,key.Value,false);
                        }
                        await Status(ctx,"Подключение сброшено. Можно подключить Trula заново.");
                    }
                    else if(command=="startObs") {string note="OBS запускается.";try {note=await bridge.StartObs();}catch(Exception ex){note=ex.Message;}await Status(ctx,note);}
                    Diagnostics.Write("inspector "+command);
                }
            }
        }
        if(ev=="sendToPlugin" && payload.ContainsKey("openConnection")) {ConnectionWindow.Open();return;}
        if(ev=="propertyInspectorDidAppear") {
            if(keys.ContainsKey(ctx)) await Refresh(ctx,keys[ctx],false);
            return;
        }
        if (ev == "sendToPlugin" && payload.ContainsKey("saveConnection")) {
            bool saved = false;
            try { Credentials.Save(Json.Text(payload,"connection")); saved = true; } catch { }
            await Send("sendToPropertyInspector",ctx,new {message=saved?"Подключение сохранено для всех кнопок.":"Не удалось сохранить ссылку. Нужна ссылка панели trula.io с token.", saved});
            return;
        }
        if (ev == "sendToPlugin" && payload.ContainsKey("getConnection")) {
            string token = Credentials.Read();
            await Status(ctx,string.IsNullOrEmpty(token)?"Сначала подключите Trula кнопкой «Подключить Trula».":"Подключение Trula сохранено.");
            if(!string.IsNullOrEmpty(token) || actionId=="com.projectdeck.trula.connect") await Refresh(ctx,Commands.Configure(actionId,Json.Child(payload,"settings")),false);
            return;
        }
        if (ev=="sendToPlugin" && payload.ContainsKey("startObs")) {
            string message="OBS запущен. Дождитесь загрузки док-панели Trula.";
            try { message=await bridge.StartObs(); } catch(Exception ex) { message=ex.Message; }
            await Status(ctx,message); return;
        }
        var effective = Commands.Configure(actionId,Json.Child(payload,"settings"));
        if (payload.ContainsKey("settings")) payload["settings"] = effective;
        if (ev == "didReceiveGlobalSettings") { global = Json.Child(payload,"settings"); globalsLoaded = true; return; }
        if (ev == "willDisappear") { keys.Remove(ctx); actionIds.Remove(ctx); artwork.Remove(ctx); return; }
        if (ev == "willAppear" || ev == "didReceiveSettings") { keys[ctx] = Json.Child(payload,"settings"); await Refresh(ctx,keys[ctx],false); }
        if (ev == "sendToPlugin" && payload.ContainsKey("check")) { keys[ctx] = Json.Child(payload,"settings"); await Refresh(ctx,keys[ctx],false); }
        if (ev == "keyUp" || (ev=="sendToPlugin" && payload.ContainsKey("execute"))) {
            Diagnostics.Write("keyUp "+actionId);
            keys[ctx] = Json.Child(payload,"settings"); await Refresh(ctx,keys[ctx],true);
        }
    }
    async Task Poll() {
        while (socket.State == WebSocketState.Open) {
            await Task.Delay(500); if(Thread.VolatileRead(ref queuedClicks)>0 || !await gate.WaitAsync(0)) continue;
            try {
                foreach(var key in keys.ToArray().Where(k=>(Json.Text(k.Value,"command")=="connect"))) await Refresh(key.Key,key.Value,false);
                foreach(var group in keys.ToArray().Where(k=>!(Json.Text(k.Value,"command")=="connect")).GroupBy(k=>Json.Text(k.Value,"token",Credentials.Read()))) {
                    if(string.IsNullOrWhiteSpace(group.Key)) {obsReady=false;obsStatus="Подключите Trula в настройках аккаунтов.";await PaintGroup(group.Key,null,true);continue;}
                    Dictionary<string,object> state=null;
                    try {state=await ReadObs(Commands.Token(group.Key));} catch { }
                    foreach(var k in group) {await Artwork(k.Key,k.Value,state,state==null);await Status(k.Key,obsStatus);}
                }
            } finally { gate.Release(); }
        }
    }
    public async Task Run(string[] args) {
        var opts = new Dictionary<string,string>(); for (int i=0;i+1<args.Length;i+=2) opts[args[i].TrimStart('-')] = args[i+1];
        int port; if (!opts.ContainsKey("port") || !int.TryParse(opts["port"],out port) || port<1 || port>65535) return;
        uuid = opts["pluginUUID"];
        await socket.ConnectAsync(new Uri("ws://127.0.0.1:"+port),CancellationToken.None);
        await Raw(new { @event=opts["registerEvent"], uuid });
        await Send("getGlobalSettings",uuid,new {});
        TwitchRequests.EnsureStarted();
        var poll = Poll();
        var buffer = new byte[8192];
        while (socket.State == WebSocketState.Open) {
            using (var ms = new MemoryStream()) {
                WebSocketReceiveResult r;
                do { r = await socket.ReceiveAsync(new ArraySegment<byte>(buffer),CancellationToken.None); if(r.MessageType==WebSocketMessageType.Close) return; ms.Write(buffer,0,r.Count); if(ms.Length>1024*1024) return; } while(!r.EndOfMessage);
                var m = Json.Decode(Encoding.UTF8.GetString(ms.ToArray()));
                var ignored = Dispatch(m);
            }
        }
        await poll;
    }
    async Task Dispatch(Dictionary<string,object> message) {
        string ev=Json.Text(message,"event"),context=Json.Text(message,"context");
        // Opening account UI must never wait behind failed OBS requests.
        var received=Json.Child(Json.Child(message,"payload"),"settings");
        var ui=Json.Child(received,"uiRequest");
        if(ev=="didReceiveSettings" && new[]{"openConnection","openTwitch","startObs"}.Contains(Json.Text(ui,"command"))) {
            string id=Json.Text(ui,"id");bool fresh;lock(uiRequests) {fresh=uiRequests.Add(id);}
            received.Remove("uiRequest");
            try {
                await Send("setSettings",context,received);
                double created,now=(DateTime.UtcNow-new DateTime(1970,1,1)).TotalMilliseconds;
                if(fresh && double.TryParse(Json.Text(ui,"created"),out created) && Math.Abs(now-created)<30000) {
                    string note="Окно подключения открыто.";
                    if(Json.Text(ui,"command")=="startObs") {
                        try {note=await bridge.StartObs();}catch(Exception ex){note=ex.Message;}
                        await Send("sendToPropertyInspector",context,new {obsLaunchMessage=note});
                    }else if(Json.Text(ui,"command")=="openTwitch") TwitchWindow.Open();else ConnectionWindow.Open();
                    await Status(context,note);Diagnostics.Write("inspector window requested");
                }
            }catch {Diagnostics.Write("account UI failed");}
            return;
        }
        bool click=ev=="keyUp" || (ev=="sendToPlugin" && Json.Child(message,"payload").ContainsKey("execute"));
        if(click)Interlocked.Increment(ref queuedClicks);
        try { await gate.WaitAsync(); try {await Event(message);} finally {gate.Release();} }
        catch {Diagnostics.Write("event failed");}
        finally {if(click)Interlocked.Decrement(ref queuedClicks);}
    }
}
