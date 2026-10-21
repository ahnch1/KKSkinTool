using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace KKSkinTool
{
    public enum BlendKind { Darken, Multiply, Normal }

    /// <summary>배치 중인 파트의 변환 상태 (undo 단위)</summary>
    public class PartState
    {
        public float Cx, Cy, ScaleX = 1f, ScaleY = 1f, Rotation, Opacity = 1f;
        public bool FlipH;
        public BlendKind Blend = BlendKind.Darken;
        public PartState Clone() { return (PartState)MemberwiseClone(); }
        public bool SameAs(PartState o)
        {
            return o != null && Cx == o.Cx && Cy == o.Cy && ScaleX == o.ScaleX && ScaleY == o.ScaleY && Rotation == o.Rotation
                && Opacity == o.Opacity && FlipH == o.FlipH && Blend == o.Blend;
        }
    }

    /// <summary>불러온 파트 이미지 + 변환. 확정 전까지는 결과에 반영되지 않음</summary>
    public class PartLayer : IDisposable
    {
        public Bitmap Source { get; private set; }
        public Bitmap Display { get; private set; }     // 화면 미리보기용 (어둡게/곱하기는 검정+알파로 변환)
        public string Name { get; private set; }
        public PartState S = new PartState();
        BlendKind displayFor = (BlendKind)(-1);

        public int W { get { return Source.Width; } }
        public int H { get { return Source.Height; } }

        public PartLayer(string path)
        {
            Name = Path.GetFileName(path);
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var tmp = new Bitmap(fs))
            {
                Source = new Bitmap(tmp.Width, tmp.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(Source)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImage(tmp, 0, 0, tmp.Width, tmp.Height); }
            }
            S.Blend = Pixels.HasTransparency(Source) ? BlendKind.Normal : BlendKind.Darken;   // 투명 PNG=타투, 불투명=탠라인
            RefreshDisplay();
        }

        public void RefreshDisplay()
        {
            if (displayFor == S.Blend && Display != null) return;
            if (Display != null && Display != Source) Display.Dispose();
            Display = S.Blend == BlendKind.Normal ? Source : Pixels.DarknessToAlpha(Source);
            displayFor = S.Blend;
        }

        /// <summary>파트 로컬 좌표(0..W, 0..H) → 문서 좌표</summary>
        public Matrix GetMatrix()
        {
            var m = new Matrix();
            m.Translate(S.Cx, S.Cy);
            m.Rotate(S.Rotation);
            m.Scale(S.ScaleX * (S.FlipH ? -1 : 1), S.ScaleY);
            m.Translate(-W / 2f, -H / 2f);
            return m;
        }

        public PointF[] Corners()
        {
            var p = new[] { new PointF(0, 0), new PointF(W, 0), new PointF(W, H), new PointF(0, H) };
            using (var m = GetMatrix()) m.TransformPoints(p);
            return p;
        }

        public PointF ToLocal(PointF doc)
        {
            var p = new[] { doc };
            using (var m = GetMatrix()) { if (!m.IsInvertible) return new PointF(-1, -1); m.Invert(); m.TransformPoints(p); }
            return p[0];
        }

        public bool Contains(PointF doc)
        {
            var l = ToLocal(doc);
            return l.X >= 0 && l.Y >= 0 && l.X < W && l.Y < H;
        }

        public static ImageAttributes OpacityAttr(float opacity)
        {
            var cm = new ColorMatrix { Matrix33 = Math.Max(0f, Math.Min(1f, opacity)) };
            var ia = new ImageAttributes(); ia.SetColorMatrix(cm); return ia;
        }

        public void Dispose()
        {
            if (Display != null && Display != Source) Display.Dispose();
            if (Source != null) Source.Dispose();
        }
    }
}
