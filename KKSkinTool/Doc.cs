using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace KKSkinTool
{
    /// <summary>
    /// 문서: 레이어 0 = MainTex(결과), 1 = NormalMap, 2 = DetailMask (1·2는 재질 세트일 때만).
    /// 세 맵은 같은 UV·같은 크기. 실행 취소는 모든 레이어를 함께 저장
    /// </summary>
    public class Doc : IDisposable
    {
        public const int MAIN = 0, NORMAL = 1, DETAIL = 2;
        public static readonly string[] LayerNames = { "MainTex", "NormalMap", "DetailMask" };
        public readonly Bitmap[] Layers = new Bitmap[3];
        public readonly string[] LayerFiles = new string[3];         // 불러온 파일 이름 (저장할 때 같은 이름으로)
        public Bitmap Result { get { return Layers[MAIN]; } }
        public int Active;                                            // 지금 보고 편집하는 레이어
        public Bitmap ActiveBitmap { get { return Layers[Active] ?? Layers[MAIN]; } }
        public bool IsMaterial { get { return Layers[NORMAL] != null || Layers[DETAIL] != null; } }
        public bool TransparentBg { get; set; }
        public int W { get { return Result.Width; } }
        public int H { get { return Result.Height; } }
        public bool Dirty;
        int MaxUndo
        {
            get
            {   // 레이어 수·크기에 맞춰 약 1.2GB 안에서
                long snap = (long)W * H * 4 * Layers.Count(l => l != null);
                return (int)Math.Max(3, Math.Min(12, 1200L * 1024 * 1024 / Math.Max(1, snap)));
            }
        }
        readonly List<Bitmap[]> undo = new List<Bitmap[]>(), redo = new List<Bitmap[]>();

        public Doc(int w, int h, bool transparent)
        {
            TransparentBg = transparent;
            Layers[MAIN] = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(Layers[MAIN])) g.Clear(transparent ? Color.Transparent : Color.White);
        }

        public static Bitmap LoadBitmap(string path, int w, int h)
        {
            Bitmap src;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read)) src = new Bitmap(fs);
            using (src)
            {
                if (w <= 0 || h <= 0) { w = src.Width; h = src.Height; }
                var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(b))
                {
                    g.CompositingMode = CompositingMode.SourceCopy; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, new Rectangle(0, 0, w, h));
                }
                return b;
            }
        }

        public static Doc Open(string path, int w, int h)
        {
            var b = LoadBitmap(path, w, h);
            var d = new Doc(1, 1, Pixels.HasTransparency(b));
            d.Layers[MAIN].Dispose(); d.Layers[MAIN] = b; d.LayerFiles[MAIN] = Path.GetFileName(path);
            return d;
        }

        /// <summary>레이어 설정 (실행 취소 없음 — 문서를 열 때). 크기가 다르면 MainTex 크기로 맞춤</summary>
        public void SetLayer(int i, Bitmap b, string file)
        {
            if (b != null && (b.Width != W || b.Height != H))
            {
                var r = new Bitmap(W, H, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(r)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(b, new Rectangle(0, 0, W, H)); }
                b.Dispose(); b = r;
            }
            if (Layers[i] != null) Layers[i].Dispose();
            Layers[i] = b; LayerFiles[i] = file;
            if (b == null && Active == i) Active = MAIN;
        }

        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }

        Bitmap[] Snapshot() { return Layers.Select(l => l == null ? null : (Bitmap)l.Clone()).ToArray(); }
        void PushSnap(List<Bitmap[]> stack, Bitmap[] s)
        {
            stack.Add(s);
            while (stack.Count > MaxUndo) { foreach (var b in stack[0]) if (b != null) b.Dispose(); stack.RemoveAt(0); }
        }
        void ClearRedo() { foreach (var s in redo) foreach (var b in s) if (b != null) b.Dispose(); redo.Clear(); }

        /// <summary>외부 편집(유동화 등) 시작 전 실행 취소 지점</summary>
        public void SaveUndoPoint() { PushSnap(undo, Snapshot()); ClearRedo(); }
        /// <summary>지정한 백업(잘라내기 전 상태)을 실행 취소 지점으로</summary>
        public void SaveUndoPoint(Bitmap[] backups) { PushSnap(undo, backups.Select(b => b == null ? null : (Bitmap)b.Clone()).ToArray()); ClearRedo(); }
        public void DropLastUndo()
        {
            if (undo.Count == 0) return;
            foreach (var b in undo[undo.Count - 1]) if (b != null) b.Dispose(); undo.RemoveAt(undo.Count - 1);
        }
        public void Undo() { if (!CanUndo) return; PushSnap(redo, Snapshot()); Restore(undo); }
        public void Redo() { if (!CanRedo) return; PushSnap(undo, Snapshot()); Restore(redo); }
        void Restore(List<Bitmap[]> from)
        {
            var s = from[from.Count - 1]; from.RemoveAt(from.Count - 1);
            for (int i = 0; i < Layers.Length; i++) { if (Layers[i] != null) Layers[i].Dispose(); Layers[i] = s[i]; }
            if (Layers[Active] == null) Active = MAIN;
            Dirty = true;
        }

        /// <summary>
        /// 파트를 지금 레이어에 확정. 결과에서 잘라낸 선택 영역이면 잘라내기 전 상태를 실행 취소 지점으로 쓰고,
        /// 함께 떼어낸 다른 맵 조각도 같은 변형으로 각 레이어에 놓음
        /// </summary>
        public void Commit(PartLayer p)
        {
            bool fromResult = p.IsFloat && p.Parent == null;
            if (fromResult && p.BackupLayers != null) SaveUndoPoint(p.BackupLayers);
            else if (fromResult) { var b = Snapshot(); b[MAIN].Dispose(); b[MAIN] = (Bitmap)p.Backup.Clone(); PushSnap(undo, b); ClearRedo(); }
            else SaveUndoPoint();
            using (var m = p.GetMatrix())
            {
                if (fromResult && p.FloatLayer >= 0) DrawPart(Layers[p.FloatLayer], p, m, true);
                else DrawPart(fromResult ? Result : ActiveBitmap, p, m, true);
                if (fromResult && p.Extra != null)
                    for (int i = 0; i < p.Extra.Length; i++)
                        if (p.Extra[i] != null && Layers[i] != null) DrawExtra(Layers[i], p, p.Extra[i], m, i == NORMAL);
            }
            Dirty = true;
        }

        /// <summary>파트를 target 위에 matrix로 그려 합성</summary>
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
                else if (p.S.Blend == BlendKind.Erase)
                {
                    using (var tmp = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(tmp)) { g.Clear(Color.Transparent); Quality(g); g.Transform = m; g.DrawImage(p.Source, srcRect, 0, 0, p.W, p.H, GraphicsUnit.Pixel, ia); }
                        Pixels.EraseInto(target, tmp);
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

        /// <summary>떼어낸 NormalMap/DetailMask 조각을 같은 변형으로 놓기 (노멀은 회전·반전·비율만큼 벡터도 돌림)</summary>
        static void DrawExtra(Bitmap target, PartLayer p, Bitmap piece, Matrix m, bool isNormal)
        {
            Bitmap src = isNormal ? MaterialGen.TransformNormals(piece, p.S.Rotation, p.S.FlipH, p.S.ScaleX, p.S.ScaleY) : piece;
            try
            {
                using (var g = Graphics.FromImage(target))
                {
                    Quality(g); g.Transform = m;
                    g.DrawImage(src, new Rectangle(0, 0, piece.Width, piece.Height), 0, 0, piece.Width, piece.Height, GraphicsUnit.Pixel);
                }
            }
            finally { if (src != piece) src.Dispose(); }
        }

        /// <summary>잘라낸 선택 영역 취소: 잘라내기 전으로 (실행 취소 기록 없음)</summary>
        public void RestoreFrom(Bitmap backup) { RestoreLayer(MAIN, backup); }
        public void RestoreFrom(Bitmap[] backups)
        {
            for (int i = 0; i < Layers.Length; i++) if (backups[i] != null && Layers[i] != null) RestoreLayer(i, backups[i]);
        }
        void RestoreLayer(int i, Bitmap backup)
        {
            using (var g = Graphics.FromImage(Layers[i])) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImage(backup, new Rectangle(0, 0, W, H)); }
        }

        /// <summary>MainTex 픽셀 직접 편집 (실행 취소 포함)</summary>
        public void ApplyPixels(Action<byte[]> edit) { ApplyLayer(MAIN, edit); }
        /// <summary>지정 레이어 픽셀 직접 편집 (실행 취소 포함)</summary>
        public void ApplyLayer(int i, Action<byte[]> edit)
        {
            SaveUndoPoint();
            var px = Pixels.Read(Layers[i]); edit(px); Pixels.Write(Layers[i], px);
            Dirty = true;
        }
        /// <summary>여러 레이어를 한 번에 편집 (실행 취소 한 단계). edit에는 레이어별 픽셀(없는 레이어는 null)</summary>
        public void ApplyAll(Action<byte[][]> edit)
        {
            SaveUndoPoint();
            var px = Layers.Select(l => l == null ? null : Pixels.Read(l)).ToArray(); edit(px);
            for (int i = 0; i < Layers.Length; i++) if (Layers[i] != null) Pixels.Write(Layers[i], px[i]);
            Dirty = true;
        }

        static void Quality(Graphics g)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
        }

        /// <summary>지금 보고 있는 레이어 저장</summary>
        public void Save(string path) { ActiveBitmap.Save(path, ImageFormat.Png); Dirty = false; }

        public void Dispose()
        {
            foreach (var l in Layers) if (l != null) l.Dispose();
            foreach (var s in undo.Concat(redo)) foreach (var b in s) if (b != null) b.Dispose();
            undo.Clear(); redo.Clear();
        }
    }
}
