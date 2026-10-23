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
        public bool TransparentBg { get; set; }
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

        /// <summary>외부 편집(유동화) 시작 전 실행 취소 지점 저장</summary>
        public void SaveUndoPoint()
        {
            Push(undo);
            foreach (var b in redo) b.Dispose(); redo.Clear();
        }
        /// <summary>마지막 실행 취소 지점을 버림 (유동화 취소 시)</summary>
        public void DropLastUndo()
        {
            if (undo.Count == 0) return;
            undo[undo.Count - 1].Dispose(); undo.RemoveAt(undo.Count - 1);
        }
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

        /// <summary>파트를 현재 변환 그대로 결과에 확정. 결과에서 잘라낸 선택 영역이면 잘라내기 전 상태를 실행 취소 지점으로 씀</summary>
        public void Commit(PartLayer p)
        {
            if (p.IsFloat && p.Parent == null) { undo.Add((Bitmap)p.Backup.Clone()); if (undo.Count > MaxUndo) { undo[0].Dispose(); undo.RemoveAt(0); } }
            else Push(undo);
            foreach (var b in redo) b.Dispose(); redo.Clear();
            using (var m = p.GetMatrix()) DrawPart(Result, p, m, true);
            Dirty = true;
        }

        /// <summary>파트를 target 위에 matrix로 그려 합성 (일반: 알파 합성 / 어둡게·곱하기: 흰 바탕에 그린 뒤 픽셀 합성)</summary>
        public static void DrawPart(Bitmap target, PartLayer p, Matrix m, bool transparentAware)
        {
            using (var ia = p.Attr(false))
            {
                var srcRect = new Rectangle(0, 0, p.W, p.H);
                if (p.S.Blend == BlendKind.Normal)
                {
                    using (var g = Graphics.FromImage(target))
                    {
                        Quality(g); g.Transform = m;
                        g.DrawImage(p.Source, srcRect, 0, 0, p.W, p.H, GraphicsUnit.Pixel, ia);
                    }
                }
                else
                {
                    using (var tmp = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(tmp))
                        {
                            g.Clear(Color.White); Quality(g); g.Transform = m;
                            g.DrawImage(p.Source, srcRect, 0, 0, p.W, p.H, GraphicsUnit.Pixel, ia);
                        }
                        Pixels.BlendInto(target, tmp, p.S.Blend);
                    }
                }
            }
        }

        /// <summary>결과에서 잘라낸 선택 영역을 취소: 잘라내기 전으로 복원 (실행 취소 기록 없음)</summary>
        public void RestoreFrom(Bitmap backup)
        {
            using (var g = Graphics.FromImage(Result)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(backup, 0, 0); }
        }

        /// <summary>결과 픽셀(BGRA)을 직접 편집 (실행 취소 지점 포함) — 3D 투영 굽기용</summary>
        public void ApplyPixels(Action<byte[]> edit)
        {
            Push(undo);
            foreach (var b in redo) b.Dispose(); redo.Clear();
            var px = Pixels.Read(Result); edit(px); Pixels.Write(Result, px);
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
