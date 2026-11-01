using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// The real HTTPS origin is retained, including YouTube's normal client identity.
public sealed class WebViewBridge : Form {
    readonly WebView2 browser = new WebView2();
    readonly HttpClient http = new HttpClient {Timeout=TimeSpan.FromSeconds(20)};
    readonly Dictionary<string,TaskCompletionSource<bool>> jobs = new Dictionary<string,TaskCompletionSource<bool>>();
    Dictionary<string,object> state;
    DateTime updated;
    string token, navigatedToken;
    bool ready;
    string failure;
    public WebViewBridge() {
        Text="Trula — плеер AJAZZ"; Width=1000; Height=760;
        browser.Dock=DockStyle.Fill; Controls.Add(browser);
        Shown += async (s,e) => await Initialize();
        FormClosing += (s,e) => { if(e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; Hide(); } };
    }
    async Task Initialize() {
        try {
            var environment = await CoreWebView2Environment.CreateAsync(null,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","webview"));
            await browser.EnsureCoreWebView2Async(environment);
            browser.CoreWebView2.Settings.AreDevToolsEnabled=false;
            browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled=false;
            browser.CoreWebView2.Settings.IsGeneralAutofillEnabled=false;
            browser.CoreWebView2.AddWebResourceRequestedFilter("https://trula.io/cp/assets/index-*.js",CoreWebView2WebResourceContext.Script);
            browser.CoreWebView2.WebResourceRequested += PatchModule;
            browser.CoreWebView2.WebMessageReceived += Message;
            ready=true;
            if(!string.IsNullOrEmpty(token)) Navigate();
        } catch { failure="Не удалось запустить WebView2. Установите Microsoft Edge WebView2 Runtime."; }
    }
    async void PatchModule(object sender, CoreWebView2WebResourceRequestedEventArgs e) {
        var deferral=e.GetDeferral();
        try {
            string code=await http.GetStringAsync(e.Request.Uri);
            const string marker="A.jsx(L0e,{ref:s,children:e})";
            if(!code.Contains(marker) || !code.Contains(",of=()=>") || !code.Contains(",_v=()=>") || !code.Contains(",Wa=()=>") || !code.Contains(",zo=()=>")) throw new Exception();
            code=code.Replace(marker,"A.jsx(L0e,{ref:s,children:A.jsxs(A.Fragment,{children:[e,A.jsx(ProjectDeckBridge,{})]})})");
            code+="\n"+File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bridge.js"));
            var bytes=System.Text.Encoding.UTF8.GetBytes(code);
            e.Response=browser.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(bytes),200,"OK","Content-Type: text/javascript; charset=utf-8\r\nCache-Control: no-store\r\n");
        } catch {
            failure="Панель Trula изменилась или недоступна. Требуется обновление адаптера.";
            e.Response=browser.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(),503,"Unavailable","Content-Type: text/javascript\r\n");
        } finally { deferral.Complete(); }
    }
    void Message(object sender, CoreWebView2WebMessageReceivedEventArgs e) {
        var origin=new Uri(e.Source);
        if(origin.Scheme!="https" || origin.Host!="trula.io" || !origin.AbsolutePath.StartsWith("/cp/")) return;
        var sourceToken=System.Web.HttpUtility.ParseQueryString(origin.Query)["token"];
        if(sourceToken!=navigatedToken) return;
        try {
            var message=Json.Decode(e.WebMessageAsJson);
            var snapshot=Json.Child(message,"state");
            if(snapshot.ContainsKey("panel") && snapshot.ContainsKey("source")) { state=snapshot; updated=DateTime.UtcNow; failure=null; }
            TaskCompletionSource<bool> job;
            if(Json.Text(message,"type")=="result" && jobs.TryGetValue(Json.Text(message,"id"),out job)) job.TrySetResult(Json.Text(message,"error")=="False");
        } catch { failure="Не удалось прочитать состояние плеера Trula."; }
    }
    void Navigate() {
        if(!ready || navigatedToken==token) return;
        navigatedToken=token; state=null; failure=null;
        browser.CoreWebView2.Navigate("https://trula.io/cp/?token="+Uri.EscapeDataString(token));
    }
    public void Start() { }
    public void Allow(string value) { if(token!=value) { token=value; Navigate(); } }
    public Dictionary<string,object> Read(string value) {
        if(failure!=null) throw new Exception(failure);
        if(token!=value || state==null || (DateTime.UtcNow-updated).TotalSeconds>5) throw new Exception("Плеер Trula загружается. Нажмите «Открыть плеер» в настройках кнопки.");
        return state;
    }
    public async Task Call(string value, object command) {
        Read(value);
        string id=Guid.NewGuid().ToString("N");
        var data=Json.Decode(Json.Encode(command)); data["id"]=id;
        var job=new TaskCompletionSource<bool>(); jobs[id]=job;
        try {
            browser.CoreWebView2.PostWebMessageAsJson(Json.Encode(data));
            if(await Task.WhenAny(job.Task,Task.Delay(8000))!=job.Task) throw new Exception("Плеер Trula не ответил вовремя.");
            if(!await job.Task) throw new Exception("Trula не смогла выполнить команду.");
        } finally { jobs.Remove(id); }
    }
    public void OpenPlayer() { Show(); WindowState=FormWindowState.Normal; Activate(); }
}
