using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace KKSkinTool
{
    public enum ViewKind { UV, Front, Back, Left, Right }

    /// <summary>
    /// 바디 OBJ (Material Editor 내보내기, y 위 / +z 앞).
    /// UV 텍셀마다 3D 위치·법선을 미리 계산해 두고(투영 굽기용), 직교 뷰 렌더링(보기용)을 제공
    /// </summary>
    public class BodyMesh
    {
        public readonly int N;                        // 텍스처 크기 (문서 크기)
        float[] P, Nv; int[] Fp, Ft;          // 정점 위치, 정점 법선(용접 평균), UV 픽셀, 면 인덱스
        public float[] TexPos, TexNrm;                 // 텍셀별 3D 위치 / 법선 (N*N*3)
        public bool[] TexValid;
        public int[] PadSrc;                           // 섬 바깥 4px 패딩 텍셀이 복사해 올 섬 안 텍셀 (-1 = 없음)
        public float MinX, MaxX, MinY, MaxY, MinZ, MaxZ;
        public string Path { get; private set; }

        public BodyMesh(string path, int size)
        {
            N = size; Path = path;
            var v = new List<float>(); var vt = new List<float>(); var fp = new List<int>(); var ft = new List<int>();
            var ci = CultureInfo.InvariantCulture;
            foreach (var raw in File.ReadLines(path))
            {
                if (raw.Length < 2) continue;
                var p = raw.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                if (p[0] == "v") { v.Add(float.Parse(p[1], ci)); v.Add(float.Parse(p[2], ci)); v.Add(float.Parse(p[3], ci)); }
                else if (p[0] == "vt") { vt.Add(float.Parse(p[1], ci)); vt.Add(float.Parse(p[2], ci)); }
                else if (p[0] == "f")
                {
                    var a = new int[p.Length - 1]; var b = new int[p.Length - 1];
                    for (int k = 1; k < p.Length; k++)
                    {
                        var s = p[k].Split('/');
                        int vi = int.Parse(s[0]); a[k - 1] = vi > 0 ? vi - 1 : v.Count / 3 + vi;
                        int ti = s.Length > 1 && s[1].Length > 0 ? int.Parse(s[1]) : 0; b[k - 1] = ti > 0 ? ti - 1 : vt.Count / 2 + ti;
                    }
                    for (int k = 1; k + 1 < a.Length; k++) { fp.Add(a[0]); fp.Add(a[k]); fp.Add(a[k + 1]); ft.Add(b[0]); ft.Add(b[k]); ft.Add(b[k + 1]); }
                }
            }
            if (vt.Count == 0) throw new Exception("OBJ에 UV(vt)가 없습니다.");
            P = v.ToArray(); Fp = fp.ToArray(); Ft = ft.ToArray();
            uvPx = new float[vt.Count];
            for (int i = 0; i < vt.Count / 2; i++) { uvPx[i * 2] = vt[i * 2] * N; uvPx[i * 2 + 1] = (1 - vt[i * 2 + 1]) * N; }
            MinX = MinY = MinZ = float.MaxValue; MaxX = MaxY = MaxZ = float.MinValue;
            for (int i = 0; i < P.Length; i += 3)
            {
                MinX = Math.Min(MinX, P[i]); MaxX = Math.Max(MaxX, P[i]); MinY = Math.Min(MinY, P[i + 1]); MaxY = Math.Max(MaxY, P[i + 1]);
                MinZ = Math.Min(MinZ, P[i + 2]); MaxZ = Math.Max(MaxZ, P[i + 2]);
            }
            SmoothNormals(); BuildTexelMaps();
        }
        float[] uvPx;
        public int FaceCount { get { return Fp.Length / 3; } }

        /// <summary>위치가 같은 정점끼리 법선을 평균 → UV 이음새에서도 법선이 이어짐</summary>
        void SmoothNormals()
        {
            int nv = P.Length / 3; var key = new int[nv]; var map = new Dictionary<long, int>();
            for (int i = 0; i < nv; i++)
            {
                long k = ((long)Math.Round(P[i * 3] * 1e5) * 73856093L) ^ ((long)Math.Round(P[i * 3 + 1] * 1e5) * 19349663L) ^ ((long)Math.Round(P[i * 3 + 2] * 1e5) * 83492791L);
                int id; if (!map.TryGetValue(k, out id)) { id = map.Count; map[k] = id; } key[i] = id;
            }
            var acc = new float[map.Count * 3];
            for (int f = 0; f < Fp.Length; f += 3)
            {
                int a = Fp[f] * 3, b = Fp[f + 1] * 3, c = Fp[f + 2] * 3;
                float ux = P[b] - P[a], uy = P[b + 1] - P[a + 1], uz = P[b + 2] - P[a + 2], vx = P[c] - P[a], vy = P[c + 1] - P[a + 1], vz = P[c + 2] - P[a + 2];
                float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                foreach (var vi in new[] { Fp[f], Fp[f + 1], Fp[f + 2] }) { int k = key[vi] * 3; acc[k] += nx; acc[k + 1] += ny; acc[k + 2] += nz; }
            }
            Nv = new float[P.Length];
            for (int i = 0; i < nv; i++)
            {
                int k = key[i] * 3; float l = (float)Math.Sqrt(acc[k] * acc[k] + acc[k + 1] * acc[k + 1] + acc[k + 2] * acc[k + 2]) + 1e-12f;
                Nv[i * 3] = acc[k] / l; Nv[i * 3 + 1] = acc[k + 1] / l; Nv[i * 3 + 2] = acc[k + 2] / l;
            }
        }

        /// <summary>UV 공간에 삼각형을 래스터화해 텍셀별 3D 위치·법선 기록 + 섬 바깥 패딩 대상 계산</summary>
        void BuildTexelMaps()
        {
            TexPos = new float[N * N * 3]; TexNrm = new float[N * N * 3]; TexValid = new bool[N * N];
            for (int f = 0; f < Fp.Length; f += 3)
            {
                float x0 = uvPx[Ft[f] * 2], y0 = uvPx[Ft[f] * 2 + 1], x1 = uvPx[Ft[f + 1] * 2], y1 = uvPx[Ft[f + 1] * 2 + 1], x2 = uvPx[Ft[f + 2] * 2], y2 = uvPx[Ft[f + 2] * 2 + 1];
                Raster(x0, y0, x1, y1, x2, y2, N, N, 0.02f, (px, py, l0, l1, l2) =>
                {
                    int i = py * N + px; TexValid[i] = true;
                    for (int c = 0; c < 3; c++)
                    {
                        TexPos[i * 3 + c] = l0 * P[Fp[f] * 3 + c] + l1 * P[Fp[f + 1] * 3 + c] + l2 * P[Fp[f + 2] * 3 + c];
                        TexNrm[i * 3 + c] = l0 * Nv[Fp[f] * 3 + c] + l1 * Nv[Fp[f + 1] * 3 + c] + l2 * Nv[Fp[f + 2] * 3 + c];
                    }
                });
            }
            // 패딩: 섬 바깥 4px까지는 가장 가까운 섬 안 텍셀 값을 복사 (BFS)
            PadSrc = Enumerable.Repeat(-1, N * N).ToArray(); var dist = new byte[N * N]; var q = new Queue<int>();
            for (int i = 0; i < N * N; i++) if (TexValid[i]) { PadSrc[i] = i; q.Enqueue(i); }
            while (q.Count > 0)
            {
                int i = q.Dequeue(); if (dist[i] >= 4) continue; int x = i % N, y = i / N;
                foreach (var j in new[] { x > 0 ? i - 1 : -1, x < N - 1 ? i + 1 : -1, y > 0 ? i - N : -1, y < N - 1 ? i + N : -1 })
                    if (j >= 0 && PadSrc[j] < 0) { PadSrc[j] = PadSrc[i]; dist[j] = (byte)(dist[i] + 1); q.Enqueue(j); }
            }
        }

        public delegate void PixelFn(int x, int y, float l0, float l1, float l2);
        public static void Raster(float x0, float y0, float x1, float y1, float x2, float y2, int W, int H, float eps, PixelFn fn)
        {
            int xa = Math.Max(0, (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2)))), xb = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
            int ya = Math.Max(0, (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2)))), yb = Math.Min(H - 1, (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))));
            float den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
            if (Math.Abs(den) < 1e-12f) return;
            for (int y = ya; y <= yb; y++)
                for (int x = xa; x <= xb; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float l0 = ((y1 - y2) * (px - x2) + (x2 - x1) * (py - y2)) / den, l1 = ((y2 - y0) * (px - x2) + (x0 - x2) * (py - y2)) / den, l2 = 1 - l0 - l1;
                    if (l0 >= -eps && l1 >= -eps && l2 >= -eps) fn(x, y, l0, l1, l2);
                }
        }

        // ------------------------------------------------------------ 직교 뷰
        /// <summary>문서와 같은 N×N 크기의 뷰 좌표. 몸 전체가 가운데 들어오도록 같은 축척</summary>
        public void ViewProject(ViewKind v, float x, float y, float z, out float sx, out float sy, out float depth)
        {
            float h = MaxY - MinY, w = Math.Max(MaxX - MinX, MaxZ - MinZ), s = 0.94f * N / Math.Max(h, w);
            float cy = (MaxY + MinY) / 2, cx = (MaxX + MinX) / 2, cz = (MaxZ + MinZ) / 2;
            float a, d;
            switch (v)
            {
                case ViewKind.Back: a = -(x - cx); d = -z; break;               // 뒤에서 봄
                case ViewKind.Left: a = (z - cz); d = -x; break;                 // -x 쪽(UV 왼쪽 몸)에서 봄
                case ViewKind.Right: a = -(z - cz); d = x; break;                // +x 쪽에서 봄
                default: a = x - cx; d = z; break;                               // 정면 (+z에서 봄)
            }
            sx = N / 2f + a * s; sy = N / 2f - (y - cy) * s; depth = d;          // depth가 클수록 카메라에 가까움
        }
        public static void ViewDir(ViewKind v, out float vx, out float vy, out float vz)
        {
            vx = vy = vz = 0;
            switch (v) { case ViewKind.Back: vz = -1; break; case ViewKind.Left: vx = -1; break; case ViewKind.Right: vx = 1; break; default: vz = 1; break; }
        }

        /// <summary>뷰 깊이 버퍼 (가장 가까운 면의 depth, 없으면 -inf) + 픽셀별 UV</summary>
        public void RenderBuffers(ViewKind v, out float[] zbuf, out float[] uvbuf, out float[] shade)
        {
            var z = new float[N * N]; var uv = new float[N * N * 2]; var sh = new float[N * N];
            for (int i = 0; i < z.Length; i++) z[i] = float.NegativeInfinity;
            float vx, vy, vz; ViewDir(v, out vx, out vy, out vz);
            for (int f = 0; f < Fp.Length; f += 3)
            {
                float[] sx = new float[3], sy = new float[3], dd = new float[3];
                for (int k = 0; k < 3; k++) { int p = Fp[f + k] * 3; ViewProject(v, P[p], P[p + 1], P[p + 2], out sx[k], out sy[k], out dd[k]); }
                int f0 = f;
                Raster(sx[0], sy[0], sx[1], sy[1], sx[2], sy[2], N, N, 1e-4f, (x, y, l0, l1, l2) =>
                {
                    int i = y * N + x; float d = l0 * dd[0] + l1 * dd[1] + l2 * dd[2];
                    if (d <= z[i]) return;
                    z[i] = d;
                    uv[i * 2] = l0 * uvPx[Ft[f0] * 2] + l1 * uvPx[Ft[f0 + 1] * 2] + l2 * uvPx[Ft[f0 + 2] * 2];
                    uv[i * 2 + 1] = l0 * uvPx[Ft[f0] * 2 + 1] + l1 * uvPx[Ft[f0 + 1] * 2 + 1] + l2 * uvPx[Ft[f0 + 2] * 2 + 1];
                    float nx = 0, ny = 0, nz = 0;
                    for (int k = 0; k < 3; k++) { int p = Fp[f0 + k] * 3; float l = k == 0 ? l0 : k == 1 ? l1 : l2; nx += l * Nv[p]; ny += l * Nv[p + 1]; nz += l * Nv[p + 2]; }
                    float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz) + 1e-9f;
                    sh[i] = Math.Max(0, (nx * vx + ny * vy + nz * vz) / nl);
                });
            }
            zbuf = z; uvbuf = uv; shade = sh;
        }

        /// <summary>결과 텍스처를 입힌 뷰 이미지 (BGRA)</summary>
        public Bitmap RenderView(ViewKind v, Bitmap texture, float[] zbuf, float[] uvbuf, float[] shade)
        {
            var tex = Pixels.Read(texture); int tw = texture.Width, th = texture.Height;
            var o = new byte[N * N * 4];
            Parallel.For(0, N, y =>
            {
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x, oi = i * 4;
                    if (float.IsNegativeInfinity(zbuf[i])) { o[oi] = 58; o[oi + 1] = 55; o[oi + 2] = 55; o[oi + 3] = 255; continue; }
                    int tx = Math.Min(tw - 1, Math.Max(0, (int)(uvbuf[i * 2] * tw / N))), ty = Math.Min(th - 1, Math.Max(0, (int)(uvbuf[i * 2 + 1] * th / N)));
                    int ti = (ty * tw + tx) * 4; float a = tex[ti + 3] / 255f, lit = 0.55f + 0.45f * shade[i];
                    for (int c = 0; c < 3; c++)
                    {
                        float col = tex[ti + c] * a + 255 * (1 - a);       // 투명한 곳은 흰 피부로
                        o[oi + c] = (byte)Math.Min(255, col * lit);
                    }
                    o[oi + 3] = 255;
                }
            });
            var bmp = new Bitmap(N, N, PixelFormat.Format32bppArgb); Pixels.Write(bmp, o); return bmp;
        }

        // ------------------------------------------------------------ 투영 굽기
        /// <summary>
        /// 뷰 좌표로 그려진 파트(rendered: N×N, 흰 바탕=어둡게/곱하기, 투명 바탕=일반)를 메쉬에 투영해 결과 텍스처에 합성.
        /// 텍셀마다: 뷰로 투영 → 깊이 테스트(가려진 면 제외) → 화면을 향한 정도로 가중 → 합성 → 섬 바깥 패딩
        /// </summary>
        public void Bake(ViewKind v, byte[] rendered, BlendKind blend, byte[] result, float[] zbuf)
        {
            float vx, vy, vz; ViewDir(v, out vx, out vy, out vz);
            float h = MaxY - MinY, w = Math.Max(MaxX - MinX, MaxZ - MinZ), s = 0.94f * N / Math.Max(h, w);
            float tol = 3f / s;                                                   // 3px에 해당하는 깊이 허용치
            var touched = new bool[N * N];
            Parallel.For(0, N, ty =>
            {
                for (int tx = 0; tx < N; tx++)
                {
                    int i = ty * N + tx; if (!TexValid[i]) continue;
                    float px = TexPos[i * 3], py = TexPos[i * 3 + 1], pz = TexPos[i * 3 + 2];
                    float sx, sy, d; ViewProject(v, px, py, pz, out sx, out sy, out d);
                    int ix = (int)sx, iy = (int)sy; if (ix < 0 || iy < 0 || ix >= N || iy >= N) continue;
                    if (d < zbuf[iy * N + ix] - tol * 2) continue;                   // 가려짐
                    float nx = TexNrm[i * 3], ny = TexNrm[i * 3 + 1], nz = TexNrm[i * 3 + 2], nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz) + 1e-9f;
                    float ndv = (nx * vx + ny * vy + nz * vz) / nl;
                    float wgt = Smooth((ndv - 0.05f) / 0.25f);                         // 옆으로 누운 면은 서서히 약하게
                    if (wgt <= 0) continue;
                    int ri = (iy * N + ix) * 4, oi = i * 4;
                    if (blend == BlendKind.Normal)
                    {
                        float a = rendered[ri + 3] / 255f * wgt; if (a <= 0) continue;
                        for (int c = 0; c < 3; c++) result[oi + c] = (byte)(result[oi + c] * (1 - a) + rendered[ri + c] * a + 0.5f);
                        result[oi + 3] = (byte)Math.Max(result[oi + 3], (int)(a * 255));
                    }
                    else
                    {
                        bool any = false;
                        for (int c = 0; c < 3; c++)
                        {
                            float pc = 255 - (255 - rendered[ri + c]) * wgt;             // 가중치만큼 흰색 쪽으로
                            if (pc >= 254.5f) continue; any = true;
                            int bv = result[oi + c];
                            result[oi + c] = (byte)(blend == BlendKind.Darken ? Math.Min(bv, (int)(pc + 0.5f)) : bv * pc / 255f + 0.5f);
                        }
                        if (!any) continue;
                        if (result[oi + 3] < 255)                                         // 투명 문서: 검정+알파로 기록
                        {
                            int dk = 255 - (rendered[ri + 2] * 299 + rendered[ri + 1] * 587 + rendered[ri] * 114) / 1000;
                            result[oi + 3] = (byte)Math.Max(result[oi + 3], (int)(dk * wgt));
                        }
                    }
                    touched[i] = true;
                }
            });
            // 섬 바깥 패딩 갱신
            for (int i = 0; i < N * N; i++)
            {
                int s0 = PadSrc[i]; if (s0 < 0 || s0 == i || !touched[s0]) continue;
                for (int c = 0; c < 4; c++) result[i * 4 + c] = result[s0 * 4 + c];
            }
        }
        static float Smooth(float t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }
    }
}
