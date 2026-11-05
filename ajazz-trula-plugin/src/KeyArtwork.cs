using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class KeyArtwork {
    public static string LogoPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"images","trula-mark.png");
    static GraphicsPath Round(float x,float y,float w,float h,float r) {
        var p=new GraphicsPath();p.AddArc(x,y,r,r,180,90);p.AddArc(x+w-r,y,r,r,270,90);p.AddArc(x+w-r,y+h-r,r,r,0,90);p.AddArc(x,y+h-r,r,r,90,90);p.CloseFigure();return p;
    }
    public static Bitmap Render(string kind,bool active,string status,bool error=false) {
        var background=error?Color.FromArgb(92,97,110):!active && kind!="music"?Color.FromArgb(220,67,78):status=="alternate"?Color.FromArgb(90,48,145):Color.FromArgb(137,66,210);
        using(var large=new Bitmap(288,288)) {
            using(var g=Graphics.FromImage(large)) {
                g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(background);g.ScaleTransform(2,2);
                // Official Trula mark as a quiet watermark behind the main control.
                if(File.Exists(LogoPath)) using(var logo=Image.FromFile(LogoPath))
                using(var attributes=new ImageAttributes()) {
                    var matrix=new ColorMatrix(new[]{new float[]{0,0,0,0,0},new float[]{0,0,0,0,0},new float[]{0,0,0,0,0},new float[]{0,0,0,.18f,0},new float[]{1,1,1,0,1}});
                    attributes.SetColorMatrix(matrix);
                    g.DrawImage(logo,new Rectangle(-3,87,66,66),0,0,logo.Width,logo.Height,GraphicsUnit.Pixel,attributes);
                }
                using(var pen=new Pen(Color.White,8) {StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
                using(var white=new SolidBrush(Color.White)) {
                    if(kind=="request") {
                        g.FillPolygon(white,new[]{new Point(36,34),new Point(36,102),new Point(81,68)});
                        g.DrawLine(pen,87,98,119,98);g.DrawLine(pen,103,82,103,114);
                    } else if(kind=="stream-step" || kind=="monitor-step" || kind=="step-volume") {
                        g.FillPolygon(white,new[]{new Point(28,59),new Point(44,59),new Point(66,40),new Point(66,104),new Point(44,85),new Point(28,85)});
                        if(status=="min" || status=="max") {
                            using(var font=new Font("Segoe UI",23,FontStyle.Bold,GraphicsUnit.Pixel))
                            using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString(status=="min"?"MIN":"MAX",font,white,new RectangleF(72,51,66,43),format);
                        } else {
                            g.DrawLine(pen,88,72,116,72);
                            if(status!="down")g.DrawLine(pen,102,58,102,86);
                        }
                    } else if(kind=="chat") {
                        using(var bubble=Round(35,37,74,57,14))g.DrawPath(pen,bubble);
                        g.DrawLine(pen,50,96,50,110);g.DrawLine(pen,50,110,68,96);
                        g.DrawLine(pen,66,53,66,75);g.DrawLine(pen,66,53,84,49);g.DrawLine(pen,84,49,84,70);
                        g.FillEllipse(white,55,70,12,9);g.FillEllipse(white,73,65,12,9);
                    } else if(kind=="connect") {
                        g.DrawEllipse(pen,57,34,30,30);
                        g.DrawArc(pen,39,76,66,52,180,180);
                        g.DrawLine(pen,39,102,105,102);
                    } else if(kind=="skip") {
                        g.FillPolygon(white,new[]{new Point(35,41),new Point(35,103),new Point(77,72)});
                        g.DrawLine(pen,91,43,91,101);
                        g.DrawLine(pen,109,99,127,117);g.DrawLine(pen,127,99,109,117);
                    } else if(kind=="next" || kind=="previous") {
                        if(kind=="previous") {g.TranslateTransform(144,0);g.ScaleTransform(-1,1);}
                        g.FillPolygon(white,new[]{new Point(40,39),new Point(40,105),new Point(88,72)});
                        using(var bar=Round(94,39,10,66,4))g.FillPath(white,bar);
                        if(kind=="previous") {g.ScaleTransform(-1,1);g.TranslateTransform(-144,0);}
                    } else if(kind=="music") {
                        if(active) {
                            using(var left=Round(48,40,16,64,6))g.FillPath(white,left);
                            using(var right=Round(80,40,16,64,6))g.FillPath(white,right);
                        } else g.FillPolygon(white,new[]{new Point(53,36),new Point(53,108),new Point(105,72)});
                    } else if(kind=="stream-volume" || kind=="monitor-volume") {
                        if(kind=="monitor-volume") {
                            g.DrawArc(pen,28,36,58,68,180,180);
                            using(var left=Round(24,66,16,33,8))g.FillPath(white,left);
                            using(var right=Round(74,66,16,33,8))g.FillPath(white,right);
                        } else {
                            using(var screen=Round(22,43,65,47,9))g.DrawPath(pen,screen);
                            g.DrawLine(pen,54,94,54,107);g.DrawLine(pen,39,107,69,107);
                        }
                        using(var font=new Font("Segoe UI",56,FontStyle.Bold,GraphicsUnit.Pixel))
                        using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString(status=="alternate"?"2":"1",font,white,new RectangleF(87,39,49,67),format);
                    } else if(kind=="stream") {
                        using(var screen=Round(34,39,76,52,12))g.DrawPath(pen,screen);
                        g.DrawLine(pen,72,94,72,106);g.DrawLine(pen,55,107,89,107);
                        if(active) {g.DrawLine(pen,55,62,55,69);g.DrawLine(pen,72,54,72,77);g.DrawLine(pen,89,60,89,71);}
                    } else if(kind=="monitor" || kind=="monitor-volume") {
                        g.DrawArc(pen,39,34,66,76,180,180);
                        using(var left=Round(35,69,19,36,12))g.FillPath(white,left);
                        using(var right=Round(90,69,19,36,12))g.FillPath(white,right);
                    } else {
                        g.DrawLine(pen,39,48,105,48);g.DrawLine(pen,39,72,105,72);g.DrawLine(pen,39,96,105,96);
                        g.FillEllipse(white,53,40,16,16);g.FillEllipse(white,80,64,16,16);g.FillEllipse(white,46,88,16,16);
                    }
                    if(!active && kind!="music" && !error) {
                        using(var gap=new Pen(background,16) {StartCap=LineCap.Round,EndCap=LineCap.Round})g.DrawLine(gap,35,110,109,34);
                        g.DrawLine(pen,35,110,109,34);
                    }
                    if(error) {
                        // Keep the warning in the clear corner, outside the main glyph.
                        using(var badge=new SolidBrush(Color.FromArgb(255,202,95)))g.FillEllipse(badge,110,8,26,26);
                        using(var ink=new Pen(Color.FromArgb(60,48,29),3.2f) {StartCap=LineCap.Round,EndCap=LineCap.Round})g.DrawLine(ink,123,14,123,22);
                        using(var dot=new SolidBrush(Color.FromArgb(60,48,29)))g.FillEllipse(dot,121.3f,26,3.4f,3.4f);
                    }

                }
            }
            var result=new Bitmap(144,144);
            using(var g=Graphics.FromImage(result)) {g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(large,0,0,144,144);}
            return result;
        }
    }
    static readonly System.Collections.Generic.Dictionary<string,string> imageCache=new System.Collections.Generic.Dictionary<string,string>();
    public static string Data(string kind,bool active,string status,bool error=false) {
        string key=kind+"|"+active+"|"+status+"|"+error;
        lock(imageCache) {
            string cached;if(imageCache.TryGetValue(key,out cached))return cached;
            using(var bitmap=Render(kind,active,status,error))using(var stream=new MemoryStream()) {
                bitmap.Save(stream,ImageFormat.Png);string result="data:image/png;base64,"+Convert.ToBase64String(stream.ToArray());
                if(imageCache.Count>=128)imageCache.Clear();imageCache[key]=result;return result;
            }
        }
    }
}
