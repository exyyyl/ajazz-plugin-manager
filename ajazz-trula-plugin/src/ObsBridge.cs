using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// No browser is created here. The existing OBS dock is the only player.
public sealed class ObsBridge {
    public const int Port = 17936;
    readonly HttpClient local = new HttpClient(new HttpClientHandler {UseProxy=false,AllowAutoRedirect=false}) {Timeout=TimeSpan.FromSeconds(3)};
    readonly string script = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"obs-player.js"));
    public void StartObs() {
        if (Process.GetProcessesByName("obs64").Length>0) throw new Exception("OBS уже запущен. Для подключения закройте его и запустите кнопкой «Запустить OBS».");
        string exe=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"obs-studio","bin","64bit","obs64.exe");
        if(!File.Exists(exe)) throw new Exception("OBS не найден в стандартной папке установки.");
        Process.Start(new ProcessStartInfo(exe,"--remote-debugging-port="+Port+" --remote-debugging-address=127.0.0.1") {WorkingDirectory=Path.GetDirectoryName(exe),WindowStyle=ProcessWindowStyle.Hidden});
    }
    async Task<Dictionary<string,object>> Player(string token,string command,object patch=null) {
        string listing;
        try { listing=await local.GetStringAsync("http://127.0.0.1:"+Port+"/json/list"); }
        catch { throw new Exception("Нет подключения к OBS. Закройте OBS и запустите кнопкой «Запустить OBS» в настройках Trula."); }
        var pages=new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(listing) as object[];
        var matches=new List<Dictionary<string,object>>();
        foreach(var page in pages??new object[0]) {
            var item=Json.Map(page); Uri url;
            if(Json.Text(item,"type")!="page" || !Uri.TryCreate(Json.Text(item,"url"),UriKind.Absolute,out url)) continue;
            if(url.Scheme=="https" && url.Host=="trula.io" && url.AbsolutePath.StartsWith("/cp/") && System.Web.HttpUtility.ParseQueryString(url.Query)["token"]==token) matches.Add(item);
        }
        if(matches.Count!=1) throw new Exception(matches.Count==0 ? "В OBS не найдена док-панель Trula с сохранённой ссылкой панели." : "В OBS открыто несколько панелей Trula. Оставьте один плеер.");
        Uri endpoint;
        if(!Uri.TryCreate(Json.Text(matches[0],"webSocketDebuggerUrl"),UriKind.Absolute,out endpoint) || endpoint.Scheme!="ws" || !endpoint.IsLoopback || endpoint.Port!=Port) throw new Exception("Некорректный адрес подключения OBS.");
        using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8)))
        using(var socket=new ClientWebSocket()) {
            socket.Options.Proxy=null;
            await socket.ConnectAsync(endpoint,timeout.Token);
            string expression="("+script+")("+Json.Encode(token)+","+Json.Encode(command)+","+Json.Encode(patch)+")";
            byte[] data=Encoding.UTF8.GetBytes(Json.Encode(new {id=1,method="Runtime.evaluate",@params=new {expression,awaitPromise=true,returnByValue=true}}));
            await socket.SendAsync(new ArraySegment<byte>(data),WebSocketMessageType.Text,true,timeout.Token);
            var buffer=new byte[8192];
            while(true) using(var output=new MemoryStream()) {
                WebSocketReceiveResult chunk;
                do {
                    chunk=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),timeout.Token);
                    if(chunk.MessageType==WebSocketMessageType.Close) throw new Exception("Соединение с панелью OBS закрыто.");
                    output.Write(buffer,0,chunk.Count);
                    if(output.Length>2*1024*1024) throw new Exception("Слишком большой ответ OBS.");
                } while(!chunk.EndOfMessage);
                var message=Json.Decode(Encoding.UTF8.GetString(output.ToArray()));
                if(Json.Text(message,"id")!="1") continue;
                var result=Json.Child(message,"result");
                if(message.ContainsKey("error") || result.ContainsKey("exceptionDetails")) throw new Exception("Панель Trula изменилась или ещё загружается. Обновите панель OBS и повторите.");
                var value=Json.Child(Json.Child(result,"result"),"value");
                if(!value.ContainsKey("playing")) throw new Exception("Плеер Trula в OBS пока недоступен.");
                return value;
            }
        }
    }
    public Task<Dictionary<string,object>> Read(string token) { return Player(token,"read"); }
    public async Task<Dictionary<string,object>> Call(string token,object command) {
        var item=Json.Decode(Json.Encode(command));
        if(Json.Text(item,"command")=="patch") return await Player(token,"patch",item["patch"]);
        return await Player(token,Json.Text(item,"command"));
    }
}
