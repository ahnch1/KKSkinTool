using System;
using System.Collections.Generic;
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

        /// <summary>
        /// 탠라인(흰 바탕 + 어두운 영역) → 타투 오버레이용 (투명 바탕 + 흰색).
        /// 알파 = 진하기(255 - 밝기). normalize면 가장 진한 곳을 알파 255로 늘림 (경계의 부드러움은 유지)
        /// </summary>
        public static int TanToWhiteAlpha(byte[] px, bool normalize)
        {
            int n = px.Length / 4, max = 0;
            var d = new int[n];
            for (int k = 0; k < n; k++)
            {
                int i = k * 4;
                int dk = (255 - Lum(px, i)) * px[i + 3] / 255;           // 이미 투명한 곳은 진하기 0
                d[k] = dk; if (dk > max) max = dk;
            }
            if (max == 0) return 0;
            Parallel.For(0, n, k =>
            {
                int i = k * 4, a = normalize ? Math.Min(255, d[k] * 255 / max) : d[k];
                if (a < 2) a = 0;                                          // 미세한 잡티 제거
                px[i] = px[i + 1] = px[i + 2] = 255; px[i + 3] = (byte)a;
            });
            return max;
        }

        /// <summary>
        /// 마스크(box 크기, 128 이상 = 비운 곳) 영역을 바깥에서부터 이웃 평균으로 채움 (양파 껍질 방식).
        /// 옷 텍스처에서 조각을 옮긴 자리를 원단처럼 메우는 용도
        /// </summary>
        public static void InpaintRegion(byte[] px, int W, int H, Rectangle box, byte[] mask)
        {
            var hole = new bool[box.Width * box.Height]; int left = 0;
            for (int i = 0; i < hole.Length; i++) if (mask[i] >= 128) { hole[i] = true; left++; }
            var next = new List<int>();
            for (int guard = 0; left > 0 && guard < 4096; guard++)
            {
                next.Clear();
                for (int y = 0; y < box.Height; y++)
                    for (int x = 0; x < box.Width; x++)
                    {
                        int m = y * box.Width + x; if (!hole[m]) continue;
                        int gx = box.X + x, gy = box.Y + y; bool edge = false;
                        for (int k = 0; k < 4 && !edge; k++)
                        {
                            int nx = gx + (k == 0 ? -1 : k == 1 ? 1 : 0), ny = gy + (k == 2 ? -1 : k == 3 ? 1 : 0);
                            if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                            int lx = nx - box.X, ly = ny - box.Y;
                            bool inBox = lx >= 0 && ly >= 0 && lx < box.Width && ly < box.Height;
                            if (!inBox || !hole[ly * box.Width + lx]) edge = true;
                        }
                        if (edge) next.Add(m);
                    }
                if (next.Count == 0) break;
                foreach (var m in next)
                {
                    int gx = box.X + m % box.Width, gy = box.Y + m / box.Width; int[] acc = new int[4]; int n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = gx + dx, ny = gy + dy; if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                            int lx = nx - box.X, ly = ny - box.Y;
                            if (lx >= 0 && ly >= 0 && lx < box.Width && ly < box.Height && hole[ly * box.Width + lx]) continue;
                            int i = (ny * W + nx) * 4; for (int c = 0; c < 4; c++) acc[c] += px[i + c]; n++;
                        }
                    if (n == 0) continue;
                    int o = (gy * W + gx) * 4; for (int c = 0; c < 4; c++) px[o + c] = (byte)(acc[c] / n);
                }
                foreach (var m in next) { hole[m] = false; left--; }
            }
        }

        /// <summary>구멍: part(투명 바탕에 그린 파트)의 알파만큼 target의 알파를 지움</summary>
        public static void EraseInto(Bitmap target, Bitmap part)
        {
            var r = Read(target); var s = Read(part);
            Parallel.For(0, r.Length / 4, k => { int i = k * 4; int a = s[i + 3]; if (a > 0) r[i + 3] = (byte)(r[i + 3] * (255 - a) / 255); });
            Write(target, r);
        }

        /// <summary>DarknessToAlpha를 rect 영역만 다시 계산 (dst는 src와 같은 크기)</summary>
        public static void DarknessToAlphaRegion(Bitmap src, Bitmap dst, Rectangle r)
        {
            r = Rectangle.Intersect(r, new Rectangle(0, 0, src.Width, src.Height)); if (r.Width <= 0 || r.Height <= 0) return;
            var full = new Rectangle(0, 0, src.Width, src.Height);
            var ds = src.LockBits(full, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var dd = dst.LockBits(full, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            var row = new byte[r.Width * 4]; var o = new byte[r.Width * 4];
            for (int y = r.Top; y < r.Bottom; y++)
            {
                Marshal.Copy(ds.Scan0 + y * ds.Stride + r.X * 4, row, 0, row.Length);
                for (int k = 0; k < r.Width; k++)
                {
                    int i = k * 4; int a = (255 - Lum(row, i)) * row[i + 3] / 255;
                    o[i] = o[i + 1] = o[i + 2] = 0; o[i + 3] = (byte)a;
                }
                Marshal.Copy(o, 0, dd.Scan0 + y * dd.Stride + r.X * 4, o.Length);
            }
            src.UnlockBits(ds); dst.UnlockBits(dd);
        }

        /// <summary>
        /// 선택 영역 잘라내기: poly(대상 픽셀 좌표) 안의 픽셀을 새 비트맵으로 복사하고 원본 자리는 fill로 채움.
        /// 반환 비트맵은 다각형 경계 상자 크기, 다각형 밖은 투명. origin = 경계 상자 왼쪽 위
        /// </summary>
        public static Bitmap Lift(Bitmap target, PointF[] poly, Color fill, out Point origin)
        {
            byte[] mask; Rectangle box; return Lift(target, poly, fill, out origin, out mask, out box, false);
        }
        /// <summary>Lift + 잘라낸 자리 마스크 반환. inpaint면 fill 대신 주변 색으로 메움</summary>
        public static Bitmap Lift(Bitmap target, PointF[] poly, Color fill, out Point origin, out byte[] maskOut, out Rectangle boxOut, bool inpaint)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var p in poly) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
            var box = Rectangle.Intersect(Rectangle.FromLTRB((int)Math.Floor(x0), (int)Math.Floor(y0), (int)Math.Ceiling(x1), (int)Math.Ceiling(y1)), new Rectangle(0, 0, target.Width, target.Height));
            origin = box.Location; maskOut = null; boxOut = box;
            if (box.Width < 2 || box.Height < 2) return null;
            var mask = new byte[box.Width * box.Height];
            using (var mb = new Bitmap(box.Width, box.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(mb))
                {
                    g.Clear(Color.Black); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                    var pp = Array.ConvertAll(poly, p => new PointF(p.X - box.X, p.Y - box.Y));
                    g.FillPolygon(Brushes.White, pp);
                }
                var mp = Read(mb); for (int i = 0; i < mask.Length; i++) mask[i] = mp[i * 4 + 2];
            }
            var all = Read(target); var cut = new byte[box.Width * box.Height * 4];
            for (int y = 0; y < box.Height; y++)
                for (int x = 0; x < box.Width; x++)
                {
                    int m = y * box.Width + x; if (mask[m] < 128) continue;
                    int si = ((box.Y + y) * target.Width + box.X + x) * 4, di = m * 4;
                    for (int c = 0; c < 4; c++) cut[di + c] = all[si + c];
                    all[si] = fill.B; all[si + 1] = fill.G; all[si + 2] = fill.R; all[si + 3] = fill.A;
                }
            if (inpaint) InpaintRegion(all, target.Width, target.Height, box, mask);
            Write(target, all); maskOut = mask;
            var res = new Bitmap(box.Width, box.Height, PixelFormat.Format32bppArgb); Write(res, cut); return res;
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
