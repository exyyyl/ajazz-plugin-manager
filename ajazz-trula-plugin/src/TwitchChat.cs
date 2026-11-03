using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class TwitchChat {
    static readonly SemaphoreSlim authGate=new SemaphoreSlim(1),sendGate=new SemaphoreSlim(1);
    static readonly byte[] entropy=Encoding.UTF8.GetBytes("AjazzTrula.Twitch.v1");
    static string PathName {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","twitch.bin");}}
    static string ClientId {get{return Json.Text(Json.Decode(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"twitch-config.json"))),"clientId");}}
    static readonly HttpClient http=new HttpClient {Timeout=TimeSpan.FromSeconds(15)};
    static readonly Dictionary<string,DateTime> sent=new Dictionary<string,DateTime>();
    static string checkedToken;
    static int loginVersion;
    static Dictionary<string,object> Load() {
        if(!File.Exists(PathName)) throw new Exception("Подключите Twitch в «Аккаунты Trula и Twitch».");
        byte[] clear=ProtectedData.Unprotect(File.ReadAllBytes(PathName),entropy,DataProtectionScope.CurrentUser);
        try {return Json.Decode(Encoding.UTF8.GetString(clear));}finally{Array.Clear(clear,0,clear.Length);}
    }
    static void Save(Dictionary<string,object> account) {
        byte[] clear=Encoding.UTF8.GetBytes(Json.Encode(account));
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName));
            File.WriteAllBytes(PathName+".tmp",ProtectedData.Protect(clear,entropy,DataProtectionScope.CurrentUser));
            if(File.Exists(PathName)) File.Replace(PathName+".tmp",PathName,null);else File.Move(PathName+".tmp",PathName);
        }finally {Array.Clear(clear,0,clear.Length);}
    }
    public static string AccountLabel() {try{return "Twitch: "+Json.Text(Load(),"login");}catch{return "Twitch не подключён.";}}
    public static bool IsConnected() {try {return !string.IsNullOrEmpty(Json.Text(Load(),"access_token"));}catch{return false;}}
    public static void ValidateIdentity(Dictionary<string,object> identity,string expectedClient) {
        if(Json.Text(identity,"client_id")!=expectedClient)throw new Exception("Twitch вернул вход для другого приложения. Подключите Twitch заново.");
        object raw;var scopes=identity.TryGetValue("scopes",out raw)?Json.Array(raw):new object[0];
        if(!scopes.Any(s=>Convert.ToString(s)=="user:write:chat"))throw new Exception("Подключите Twitch с разрешением отправлять сообщения.");
        if(string.IsNullOrEmpty(Json.Text(identity,"user_id")) || string.IsNullOrEmpty(Json.Text(identity,"login")))throw new Exception("Twitch не передал данные аккаунта.");
    }
    public static bool DeliveryConfirmed(Dictionary<string,object> response) {
        object raw;var data=response.TryGetValue("data",out raw)?Json.Array(raw):new object[0];
        return data.Length==1 && Json.Text(Json.Map(data[0]),"is_sent")=="True";
    }
    public static async Task Disconnect() {
        Interlocked.Increment(ref loginVersion);TwitchWindow.Cancel();
        await authGate.WaitAsync();
        try {if(File.Exists(PathName))File.Delete(PathName);if(File.Exists(PathName+".tmp"))File.Delete(PathName+".tmp");checkedToken=null;}
        finally{authGate.Release();}
    }
    static async Task<HttpResponseMessage> Form(string endpoint,Dictionary<string,string> data,CancellationToken cancellation) {
        using(var body=new FormUrlEncodedContent(data))return await http.PostAsync(endpoint,body,cancellation);
    }
    public static async Task<Dictionary<string,object>> Begin(CancellationToken cancellation) {
        using(var response=await Form("https://id.twitch.tv/oauth2/device",new Dictionary<string,string>{{"client_id",ClientId},{"scopes","user:write:chat"}},cancellation)) {
            if(!response.IsSuccessStatusCode)throw new Exception("Не удалось начать вход Twitch. Повторите позже.");
            return Json.Decode(await response.Content.ReadAsStringAsync());
        }
    }
    static async Task Validate(Dictionary<string,object> account,CancellationToken cancellation) {
        using(var request=new HttpRequestMessage(HttpMethod.Get,"https://id.twitch.tv/oauth2/validate")) {
            request.Headers.Authorization=new AuthenticationHeaderValue("OAuth",Json.Text(account,"access_token"));
            using(var response=await http.SendAsync(request,cancellation)) {
                if(!response.IsSuccessStatusCode) throw new Exception("Twitch требует повторного входа.");
                var value=Json.Decode(await response.Content.ReadAsStringAsync());
                ValidateIdentity(value,ClientId);
                account["user_id"]=Json.Text(value,"user_id");account["login"]=Json.Text(value,"login");
                account["expiresAt"]=DateTime.UtcNow.AddSeconds(Convert.ToDouble(value["expires_in"])).Ticks;
                account["validatedAt"]=DateTime.UtcNow.Ticks;
            }
        }
    }
    public static async Task Complete(Dictionary<string,object> device,CancellationToken cancellation) {
        int version=Thread.VolatileRead(ref loginVersion);
        int interval=Math.Max(1,Convert.ToInt32(device["interval"]));
        DateTime until=DateTime.UtcNow.AddSeconds(Convert.ToDouble(device["expires_in"]));
        while(DateTime.UtcNow<until) {
            await Task.Delay(TimeSpan.FromSeconds(interval),cancellation);
            using(var response=await Form("https://id.twitch.tv/oauth2/token",new Dictionary<string,string>{{"client_id",ClientId},{"device_code",Json.Text(device,"device_code")},{"grant_type","urn:ietf:params:oauth:grant-type:device_code"}},cancellation)) {
                var result=Json.Decode(await response.Content.ReadAsStringAsync());
                if(response.IsSuccessStatusCode) {
                    await Validate(result,cancellation);await authGate.WaitAsync(cancellation);
                    try {cancellation.ThrowIfCancellationRequested();if(version!=Thread.VolatileRead(ref loginVersion))throw new OperationCanceledException();Save(result);}finally{authGate.Release();}return;
                }
                string error=Json.Text(result,"message",Json.Text(result,"error"));
                if(error=="authorization_pending")continue;
                if(error=="slow_down") {interval+=5;continue;}
                throw new Exception("Вход Twitch отменён или код устарел. Начните подключение заново.");
            }
        }
        throw new Exception("Код Twitch устарел. Начните подключение заново.");
    }
    static async Task<Dictionary<string,object>> Account(bool force=false) {
        await authGate.WaitAsync();
        try {
            var account=Load();long expires=Convert.ToInt64(account["expiresAt"]),validated=Convert.ToInt64(account["validatedAt"]);
            if(force || expires<DateTime.UtcNow.AddMinutes(2).Ticks) {
                using(var response=await Form("https://id.twitch.tv/oauth2/token",new Dictionary<string,string>{{"client_id",ClientId},{"grant_type","refresh_token"},{"refresh_token",Json.Text(account,"refresh_token")}},CancellationToken.None)) {
                    if(!response.IsSuccessStatusCode)throw new Exception("Подключите Twitch заново: разрешение истекло или отозвано.");
                    var fresh=Json.Decode(await response.Content.ReadAsStringAsync());
                    foreach(var field in fresh)account[field.Key]=field.Value;
                    // DCF refresh tokens rotate: persist the replacement immediately.
                    account["expiresAt"]=DateTime.UtcNow.AddSeconds(Convert.ToDouble(fresh["expires_in"])).Ticks;
                    account["validatedAt"]=0L;Save(account);
                }
                await Validate(account,CancellationToken.None);Save(account);checkedToken=Json.Text(account,"access_token");
            } else if(checkedToken!=Json.Text(account,"access_token") || validated<DateTime.UtcNow.AddHours(-1).Ticks) {await Validate(account,CancellationToken.None);Save(account);checkedToken=Json.Text(account,"access_token");}
            return account;
        }finally {authGate.Release();}
    }
    static async Task<HttpResponseMessage> Helix(Dictionary<string,object> account,HttpMethod method,string path,object body=null) {
        using(var request=new HttpRequestMessage(method,"https://api.twitch.tv/helix/"+path)) {
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Json.Text(account,"access_token"));request.Headers.Add("Client-Id",ClientId);
            if(body!=null)request.Content=new StringContent(Json.Encode(body),Encoding.UTF8,"application/json");
            return await http.SendAsync(request);
        }
    }
    public static Task Send(Dictionary<string,object> state,Dictionary<string,object> cfg) {
        return SendText(TrackMessage.Format(state,Json.Text(cfg,"messageFormat","both")));
    }
    public static async Task SendText(string message) {
        if(string.IsNullOrWhiteSpace(message) || message.Length>500 || message.Contains("\r") || message.Contains("\n"))throw new Exception("Некорректное сообщение Twitch.");
        await sendGate.WaitAsync();
        try {
            var account=await Account();string broadcaster=Json.Text(account,"user_id");
            string key=Json.Text(account,"user_id")+":"+broadcaster;DateTime last;
            if(sent.TryGetValue(key,out last) && (DateTime.UtcNow-last).TotalSeconds<1)throw new Exception("Подождите секунду перед следующим сообщением.");
            sent[key]=DateTime.UtcNow;
            var body=new {broadcaster_id=broadcaster,sender_id=Json.Text(account,"user_id"),message};
            var responseMessage=await Helix(account,HttpMethod.Post,"chat/messages",body);
            if(responseMessage.StatusCode==HttpStatusCode.Unauthorized) {responseMessage.Dispose();account=await Account(true);responseMessage=await Helix(account,HttpMethod.Post,"chat/messages",body);}
            using(responseMessage) {
                if(!responseMessage.IsSuccessStatusCode)throw new Exception("Twitch отклонил отправку. Проверьте разрешения и ограничения чата.");
                if(!DeliveryConfirmed(Json.Decode(await responseMessage.Content.ReadAsStringAsync())))throw new Exception("Twitch не отправил сообщение: проверьте ограничения чата.");
                sent[key]=DateTime.UtcNow;
            }
        }finally {sendGate.Release();}
    }
}
