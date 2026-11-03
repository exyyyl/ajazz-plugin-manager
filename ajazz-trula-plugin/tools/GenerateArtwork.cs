using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
class GenerateArtwork {
 static void Main(string[] args) {
  Directory.CreateDirectory(args[0]);
  KeyArtwork.LogoPath=Path.Combine(args[0],"trula-mark.png");
  using(var logo=new Bitmap(Path.Combine(args[0],"trula@2x.png"))) {
   for(int y=0;y<logo.Height;y++)for(int x=0;x<logo.Width;x++) {
    var color=logo.GetPixel(x,y);
    int alpha=color.A*Math.Max(0,color.B-color.R-30)/212;
    logo.SetPixel(x,y,Color.FromArgb(Math.Min(255,alpha),255,255,255));
   }
   logo.Save(KeyArtwork.LogoPath,ImageFormat.Png);
  }
  using(var preview=new Bitmap(12*174,3*174))using(var g=Graphics.FromImage(preview)) {
   g.Clear(Color.FromArgb(9,13,21));int x=0;
   foreach(string kind in new[]{"music","stream","monitor","previous","next","connect","chat","stream-volume","monitor-volume","step-volume","stream-step","monitor-step"}) {
    for(int i=0;i<3;i++) {
     string status=i==2?"НЕТ СВЯЗИ":kind=="music"?(i==0?"ИГРАЕТ":"ПАУЗА"):(i==0?"50%":"БЕЗ ЗВУКА");
     if(kind.EndsWith("-volume") && kind!="step-volume")status=i==0?"first":"alternate";
     if(kind.EndsWith("-step") || kind=="step-volume")status=i==0?"up":"max";
     using(var bitmap=KeyArtwork.Render(kind,i==0 || kind.EndsWith("-step") || kind=="step-volume",status,i==2)) {
      g.DrawImage(bitmap,x*174+15,i*174+15,144,144);
      string name=kind+(i==0?"-on":i==1?"-off":"-error");
      bitmap.Save(Path.Combine(args[0],name+"@2x.png"),ImageFormat.Png);
      using(var small=new Bitmap(bitmap,72,72))small.Save(Path.Combine(args[0],name+".png"),ImageFormat.Png);
     }
    } x++;
   }
   preview.Save(args[1],ImageFormat.Png);
  }
 }
}
