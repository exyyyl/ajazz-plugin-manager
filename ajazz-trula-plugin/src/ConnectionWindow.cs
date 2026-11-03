using System;
using System.Drawing;
using System.Net.Http;
using System.Threading;
using System.Windows.Forms;

public static class ConnectionWindow {
    static int showing;
    static Form current;
    static void Foreground(Form window) {
        if(window.IsDisposed) return;
        window.Show();window.WindowState=FormWindowState.Normal;
        window.TopMost=true;window.Activate();window.BringToFront();window.TopMost=false;
    }
    public static void Open() {
        if(Interlocked.Exchange(ref showing,1)!=0) {
            var existing=current;
            if(existing!=null && existing.IsHandleCreated && !existing.IsDisposed) {
                try {existing.BeginInvoke((Action)(()=>Foreground(existing)));}catch(InvalidOperationException) { }
            }
            return;
        }
        var thread=new Thread(()=>{
            try {
                using(var window=new Form {Text="Подключение Trula",ClientSize=new Size(450,190),StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,BackColor=Color.FromArgb(40,40,40),ForeColor=Color.White,Font=new Font("Segoe UI",10)})
                using(var input=new TextBox {Left=20,Top=49,Width=410,UseSystemPasswordChar=true})
                using(var label=new Label {Left=20,Top=18,Width=410,Text="Ссылка панели Trula из личного кабинета"})
                using(var status=new Label {Left=20,Top=87,Width=410,Height=45,Text="Подключение будет общим для всех кнопок."})
                using(var save=new Button {Left=20,Top=139,Width=155,Height=32,Text="Подключить",FlatStyle=FlatStyle.Flat}) {
                    window.Controls.AddRange(new Control[]{label,input,status,save});
                    window.Shown+=(sender,args)=>{current=window;Foreground(window);input.Focus();};
                    window.AcceptButton=save;
                    save.Click+=async (sender,args)=>{
                        save.Enabled=false;status.Text="Проверка ссылки…";
                        try {
                            string token=Commands.Token(input.Text);
                            using(var http=new HttpClient {Timeout=TimeSpan.FromSeconds(15)})
                            using(var request=new HttpRequestMessage(HttpMethod.Get,"https://trula.io/api/v1/widget/panel/")) {
                                request.Headers.Add("token",token);
                                using(var response=await http.SendAsync(request)) {
                                    if(!response.IsSuccessStatusCode) throw new Exception();
                                    var state=Json.Decode(await response.Content.ReadAsStringAsync());
                                    if(!state.ContainsKey("panel") || !state.ContainsKey("source")) throw new Exception();
                                }
                            }
                            Credentials.Save(token);window.Close();
                        } catch {if(!window.IsDisposed) {status.Text="Не удалось подключиться. Проверьте ссылку панели и интернет.";save.Enabled=true;}}
                    };
                    Application.Run(window);
                }
            } finally {current=null;Interlocked.Exchange(ref showing,0);}
        });
        thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();
    }
}
