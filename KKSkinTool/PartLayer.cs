using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace KKSkinTool
{
    public enum BlendKind { Darken, Multiply, Normal }

    /// <summary>배치 중인 파트의 변환·표현 상태 (undo 단위)</summary>
    public class PartState
    {
        public float Cx, Cy, ScaleX = 1f, ScaleY = 1f, Rotation, Opacity = 1f;
        public bool FlipH;
        public BlendKind Blend = BlendKind.Darken;
        public int Tint = 0;            // 0 = 색상 변경 없음, 그 외 ARGB (알파 255)
        public PartState Clone() { return (PartState)MemberwiseClone(); }
        public bool SameAs(PartState o)
        {
            return o != null && Cx == o.Cx && Cy == o.Cy && ScaleX == o.ScaleX && ScaleY == o.ScaleY && Rotation == o.Rotation
                && Opacity == o.Opacity && FlipH == o.FlipH && Blend == o.Blend && Tint == o.Tint;
        }
    }

    /// <summary>
    /// 불러온 파트 이미지 + 변환. 확정 전까지는 결과에 반영되지 않음.
    /// Parent가 있으면 "떠 있는 선택 영역": 좌표(S.Cx, S.Cy 등)는 부모 파트의 로컬 좌표계 기준
    /// </summary>
    public class PartLayer : IDisposable
    {
        public Bitmap Source { get; private set; }
        public Bitmap Display { get; private set; }     // 화면 미리보기용 (어둡게/곱하기는 검정+알파로 변환)
        public string Name { get; private set; }
        public PartState S = new PartState();
        public PartLayer Parent;                        // 파트 안에서 잘라낸 선택 영역이면 그 파트
        public Bitmap Backup;                           // 선택 영역을 잘라내기 전 원본 (취소용)
        public bool IsFloat { get { return Backup != null; } }
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

        /// <summary>잘라낸 선택 영역 등 메모리 이미지로 만들기 (src 소유권을 가져감)</summary>
        public PartLayer(Bitmap src, string name, BlendKind blend)
        {
            Source = src; Name = name; S.Blend = blend; RefreshDisplay();
        }

        public void RefreshDisplay(bool force = false)
        {
            if (!force && displayFor == S.Blend && Display != null) return;
            if (Display != null && Display != Source) Display.Dispose();
            Display = S.Blend == BlendKind.Normal ? Source : Pixels.DarknessToAlpha(Source);
            displayFor = S.Blend;
        }
        /// <summary>원본 일부가 바뀌었을 때(유동화) 미리보기만 그 영역 갱신</summary>
        public void RefreshDisplayRegion(Rectangle r)
        {
            if (Display == null || Display == Source) return;
            Pixels.DarknessToAlphaRegion(Source, Display, r);
        }

        /// <summary>자기 로컬(0..W, 0..H) → 부모 좌표 (부모가 없으면 문서 좌표)</summary>
        public Matrix GetLocalMatrix()
        {
            var m = new Matrix();
            m.Translate(S.Cx, S.Cy);
            m.Rotate(S.Rotation);
            m.Scale(S.ScaleX * (S.FlipH ? -1 : 1), S.ScaleY);
            m.Translate(-W / 2f, -H / 2f);
            return m;
        }
        /// <summary>자기 로컬 → 문서 좌표</summary>
        public Matrix GetMatrix()
        {
            var m = GetLocalMatrix();
            if (Parent != null) using (var pm = Parent.GetMatrix()) m.Multiply(pm, MatrixOrder.Append);
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
        /// <summary>문서 좌표 → 이 파트의 S 값이 쓰는 좌표계 (부모 로컬 또는 문서)</summary>
        public PointF ToParentSpace(PointF doc) { return Parent == null ? doc : Parent.ToLocal(doc); }
        /// <summary>문서 픽셀 1개가 자기 원본 몇 픽셀인지의 역수 (화면 브러시 크기 환산용)</summary>
        public float DocPerLocal()
        {
            var c = Corners();
            float w = (float)Math.Sqrt(Math.Pow(c[1].X - c[0].X, 2) + Math.Pow(c[1].Y - c[0].Y, 2)) / W;
            float h = (float)Math.Sqrt(Math.Pow(c[3].X - c[0].X, 2) + Math.Pow(c[3].Y - c[0].Y, 2)) / H;
            return Math.Max(1e-3f, (w + h) / 2);
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

        /// <summary>
        /// 불투명도 + 색상 변경을 반영한 그리기 속성.
        /// 일반: 색상을 곱함(흰 타투 → 지정 색). 어둡게/곱하기: 흰 바탕은 그대로, 진한 부분(잉크)이 지정 색이 됨.
        /// forDisplay: 어둡게/곱하기 미리보기(검정+알파)에 맞춘 행렬
        /// </summary>
        public ImageAttributes Attr(bool forDisplay)
        {
            var cm = new ColorMatrix { Matrix33 = Math.Max(0f, Math.Min(1f, S.Opacity)) };
            if (S.Tint != 0)
            {
                var t = Color.FromArgb(S.Tint); float r = t.R / 255f, g = t.G / 255f, b = t.B / 255f;
                if (S.Blend == BlendKind.Normal) { cm.Matrix00 = r; cm.Matrix11 = g; cm.Matrix22 = b; }
                else if (forDisplay) { cm.Matrix40 = r; cm.Matrix41 = g; cm.Matrix42 = b; }      // 검정 잉크 → 지정 색
                else { cm.Matrix00 = 1 - r; cm.Matrix11 = 1 - g; cm.Matrix22 = 1 - b; cm.Matrix40 = r; cm.Matrix41 = g; cm.Matrix42 = b; }  // c' = t + c(1-t)
            }
            var ia = new ImageAttributes(); ia.SetColorMatrix(cm); return ia;
        }

        public void Dispose()
        {
            if (Display != null && Display != Source) Display.Dispose();
            if (Source != null) Source.Dispose();
            if (Backup != null) Backup.Dispose();
        }
    }
}
