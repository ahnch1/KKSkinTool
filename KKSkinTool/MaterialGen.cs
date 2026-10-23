using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;

namespace KKSkinTool
{
    /// <summary>
    /// NormalMap / DetailMask 생성과 연동 효과.
    /// 노멀맵: Unity(OpenGL) 방식 탄젠트 공간, R=+U(오른쪽) G=+V(위쪽) B=바깥.
    /// 디테일마스크: KK 의상 셰이더 보조 마스크 — G = 밝기(낮을수록 주름·그늘이 짙음), R = 광택 부위.
    /// KK 청바지 기준: 평평한 원단 G ≈ 110 전후, 주름 골은 0에 가까움
    /// </summary>
    public static class MaterialGen
    {
        public const int DetailNeutralG = 110;

        // ------------------------------------------------------------ 기본 연산
        public static float[] Luma(byte[] p)
        {
            var h = new float[p.Length / 4];
            Parallel.For(0, h.Length, k => { int i = k * 4; h[k] = (p[i + 2] * 0.299f + p[i + 1] * 0.587f + p[i] * 0.114f) / 255f; });
            return h;
        }
        /// <summary>박스 블러 3회 ≈ 가우시안</summary>
        public static float[] Blur(float[] src, int w, int h, float sigma)
        {
            if (sigma <= 0.3f) return (float[])src.Clone();
            int r = Math.Max(1, (int)Math.Round(sigma * 0.9));
            var a = (float[])src.Clone(); var b = new float[src.Length];
            for (int pass = 0; pass < 3; pass++)
            {
                Parallel.For(0, h, y =>
                {
                    int o = y * w; float acc = 0; int n = 0;
                    for (int x = -r; x <= r; x++) if (x >= 0 && x < w) { acc += a[o + x]; n++; }
                    for (int x = 0; x < w; x++)
                    {
                        b[o + x] = acc / n;
                        int xo = x - r, xi = x + r + 1;
                        if (xo >= 0) { acc -= a[o + xo]; n--; }
                        if (xi < w) { acc += a[o + xi]; n++; }
                    }
                });
                Parallel.For(0, w, x =>
                {
                    float acc = 0; int n = 0;
                    for (int y = -r; y <= r; y++) if (y >= 0 && y < h) { acc += b[y * w + x]; n++; }
                    for (int y = 0; y < h; y++)
                    {
                        a[y * w + x] = acc / n;
                        int yo = y - r, yi = y + r + 1;
                        if (yo >= 0) { acc -= b[yo * w + x]; n--; }
                        if (yi < h) { acc += b[yi * w + x]; n++; }
                    }
                });
            }
            return a;
        }
        static void Encode(byte[] o, int i, float nx, float ny, float nz)
        {
            float l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz) + 1e-9f; nx /= l; ny /= l; nz /= l;
            o[i + 2] = (byte)Math.Max(0, Math.Min(255, (nx * 0.5f + 0.5f) * 255 + 0.5f));
            o[i + 1] = (byte)Math.Max(0, Math.Min(255, (ny * 0.5f + 0.5f) * 255 + 0.5f));
            o[i] = (byte)Math.Max(0, Math.Min(255, (nz * 0.5f + 0.5f) * 255 + 0.5f));
            o[i + 3] = 255;
        }
        static void Decode(byte[] p, int i, out float nx, out float ny, out float nz)
        {
            nx = p[i + 2] / 255f * 2 - 1; ny = p[i + 1] / 255f * 2 - 1; nz = p[i] / 255f * 2 - 1;
        }

