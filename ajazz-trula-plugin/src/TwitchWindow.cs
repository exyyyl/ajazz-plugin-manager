using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

public static class TwitchWindow {
    static int showing;
    static Form current;
    public static void Cancel() {var window=current;if(window!=null && !window.IsDisposed && window.IsHandleCreated)try{window.BeginInvoke((Action)(()=>window.Close()));}catch(InvalidOperationException){}}
    static void Foreground(Form window) {if(window.IsDisposed)return;window.Show();window.WindowState=FormWindowState.Normal;window.TopMost=true;window.Activate();window.TopMost=false;}
    public static void Open() {
        if(Interlocked.Exchange(ref showing,1)!=0){var existing=current;if(existing!=null && !existing.IsDisposed && existing.IsHandleCreated)try{existing.BeginInvoke((Action)(()=>Foreground(existing)));}catch(InvalidOperationException){}return;}
        var thread=new Thread(()=>{
            try {
                using(var cancellation=new CancellationTokenSource())
                using(var window=new Form {Text="Подключение Twitch",ClientSize=new Size(460,190),StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,BackColor=Color.FromArgb(40,40,40),ForeColor=Color.White,Font=new Font("Segoe UI",10)})
                using(var label=new Label {Left=20,Top=20,Width=420,Height=110,Text="Подготовка входа Twitch…"})
                using(var close=new Button {Left=20,Top=140,Width=130,Height=30,Text="Отмена"}) {
                    window.Controls.AddRange(new Control[]{label,close});
                    close.Click+=(s,e)=>window.Close();window.FormClosed+=(s,e)=>cancellation.Cancel();
                    window.Shown+=async (s,e)=>{
                        current=window;Foreground(window);
                        try {
                            var device=await TwitchChat.Begin(cancellation.Token);Uri url;
                            if(!Uri.TryCreate(Json.Text(device,"verification_uri"),UriKind.Absolute,out url) || url.Scheme!="https" || (url.Host!="www.twitch.tv" && url.Host!="twitch.tv"))throw new Exception("Неожиданный адрес входа Twitch.");
                            label.Text="Подтвердите вход в открывшемся браузере.\nКод: "+Json.Text(device,"user_code")+"\n\nТолько разрешение отправлять сообщения в чат.";
                            Process.Start(new ProcessStartInfo(url.AbsoluteUri) {UseShellExecute=true});
                            await TwitchChat.Complete(device,cancellation.Token);
                            if(!window.IsDisposed)window.Close();
                        }catch(OperationCanceledException) {if(!cancellation.IsCancellationRequested && !window.IsDisposed){label.Text="Twitch не ответил вовремя. Начните подключение заново.";close.Text="Закрыть";}}catch(Exception ex) {if(!window.IsDisposed){label.Text=ex is System.Net.Http.HttpRequestException?"Нет соединения с Twitch. Повторите подключение позже.":ex.Message;close.Text="Закрыть";}}
                    };
                    Application.Run(window);
                }
            }finally{current=null;Interlocked.Exchange(ref showing,0);}
        });thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();
    }
}
