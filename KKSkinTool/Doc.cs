using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace KKSkinTool
{
    /// <summary>결과 레이어 + 실행 취소/다시 실행</summary>
    public class Doc : IDisposable
    {
        public Bitmap Result { get; private set; }
        public bool TransparentBg { get; private set; }
        public int W { get { return Result.Width; } }
        public int H { get { return Result.Height; } }
        public bool Dirty;
        const int MaxUndo = 12;
        readonly List<Bitmap> undo = new List<Bitmap>(), redo = new List<Bitmap>();

        public Doc(int w, int h, bool transparent)
        {
            TransparentBg = transparent;
            Result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(Result)) g.Clear(transparent ? Color.Transparent : Color.White);
        }

        public static Doc Open(string path, int w, int h)
        {
            Bitmap src;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read)) src = new Bitmap(fs);
            using (src)
            {
                var d = new Doc(w, h, Pixels.HasTransparency(src));
                using (var g = Graphics.FromImage(d.Result))
                {
                    g.CompositingMode = CompositingMode.SourceCopy; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, 0, 0, w, h);
                }
                return d;
            }
        }

        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }

        void Push(List<Bitmap> stack)
        {
            stack.Add((Bitmap)Result.Clone());
            if (stack.Count > MaxUndo) { stack[0].Dispose(); stack.RemoveAt(0); }
        }
        public void Undo() { if (!CanUndo) return; Push(redo); Swap(undo); }
        public void Redo() { if (!CanRedo) return; Push(undo); Swap(redo); }
        void Swap(List<Bitmap> from)
        {
            var b = from[from.Count - 1]; from.RemoveAt(from.Count - 1);
            Result.Dispose(); Result = b; Dirty = true;
        }

        /// <summary>파트를 현재 변환 그대로 결과에 확정</summary>
        public void Commit(PartLayer p)
        {
            Push(undo);
            foreach (var b in redo) b.Dispose(); redo.Clear();
            using (var m = p.GetMatrix())
            using (var ia = PartLayer.OpacityAttr(p.S.Opacity))
            {
                var srcRect = new Rectangle(0, 0, p.W, p.H);
                if (p.S.Blend == BlendKind.Normal)
                {
                    using (var g = Graphics.FromImage(Result))
                    {
                        Quality(g); g.Transform = m;
                        g.DrawImage(p.Source, srcRect, 0, 0, p.W, p.H, GraphicsUnit.Pixel, ia);
                    }
                }
                else
                {
                    using (var tmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(tmp))
                        {
                            g.Clear(Color.White); Quality(g); g.Transform = m;
                            g.DrawImage(p.Source, srcRect, 0, 0, p.W, p.H, GraphicsUnit.Pixel, ia);
                        }
                        Pixels.BlendInto(Result, tmp, p.S.Blend);
                    }
                }
            }
            Dirty = true;
        }

        static void Quality(Graphics g)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
        }

        public void Save(string path) { Result.Save(path, ImageFormat.Png); Dirty = false; }

        public void Dispose()
        {
            Result.Dispose();
            foreach (var b in undo) b.Dispose(); foreach (var b in redo) b.Dispose();
            undo.Clear(); redo.Clear();
        }
    }
}