        // ------------------------------------------------------------ 생성
        /// <summary>MainTex 밝기를 높이로 보고 노멀맵 생성 (밝은 곳이 볼록). strength: 기울기 배율</summary>
        public static Bitmap GenerateNormal(Bitmap main, float strength = 3f, float blur = 1.2f)
        {
            int w = main.Width, h = main.Height; var p = Pixels.Read(main);
            var hgt = Blur(Luma(p), w, h, blur); var o = new byte[p.Length];
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    float hl = hgt[y * w + Math.Max(0, x - 1)], hr = hgt[y * w + Math.Min(w - 1, x + 1)];
                    float hu = hgt[Math.Max(0, y - 1) * w + x], hd = hgt[Math.Min(h - 1, y + 1) * w + x];
                    float dx = (hr - hl) * 0.5f * strength, dyImg = (hd - hu) * 0.5f * strength;
                    Encode(o, (y * w + x) * 4, -dx, dyImg, 1f);         // 이미지 y는 아래 = -V
                }
            });
            var b = new Bitmap(w, h, PixelFormat.Format32bppArgb); Pixels.Write(b, o); return b;
        }
        /// <summary>평평한 노멀맵 (128,128,255)</summary>
        public static Bitmap FlatNormal(int w, int h)
        {
            var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b)) g.Clear(Color.FromArgb(255, 128, 128, 255));
            return b;
        }
        /// <summary>
        /// DetailMask 생성: G = 원단 기본값(110)에서 주변보다 어두운 골(주름·솔기)일수록 낮게(짙은 그늘), R = 0.
        /// MainTex가 투명한 곳은 0
        /// </summary>
        public static Bitmap GenerateDetail(Bitmap main)
        {
            int w = main.Width, h = main.Height; var p = Pixels.Read(main);
            var l = Luma(p); var big = Blur(l, w, h, Math.Max(3f, w / 170f)); var o = new byte[p.Length];
            Parallel.For(0, l.Length, k =>
            {
                int i = k * 4; float cav = big[k] - l[k];                   // +면 주변보다 어두움
                int g = (int)(DetailNeutralG - cav * 700);
                o[i] = 0; o[i + 1] = (byte)Math.Max(0, Math.Min(200, g)); o[i + 2] = 0; o[i + 3] = 255;
                if (p[i + 3] < 8) o[i + 1] = 0;
            });
            var b = new Bitmap(w, h, PixelFormat.Format32bppArgb); Pixels.Write(b, o); return b;
        }

        /// <summary>노멀 조각을 회전·반전·비율에 맞게 벡터까지 변환 (UV 위에서 조각을 돌리면 요철 방향도 같이 돌아야 함)</summary>
        public static Bitmap TransformNormals(Bitmap piece, float rotDeg, bool flipH, float sx, float sy)
        {
            var p = Pixels.Read(piece); double th = rotDeg * Math.PI / 180; float c = (float)Math.Cos(th), s = (float)Math.Sin(th);
            float ax = 1f / Math.Max(1e-3f, Math.Abs(sx)), ay = 1f / Math.Max(1e-3f, Math.Abs(sy));
            Parallel.For(0, p.Length / 4, k =>
            {
                int i = k * 4; if (p[i + 3] == 0) return;
                float nx, ny, nz; Decode(p, i, out nx, out ny, out nz);
                nx *= ax * (flipH ? -1 : 1); ny *= ay;
                // 이미지 좌표(y 아래)에서 시계방향 θ 회전 = 탄젠트(V 위) 좌표에서 -θ 회전
                float rx = nx * c + ny * s, ry = -nx * s + ny * c;
                byte a = p[i + 3]; Encode(p, i, rx, ry, nz); p[i + 3] = a;
            });
            var b = new Bitmap(piece.Width, piece.Height, PixelFormat.Format32bppArgb); Pixels.Write(b, p); return b;
        }

        // ------------------------------------------------------------ 파트 → 다른 맵 연동
        /// <summary>
        /// 파트가 덮은 정도(0..1)로 노멀맵 수정.
        /// Raise/Sink: 덮인 영역을 높이로 보고 경계에 요철(패치·자수 / 찢김·구멍), Flatten: 원단 결을 평평하게
        /// </summary>
        public static void ApplyNormalFx(byte[] normal, float[] cov, int w, int h, NormalFx fx, float strength)
        {
            if (fx == NormalFx.None) return;
            if (fx == NormalFx.Flatten)
            {
                Parallel.For(0, cov.Length, k =>
                {
                    float a = Math.Min(1, cov[k] * strength * 1.6f); if (a <= 0) return; int i = k * 4;
                    float nx, ny, nz; Decode(normal, i, out nx, out ny, out nz);
                    Encode(normal, i, nx * (1 - a), ny * (1 - a), nz * (1 - a) + a);
                });
                return;
            }
            float sign = fx == NormalFx.Raise ? 1 : -1;
            var hgt = Blur(cov, w, h, Math.Max(1.5f, w / 512f));
            float k0 = sign * strength * 6f * (w / 1024f);
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int k = y * w + x;
                    float dx = (hgt[y * w + Math.Min(w - 1, x + 1)] - hgt[y * w + Math.Max(0, x - 1)]) * 0.5f * k0;
                    float dy = (hgt[Math.Min(h - 1, y + 1) * w + x] - hgt[Math.Max(0, y - 1) * w + x]) * 0.5f * k0;
                    if (Math.Abs(dx) < 1e-4f && Math.Abs(dy) < 1e-4f) continue;
                    int i = k * 4; float nx, ny, nz; Decode(normal, i, out nx, out ny, out nz);
                    Encode(normal, i, nx - dx, ny + dy, nz);               // 기존 요철에 더함
                }
            });
        }
        /// <summary>파트가 덮은 정도로 디테일마스크 수정 (G 음영 / R 광택)</summary>
        public static void ApplyDetailFx(byte[] detail, float[] cov, int w, int h, DetailFx fx, float strength)
        {
            if (fx == DetailFx.None) return;
            float[] edge = null;
            if (fx == DetailFx.EdgeShade)
            {   // 덮인 영역 바깥쪽 테두리 (블러 - 원본, 양수 부분)
                var bl = Blur(cov, w, h, Math.Max(2f, w / 256f)); edge = new float[cov.Length];
                for (int k = 0; k < cov.Length; k++) edge[k] = Math.Max(0, bl[k] - cov[k]) * 2.5f;
            }
            Parallel.For(0, cov.Length, k =>
            {
                int i = k * 4; float a;
                switch (fx)
                {
                    case DetailFx.EdgeShade: a = Math.Min(1, edge[k] * strength); if (a > 0) detail[i + 1] = (byte)(detail[i + 1] * (1 - a)); break;      // G 낮춤 = 그늘
                    case DetailFx.Darker: a = Math.Min(1, cov[k] * strength); if (a > 0) detail[i + 1] = (byte)(detail[i + 1] * (1 - a)); break;
                    case DetailFx.Clear: a = Math.Min(1, cov[k] * strength); if (a > 0) detail[i + 1] = (byte)(detail[i + 1] + (DetailNeutralG - detail[i + 1]) * a); break;
                    case DetailFx.Gloss: a = Math.Min(1, cov[k] * strength); if (a > 0) detail[i + 2] = (byte)Math.Min(255, detail[i + 2] + (255 - detail[i + 2]) * a); break;
                }
            });
        }

        /// <summary>파트가 결과(UV) 위를 덮는 정도: 일반·구멍 = 알파, 어둡게·곱하기 = 진하기 × 알파</summary>
        public static float[] Coverage(PartLayer p, int w, int h)
        {
            var cov = new float[w * h];
            using (var tmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(tmp))
                using (var m = p.GetMatrix())
                using (var ia = PartLayer.OpacityAttr(p.S.Opacity))
                {
                    g.Clear(Color.Transparent); g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality; g.Transform = m;
                    g.DrawImage(p.Source, new Rectangle(0, 0, p.W, p.H), 0, 0, p.W, p.H, GraphicsUnit.Pixel, ia);
                }
                var px = Pixels.Read(tmp); bool dark = p.S.Blend == BlendKind.Darken || p.S.Blend == BlendKind.Multiply;
                Parallel.For(0, cov.Length, k =>
                {
                    int i = k * 4; float a = px[i + 3] / 255f;
                    if (dark) a *= 1 - (px[i + 2] * 0.299f + px[i + 1] * 0.587f + px[i] * 0.114f) / 255f;
                    cov[k] = a;
                });
            }
            return cov;
        }
        /// <summary>뷰 크기로 그린 파트 이미지(BGRA)에서 덮는 정도를 흰색+알파 이미지로 (3D 투영용)</summary>
        public static byte[] CoverageImage(byte[] rendered, BlendKind blend)
        {
            var o = new byte[rendered.Length]; bool dark = blend == BlendKind.Darken || blend == BlendKind.Multiply;
            Parallel.For(0, o.Length / 4, k =>
            {
                int i = k * 4; float a = dark ? 1 - (rendered[i + 2] * 0.299f + rendered[i + 1] * 0.587f + rendered[i] * 0.114f) / 255f : rendered[i + 3] / 255f;
                o[i] = o[i + 1] = o[i + 2] = 255; o[i + 3] = (byte)Math.Max(0, Math.Min(255, a * 255 + 0.5f));
            });
            return o;
        }
    }
}
