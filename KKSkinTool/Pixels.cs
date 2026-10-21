using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace KKSkinTool
{
    /// <summary>32bpp ARGB 비트맵 픽셀 연산 (BGRA 바이트 배열)</summary>
    public static class Pixels
    {
        public static byte[] Read(Bitmap b)
        {
            var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var buf = new byte[b.Width * b.Height * 4];
            for (int y = 0; y < b.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, buf, y * b.Width * 4, b.Width * 4);
            b.UnlockBits(data);
            return buf;
        }

        public static void Write(Bitmap b, byte[] buf)
        {
            var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < b.Height; y++) Marshal.Copy(buf, y * b.Width * 4, data.Scan0 + y * data.Stride, b.Width * 4);
            b.UnlockBits(data);
        }

        public static bool HasTransparency(Bitmap b)
        {
            var p = Read(b);
            for (int i = 3; i < p.Length; i += 4) if (p[i] < 250) return true;
            return false;
        }

        static int Lum(byte[] p, int i) { return (p[i + 2] * 299 + p[i + 1] * 587 + p[i] * 114) / 1000; }

        /// <summary>흰 바탕 회색 이미지를 "검정 + 알파(=진하기)"로 변환. 흰 바탕 위에 그리면 원래와 같은 회색이 됨</summary>
        public static Bitmap DarknessToAlpha(Bitmap src)
        {
            var p = Read(src); var o = new byte[p.Length];
            Parallel.For(0, p.Length / 4, k =>
            {
                int i = k * 4; int a = (255 - Lum(p, i)) * p[i + 3] / 255;
                o[i] = o[i + 1] = o[i + 2] = 0; o[i + 3] = (byte)a;
            });
            var b = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb); Write(b, o); return b;
        }

        /// <summary>와이어프레임을 반투명 선 레이어로 변환 (어두운 선만 남김)</summary>
        public static Bitmap WireOverlay(Bitmap src, Color col, int alpha)
        {
            var p = Read(src); var o = new byte[p.Length];
            Parallel.For(0, p.Length / 4, k =>
            {
                int i = k * 4;
                if (Lum(p, i) < 128 && p[i + 3] > 0) { o[i] = col.B; o[i + 1] = col.G; o[i + 2] = col.R; o[i + 3] = (byte)alpha; }
            });
            var b = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb); Write(b, o); return b;
        }

        /// <summary>어둡게/곱하기 합성. part는 흰 바탕에 렌더링된 파트(파트 밖 = 흰색)</summary>
        public static void BlendInto(Bitmap result, Bitmap part, BlendKind kind)
        {
            var r = Read(result); var s = Read(part);
            Parallel.For(0, r.Length / 4, k =>
            {
                int i = k * 4;
                int sb = s[i], sg = s[i + 1], sr = s[i + 2];
                if (sb == 255 && sg == 255 && sr == 255) return;
                int ra = r[i + 3];
                int d = 255 - (sr * 299 + sg * 587 + sb * 114) / 1000;
                if (ra == 0)
                {
                    // 투명 배경 위: 검정 + 알파(진하기)로 기록 → 흰 바탕 위에 올리면 원래 회색과 같음
                    r[i] = r[i + 1] = r[i + 2] = 0; r[i + 3] = (byte)d;
                    return;
                }
                for (int c = 0; c < 3; c++)
                {
                    int bv = r[i + c], pv = s[i + c];
                    r[i + c] = (byte)(kind == BlendKind.Darken ? Math.Min(bv, pv) : bv * pv / 255);
                }
                if (ra < 255) r[i + 3] = (byte)Math.Max(ra, d);
            });
            Write(result, r);
        }
    }
}
