using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace KKSkinTool
{
    public enum LiquifyTool { Push, Bloat, Pucker, TwirlCW, TwirlCCW, Smooth, Reconstruct }

    /// <summary>
    /// 픽셀 유동화 (포토샵 Liquify 방식).
    /// 시작 시점의 결과 이미지를 원본(basePix)으로 두고, 픽셀마다 "어디서 가져올지" 변위 필드(dx, dy)를 브러시로 편집한다.
    /// 화면 결과 = 원본(x + dx, y + dy)를 양선형 보간으로 샘플링. 원본은 한 번만 샘플링하므로 여러 번 문질러도 흐려지지 않음.
    /// </summary>
    public class LiquifySession
    {
        public readonly int W, H;
        readonly Bitmap target;
        readonly byte[] basePix;          // BGRA
        readonly float[] dx, dy;
        public LiquifyTool Tool = LiquifyTool.Push;
        public float Radius = 80f;        // 문서 픽셀
        public float Strength = 0.5f;     // 0..1
        // 파트 유동화용: 문서 좌표 → 대상 이미지 좌표 변환, 대상 1px이 문서 몇 px인지, 그린 뒤 알림
        public Func<PointF, PointF> MapIn = p => p;
        public float ViewScale = 1f;
        public Action<Rectangle> Rendered;
        public Bitmap Target { get { return target; } }

        // 실행 취소: 획마다 처음 건드린 타일의 변위값을 저장
        const int Tile = 64;
        readonly int tilesX;
        Dictionary<int, float[]> strokeSave;
        readonly List<Dictionary<int, float[]>> undo = new List<Dictionary<int, float[]>>();
        const int MaxUndo = 40;
        PointF last; bool inStroke;
        public bool Changed { get; private set; }

        readonly Bitmap[] extras; readonly byte[][] extraBase;      // 같은 변형을 함께 받는 레이어 (NormalMap/DetailMask)
        public LiquifySession(Bitmap result, Bitmap[] alsoWarp = null)
        {
            target = result; W = result.Width; H = result.Height;
            basePix = Pixels.Read(result);
            extras = alsoWarp == null ? new Bitmap[0] : alsoWarp.Where(b => b != null && b.Width == W && b.Height == H).ToArray();
            extraBase = extras.Select(Pixels.Read).ToArray();
            dx = new float[W * H]; dy = new float[W * H];
            tilesX = (W + Tile - 1) / Tile;
        }

        // ------------------------------------------------------------ 획
        public void BeginStroke(PointF p)
        {
            strokeSave = new Dictionary<int, float[]>(); last = p; inStroke = true;
            if (Tool != LiquifyTool.Push) Dab(p, p);       // 밀기 외에는 누르는 순간에도 적용
        }

        /// <summary>마우스 이동: 브러시 반지름의 1/4 간격으로 찍음</summary>
        public Rectangle MoveTo(PointF p)
        {
            if (!inStroke) return Rectangle.Empty;
            float ddx = p.X - last.X, ddy = p.Y - last.Y, len = (float)Math.Sqrt(ddx * ddx + ddy * ddy);
            float step = Math.Max(1f, Radius * 0.25f);
            var dirty = Rectangle.Empty;
            if (Tool == LiquifyTool.Push)
            {
                if (len < 0.5f) return dirty;
                int n = Math.Max(1, (int)Math.Ceiling(len / step));
                for (int k = 1; k <= n; k++)
                {
                    var a = new PointF(last.X + ddx * (k - 1) / n, last.Y + ddy * (k - 1) / n);
                    var b = new PointF(last.X + ddx * k / n, last.Y + ddy * k / n);
                    dirty = Union(dirty, Dab(b, a));
                }
                last = p;
            }
            else
            {
                if (len < step) return dirty;
                int n = (int)(len / step);
                for (int k = 1; k <= n; k++)
                {
                    var b = new PointF(last.X + ddx * k * step / len, last.Y + ddy * k * step / len);
                    dirty = Union(dirty, Dab(b, b));
                }
                last = new PointF(last.X + ddx * n * step / len, last.Y + ddy * n * step / len);
            }
            return dirty;
        }

        /// <summary>제자리에서 누르고 있을 때(부풀리기/오므리기/소용돌이/복원)용</summary>
        public Rectangle Hold() { return inStroke && Tool != LiquifyTool.Push ? Dab(last, last) : Rectangle.Empty; }

        public void EndStroke()
        {
            if (!inStroke) return; inStroke = false;
            if (strokeSave != null && strokeSave.Count > 0)
            {
                undo.Add(strokeSave); if (undo.Count > MaxUndo) undo.RemoveAt(0);
            }
            strokeSave = null;
        }

        public bool CanUndo { get { return undo.Count > 0; } }
        public void Undo()
        {
            if (undo.Count == 0) return;
            var s = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1);
            var dirty = Rectangle.Empty;
            foreach (var kv in s)
            {
                var r = TileRect(kv.Key); RestoreTile(r, kv.Value); dirty = Union(dirty, r);
            }
            Render(dirty);
        }

        /// <summary>모든 변형을 되돌림 (원본으로)</summary>
        public void ResetAll()
        {
            Array.Clear(dx, 0, dx.Length); Array.Clear(dy, 0, dy.Length); undo.Clear();
            Render(new Rectangle(0, 0, W, H)); Changed = false;
        }

        /// <summary>취소: 결과를 시작 시점 그대로 되돌림</summary>
        public void Cancel()
        {
            Pixels.Write(target, basePix);
            for (int e = 0; e < extras.Length; e++) Pixels.Write(extras[e], extraBase[e]);
            if (Rendered != null) Rendered(new Rectangle(0, 0, W, H));
        }

        // ------------------------------------------------------------ 브러시 한 번
        Rectangle Dab(PointF c, PointF prev)
        {
            int r = (int)Math.Ceiling(Radius);
            var rect = Rectangle.Intersect(new Rectangle((int)c.X - r - 1, (int)c.Y - r - 1, 2 * r + 3, 2 * r + 3), new Rectangle(0, 0, W, H));
            if (rect.Width <= 0 || rect.Height <= 0) return Rectangle.Empty;
            SaveTiles(rect);
            // 이전 값을 읽기 위한 사본 (주변 1px 포함)
            var src = Rectangle.Intersect(Rectangle.Inflate(rect, 2, 2), new Rectangle(0, 0, W, H));
            var ox = new float[src.Width * src.Height]; var oy = new float[ox.Length];
            for (int y = 0; y < src.Height; y++)
            {
                Array.Copy(dx, (src.Y + y) * W + src.X, ox, y * src.Width, src.Width);
                Array.Copy(dy, (src.Y + y) * W + src.X, oy, y * src.Width, src.Width);
            }
            float vx = c.X - prev.X, vy = c.Y - prev.Y, s = Strength, R = Radius, R2 = R * R;
            var tool = Tool;
            Parallel.For(rect.Top, rect.Bottom, y =>
            {
                for (int x = rect.Left; x < rect.Right; x++)
                {
                    float px = x - c.X, py = y - c.Y, d2 = px * px + py * py;
                    if (d2 >= R2) continue;
                    float t = 1 - d2 / R2, f = t * t;                 // 부드러운 감쇠
                    int i = y * W + x;
                    float qx, qy;                                     // 이전 변위를 읽어올 위치
                    switch (tool)
                    {
                        case LiquifyTool.Push:
                            qx = x - vx * f * s; qy = y - vy * f * s; break;
                        case LiquifyTool.Bloat:
                            { float k = 0.04f * s * f; qx = x - px * k; qy = y - py * k; break; }
                        case LiquifyTool.Pucker:
                            { float k = 0.04f * s * f; qx = x + px * k; qy = y + py * k; break; }
                        case LiquifyTool.TwirlCW:
                        case LiquifyTool.TwirlCCW:
                            {
                                float ang = (tool == LiquifyTool.TwirlCW ? -1 : 1) * 0.06f * s * f;
                                float cs = (float)Math.Cos(ang), sn = (float)Math.Sin(ang);
                                qx = c.X + px * cs - py * sn; qy = c.Y + px * sn + py * cs; break;
                            }
                        case LiquifyTool.Smooth:
                            {
                                float k = 0.5f * s * f, ax = 0, ay = 0; int n = 0;
                                for (int yy = -2; yy <= 2; yy++)
                                    for (int xx = -2; xx <= 2; xx++)
                                    {
                                        int sx = x + xx - src.X, sy = y + yy - src.Y;
                                        if (sx < 0 || sy < 0 || sx >= src.Width || sy >= src.Height) continue;
                                        ax += ox[sy * src.Width + sx]; ay += oy[sy * src.Width + sx]; n++;
                                    }
                                int o = (y - src.Y) * src.Width + (x - src.X);
                                dx[i] = ox[o] + (ax / n - ox[o]) * k; dy[i] = oy[o] + (ay / n - oy[o]) * k;
                                continue;
                            }
                        default:   // Reconstruct
                            {
                                float k = 1 - 0.12f * s * f; int o = (y - src.Y) * src.Width + (x - src.X);
                                dx[i] = ox[o] * k; dy[i] = oy[o] * k; continue;
                            }
                    }
                    // 새 변위 = (q 위치의 이전 변위) + (q - p) → 결과(p) = 이전 결과(q)
                    float qdx, qdy; Sample(ox, oy, src, qx, qy, out qdx, out qdy);
                    dx[i] = qdx + (qx - x); dy[i] = qdy + (qy - y);
                }
            });
            Changed = true;
            Render(rect);
            return rect;
        }

        static void Sample(float[] ox, float[] oy, Rectangle src, float x, float y, out float rx, out float ry)
        {
            float fx = x - src.X, fy = y - src.Y;
            fx = Math.Max(0, Math.Min(src.Width - 1.001f, fx)); fy = Math.Max(0, Math.Min(src.Height - 1.001f, fy));
            int x0 = (int)fx, y0 = (int)fy; float ax = fx - x0, ay = fy - y0; int w = src.Width;
            int i00 = y0 * w + x0, i10 = i00 + 1, i01 = i00 + w, i11 = i01 + 1;
            rx = (ox[i00] * (1 - ax) + ox[i10] * ax) * (1 - ay) + (ox[i01] * (1 - ax) + ox[i11] * ax) * ay;
            ry = (oy[i00] * (1 - ax) + oy[i10] * ax) * (1 - ay) + (oy[i01] * (1 - ax) + oy[i11] * ax) * ay;
        }

        // ------------------------------------------------------------ 결과 그리기
        /// <summary>rect 안의 결과 = 원본(p + 변위)을 양선형 보간</summary>
        public void Render(Rectangle rect)
        {
            rect = Rectangle.Intersect(rect, new Rectangle(0, 0, W, H));
            if (rect.Width <= 0 || rect.Height <= 0) return;
            RenderOne(basePix, target, rect);
            for (int e = 0; e < extras.Length; e++) RenderOne(extraBase[e], extras[e], rect);
            if (Rendered != null) Rendered(rect);
        }
        void RenderOne(byte[] src, Bitmap dst, Rectangle rect)
        {
            var buf = new byte[rect.Width * rect.Height * 4];
            Parallel.For(0, rect.Height, ry =>
            {
                int y = rect.Y + ry;
                for (int rx = 0; rx < rect.Width; rx++)
                {
                    int x = rect.X + rx, i = y * W + x;
                    float sx = x + dx[i], sy = y + dy[i];
                    sx = Math.Max(0, Math.Min(W - 1.001f, sx)); sy = Math.Max(0, Math.Min(H - 1.001f, sy));
                    int x0 = (int)sx, y0 = (int)sy; float ax = sx - x0, ay = sy - y0;
                    int p00 = (y0 * W + x0) * 4, p10 = p00 + 4, p01 = p00 + W * 4, p11 = p01 + 4, o = (ry * rect.Width + rx) * 4;
                    for (int ch = 0; ch < 4; ch++)
                    {
                        float v = (src[p00 + ch] * (1 - ax) + src[p10 + ch] * ax) * (1 - ay) + (src[p01 + ch] * (1 - ax) + src[p11 + ch] * ax) * ay;
                        buf[o + ch] = (byte)(v + 0.5f);
                    }
                }
            });
            // 전체를 잠그고 해당 행 구간만 씀 (부분 영역 LockBits는 환경에 따라 동작이 달라 피함)
            var data = dst.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            for (int y = 0; y < rect.Height; y++)
                Marshal.Copy(buf, y * rect.Width * 4, data.Scan0 + (rect.Y + y) * data.Stride + rect.X * 4, rect.Width * 4);
            dst.UnlockBits(data);
        }

        // ------------------------------------------------------------ 타일 저장/복원
        Rectangle TileRect(int t) { int tx = t % tilesX, ty = t / tilesX; return Rectangle.Intersect(new Rectangle(tx * Tile, ty * Tile, Tile, Tile), new Rectangle(0, 0, W, H)); }
        void SaveTiles(Rectangle r)
        {
            if (strokeSave == null) return;
            for (int ty = r.Top / Tile; ty <= (r.Bottom - 1) / Tile; ty++)
                for (int tx = r.Left / Tile; tx <= (r.Right - 1) / Tile; tx++)
                {
                    int t = ty * tilesX + tx; if (strokeSave.ContainsKey(t)) continue;
                    var tr = TileRect(t); var a = new float[tr.Width * tr.Height * 2];
                    for (int y = 0; y < tr.Height; y++)
                    {
                        Array.Copy(dx, (tr.Y + y) * W + tr.X, a, y * tr.Width, tr.Width);
                        Array.Copy(dy, (tr.Y + y) * W + tr.X, a, tr.Width * tr.Height + y * tr.Width, tr.Width);
                    }
                    strokeSave[t] = a;
                }
        }
        void RestoreTile(Rectangle tr, float[] a)
        {
            for (int y = 0; y < tr.Height; y++)
            {
                Array.Copy(a, y * tr.Width, dx, (tr.Y + y) * W + tr.X, tr.Width);
                Array.Copy(a, tr.Width * tr.Height + y * tr.Width, dy, (tr.Y + y) * W + tr.X, tr.Width);
            }
        }
        static Rectangle Union(Rectangle a, Rectangle b) { return a.IsEmpty ? b : b.IsEmpty ? a : Rectangle.Union(a, b); }
    }
}
