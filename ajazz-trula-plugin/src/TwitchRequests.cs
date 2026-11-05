using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public static class TrackLink {
    public static string Canonical(string value) {
        Uri url;if(!Uri.TryCreate(value,UriKind.Absolute,out url) || url.Scheme!="https" || !string.IsNullOrEmpty(url.UserInfo) || !url.IsDefaultPort)throw new Exception("Нужна HTTPS-ссылка YouTube или VK Видео.");
        string host=url.Host.ToLowerInvariant(),id="";
        if(host=="youtu.be")id=url.AbsolutePath.Trim('/');
        else if(new[]{"youtube.com","www.youtube.com","m.youtube.com","music.youtube.com"}.Contains(host)) {
            if(url.AbsolutePath=="/watch")id=System.Web.HttpUtility.ParseQueryString(url.Query)["v"]??"";
            else {var match=Regex.Match(url.AbsolutePath,"^/(?:shorts|live)/([A-Za-z0-9_-]{11})/?$");if(match.Success)id=match.Groups[1].Value;}
        }
        if(Regex.IsMatch(id,"^[A-Za-z0-9_-]{11}$"))return "https://youtu.be/"+id;
        if(new[]{"vk.com","www.vk.com","vkvideo.ru","www.vkvideo.ru"}.Contains(host)) {
            var match=Regex.Match(url.AbsolutePath,"^/video(-?[0-9]+_[0-9]+)/?$");if(match.Success)return "https://vk.com/video"+match.Groups[1].Value;
        }
        throw new Exception("В заявке нет поддерживаемой ссылки YouTube или VK Видео.");
    }
    public static string Extract(string text) {
        var links=new HashSet<string>();
        foreach(Match match in Regex.Matches(text??"",@"https://[^\s<>""']+")) {
            try {links.Add(Canonical(match.Value.TrimEnd('.',',',';',')',']','}','!')));}catch { }
        }
        if(links.Count!=1)throw new Exception(links.Count==0?"В последней заявке нет поддерживаемой ссылки.":"В последней заявке несколько ссылок. Нужна одна ссылка на трек.");
        return links.First();
    }
}

public sealed class RequestBook {
    readonly Dictionary<string,Dictionary<string,object>> latest=new Dictionary<string,Dictionary<string,object>>();
    readonly HashSet<string> handled=new HashSet<string>();
    readonly object sync=new object();
    public void Clear() {lock(sync){latest.Clear();handled.Clear();}}
    public void Receive(Dictionary<string,object> item,string owner) {
        if(Json.Text(item,"broadcaster_user_id")!=owner)return;
        string id=Json.Text(item,"id"),reward=Json.Text(Json.Child(item,"reward"),"id");DateTime time;
        if(!Regex.IsMatch(id,"^[A-Za-z0-9-]{1,100}$") || string.IsNullOrEmpty(reward) || !DateTime.TryParse(Json.Text(item,"redeemed_at"),null,System.Globalization.DateTimeStyles.RoundtripKind,out time))return;
        time=time.ToUniversalTime();string link="",error="";try{link=TrackLink.Extract(Json.Text(item,"user_input"));}catch(Exception ex){error=ex.Message;}
        var candidate=new Dictionary<string,object>{{"id",id},{"rewardId",reward},{"url",link},{"error",error},{"time",time.Ticks},{"user",Json.Text(item,"user_name")}};
        lock(sync) {Dictionary<string,object> previous;if(latest.TryGetValue(reward,out previous) && Convert.ToInt64(previous["time"])>=time.Ticks)return;latest[reward]=candidate;}
    }
    public Dictionary<string,object> Candidate(string reward,DateTime now) {
        lock(sync) {
            Dictionary<string,object> item;if(string.IsNullOrEmpty(reward))throw new Exception("Выберите музыкальную награду в настройках кнопки.");
            if(!latest.TryGetValue(reward,out item))throw new Exception("Новых заявок этой награды пока нет. Ожидаю покупку за баллы канала.");
            if(now.ToUniversalTime().Ticks-Convert.ToInt64(item["time"])>TimeSpan.FromMinutes(30).Ticks)throw new Exception("Последняя заявка старше 30 минут. Ожидаю новую.");
            if(handled.Contains(Json.Text(item,"id")))throw new Exception("Эта заявка уже добавлена в очередь.");
            if(!string.IsNullOrEmpty(Json.Text(item,"error")))throw new Exception(Json.Text(item,"error"));
            return Json.Decode(Json.Encode(item));
        }
    }
    public void Complete(string id) {lock(sync){if(handled.Count>=1000)handled.Clear();handled.Add(id);}}
}

