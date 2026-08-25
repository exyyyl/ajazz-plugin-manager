using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class IconGenerator
{
    private static readonly int[] Sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };

    private static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        string output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(output));

        List<byte[]> images = new List<byte[]>();
        foreach (int size in Sizes) images.Add(CreatePng(size));

        using (FileStream stream = File.Create(output))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)Sizes.Length);
            int offset = 6 + Sizes.Length * 16;
            for (int i = 0; i < Sizes.Length; i++)
            {
                writer.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));
                writer.Write((byte)(Sizes[i] >= 256 ? 0 : Sizes[i]));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(images[i].Length);
                writer.Write(offset);
                offset += images[i].Length;
            }
            foreach (byte[] image in images) writer.Write(image);
        }
        return 0;
    }

    private static byte[] CreatePng(int size)
    {
        using (Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.Clear(Color.Transparent);

            float scale = size / 256f;
            RectangleF body = new RectangleF(10 * scale, 10 * scale, 236 * scale, 236 * scale);
            using (GraphicsPath bodyPath = RoundedRectangle(body, 47 * scale))
            using (LinearGradientBrush gradient = new LinearGradientBrush(body, Color.FromArgb(137, 91, 246), Color.FromArgb(76, 49, 174), 45f))
                graphics.FillPath(gradient, bodyPath);

            float startX = 42 * scale;
            float startY = 61 * scale;
            float key = 47 * scale;
            float gap = 14 * scale;
            using (SolidBrush keyBrush = new SolidBrush(Color.FromArgb(242, 245, 250)))
            {
                for (int row = 0; row < 2; row++)
                {
                    for (int column = 0; column < 3; column++)
                    {
                        RectangleF keyRect = new RectangleF(startX + column * (key + gap), startY + row * (key + gap), key, key);
                        using (GraphicsPath keyPath = RoundedRectangle(keyRect, 11 * scale)) graphics.FillPath(keyBrush, keyPath);
                    }
                }
            }

            RectangleF accentRect = new RectangleF(164 * scale, 183 * scale, 50 * scale, 13 * scale);
            using (SolidBrush accentBrush = new SolidBrush(Color.FromArgb(186, 167, 255)))
            using (GraphicsPath accentPath = RoundedRectangle(accentRect, 6 * scale)) graphics.FillPath(accentBrush, accentPath);

            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, ImageFormat.Png);
                return memory.ToArray();
            }
        }
    }

    private static GraphicsPath RoundedRectangle(RectangleF rectangle, float radius)
    {
        float diameter = Math.Max(1f, radius * 2f);
        GraphicsPath path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