public static class TwitchRequests {
    const string EventType="channel.channel_points_custom_reward_redemption.add";
    static readonly RequestBook book=new RequestBook();
    static readonly object sync=new object();
    static object[] rewards=new object[0];
    static string owner="",status="Подключите Twitch с доступом к заявкам за баллы.",connectedOwner="";
    static CancellationTokenSource session;
    static int started;
    public static bool Ready {get{lock(sync)return !string.IsNullOrEmpty(connectedOwner);}}
    public static string Status {get{lock(sync)return status;}}
    public static object[] Rewards {get{lock(sync)return rewards.ToArray();}}
    static void SetStatus(string text) {lock(sync)status=text;}
    public static void Reset() {lock(sync){if(session!=null)session.Cancel();owner="";connectedOwner="";rewards=new object[0];book.Clear();status="Twitch отключён.";}}
    public static void EnsureStarted() {if(Interlocked.Exchange(ref started,1)==0){var ignored=Run();}}
    public static Dictionary<string,object> Candidate(string reward) {lock(sync){if(string.IsNullOrEmpty(connectedOwner))throw new Exception(status);return book.Candidate(reward,DateTime.UtcNow);}}
    public static void Complete(string id) {book.Complete(id);}
    public static string Detail(string reward) {if(!Ready || string.IsNullOrEmpty(reward))return "";try {var item=Candidate(reward);return "Последняя заявка: "+Json.Text(item,"url")+" — "+Json.Text(item,"user");}catch(Exception ex){return ex.Message;}}
    public static bool ValidEndpoint(string address) {Uri uri;return Uri.TryCreate(address,UriKind.Absolute,out uri) && uri.Scheme=="wss" && uri.Host=="eventsub.wss.twitch.tv" && uri.IsDefaultPort && uri.AbsolutePath=="/ws" && string.IsNullOrEmpty(uri.UserInfo);}
    static async Task<Dictionary<string,object>> Receive(ClientWebSocket socket,int seconds,CancellationToken cancellation) {
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation))using(var output=new MemoryStream()) {
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));var buffer=new byte[8192];WebSocketReceiveResult part;
            do {part=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),timeout.Token);if(part.MessageType==WebSocketMessageType.Close)throw new Exception("Соединение с заявками Twitch закрыто.");output.Write(buffer,0,part.Count);if(output.Length>1024*1024)throw new Exception("Неожиданный ответ Twitch.");}while(!part.EndOfMessage);
            return Json.Decode(Encoding.UTF8.GetString(output.ToArray()));
        }
    }
    static async Task Subscribe(Dictionary<string,object> account,string sessionId,CancellationToken cancellation) {
        using(var response=await TwitchChat.Helix(account,HttpMethod.Post,"eventsub/subscriptions",new {type=EventType,version="1",condition=new {broadcaster_user_id=Json.Text(account,"user_id")},transport=new {method="websocket",session_id=sessionId}},cancellation)) {
            if(!response.IsSuccessStatusCode)throw new Exception("Twitch не разрешил получение заявок. Переподключите Twitch в аккаунтах.");
        }
    }
    static async Task Listen(Dictionary<string,object> account,CancellationToken cancellation) {
        string address="wss://eventsub.wss.twitch.tv/ws",user=Json.Text(account,"user_id");bool transferred=false;
        ClientWebSocket current=null;
        try {
            while(true) {
                cancellation.ThrowIfCancellationRequested();if(!ValidEndpoint(address))throw new Exception("Неожиданный адрес EventSub Twitch.");
                var socket=new ClientWebSocket();
                try {
                    using(var connect=CancellationTokenSource.CreateLinkedTokenSource(cancellation)){connect.CancelAfter(15000);await socket.ConnectAsync(new Uri(address),connect.Token);}
                    var welcome=await Receive(socket,15,cancellation);
                    if(Json.Text(Json.Child(welcome,"metadata"),"message_type")!="session_welcome")throw new Exception("Twitch не подтвердил подключение заявок.");
                    var details=Json.Child(Json.Child(welcome,"payload"),"session");
                    if(!transferred)await Subscribe(account,Json.Text(details,"id"),cancellation);
                    if(current!=null)current.Dispose();current=socket;
                    int keepalive=40;object timeoutValue;if(details.TryGetValue("keepalive_timeout_seconds",out timeoutValue) && timeoutValue!=null)keepalive=Math.Max(15,Convert.ToInt32(timeoutValue)+5);
                    lock(sync){connectedOwner=user;status="Заявки Twitch подключены";}
                    while(true) {
                        var message=await Receive(socket,keepalive,cancellation);string type=Json.Text(Json.Child(message,"metadata"),"message_type");
                        if(type=="notification") {
                            var payload=Json.Child(message,"payload");if(Json.Text(Json.Child(payload,"subscription"),"type")==EventType){lock(sync){if(owner==user && !cancellation.IsCancellationRequested)book.Receive(Json.Child(payload,"event"),user);}}
                        } else if(type=="session_reconnect") {
                            address=Json.Text(Json.Child(Json.Child(message,"payload"),"session"),"reconnect_url");
                            if(!ValidEndpoint(address))throw new Exception("Неожиданный адрес переподключения Twitch.");
                            // Keep the old connection alive until the transferred session welcomes us.
                            transferred=true;break;
                        } else if(type=="revocation")throw new Exception("Twitch отозвал доступ к заявкам. Подключите аккаунт заново.");
                    }
                    // The old socket remains alive until the new session welcome.
                }catch {if(socket!=current)socket.Dispose();throw;}
            }
        }finally {if(current!=null)current.Dispose();lock(sync)connectedOwner="";}
    }
    static async Task Run() {
        while(true) {
            try {
                if(!TwitchChat.IsConnected()){SetStatus("Подключите Twitch в аккаунтах.");await Task.Delay(2000);continue;}
                var account=await TwitchChat.Account();
                if(!TwitchChat.HasScope(account,"channel:read:redemptions")){SetStatus("Переподключите Twitch в аккаунтах: нужен доступ к заявкам за баллы.");await Task.Delay(5000);continue;}
                string user=Json.Text(account,"user_id");
                lock(sync){if(owner!=user){book.Clear();rewards=new object[0];owner=user;status="Подключение к заявкам Twitch…";}session=new CancellationTokenSource();}
                var cancellation=session.Token;
                using(var response=await TwitchChat.Helix(account,HttpMethod.Get,"channel_points/custom_rewards?broadcaster_id="+Uri.EscapeDataString(user),null,cancellation)) {
                    if(!response.IsSuccessStatusCode)throw new Exception("Не удалось получить награды Twitch. На канале должны быть доступны баллы канала.");
                    var data=Json.Decode(await response.Content.ReadAsStringAsync());object raw;var list=data.TryGetValue("data",out raw)?Json.Array(raw):new object[0];
                    lock(sync)rewards=list.Select(x=>Json.Map(x)).Where(x=>Json.Text(x,"is_user_input_required")=="True").Select(x=>(object)new {id=Json.Text(x,"id"),title=Json.Text(x,"title")}).ToArray();
                }
                await Listen(account,cancellation);
            } catch(OperationCanceledException){if(!TwitchChat.IsConnected())SetStatus("Подключите Twitch в аккаунтах.");else SetStatus("Связь с заявками Twitch прервалась. Повторяю подключение…");}
            catch(Exception ex){SetStatus(ex is HttpRequestException || ex is WebSocketException?"Нет связи с заявками Twitch. Переподключаюсь…":ex.Message);}
            finally {lock(sync){connectedOwner="";if(session!=null){session.Dispose();session=null;}}}
            await Task.Delay(3000);
        }
    }
}
