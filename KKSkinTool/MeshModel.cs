using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace KKSkinTool
{
    public enum ViewKind { UV, Front, Back, Left, Right }

    /// <summary>OBJ 파일 내용 (그룹 선택 전). 그룹 = o/g 이름 + usemtl</summary>
    public class ObjFile
    {
        public float[] V, VT; public int[] Fp, Ft; public string[] FaceGroup;
        public Dictionary<string, int> Groups = new Dictionary<string, int>();
        public string Path;

        public static ObjFile Load(string path)
        {
            var o = new ObjFile { Path = path };
            var v = new List<float>(); var vt = new List<float>(); var fp = new List<int>(); var ft = new List<int>(); var fg = new List<string>();
            var ci = CultureInfo.InvariantCulture; string og = "default", mtl = "", group = "default";
            foreach (var raw in File.ReadLines(path))
            {
                if (raw.Length < 2) continue;
                var p = raw.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                switch (p[0])
                {
                    case "v": v.Add(float.Parse(p[1], ci)); v.Add(float.Parse(p[2], ci)); v.Add(float.Parse(p[3], ci)); break;
                    case "vt": vt.Add(float.Parse(p[1], ci)); vt.Add(float.Parse(p[2], ci)); break;
                    case "o": case "g": og = string.Join(" ", p.Skip(1)); group = mtl.Length > 0 ? og + " | " + mtl : og; break;
                    case "usemtl": mtl = string.Join(" ", p.Skip(1)); group = og + " | " + mtl; break;
                    case "f":
                        {
                            var a = new int[p.Length - 1]; var b = new int[p.Length - 1];
                            for (int k = 1; k < p.Length; k++)
                            {
                                var s = p[k].Split('/');
                                int vi = int.Parse(s[0]); a[k - 1] = vi > 0 ? vi - 1 : v.Count / 3 + vi;
                                int ti = s.Length > 1 && s[1].Length > 0 ? int.Parse(s[1]) : 0; b[k - 1] = ti > 0 ? ti - 1 : (ti < 0 ? vt.Count / 2 + ti : -1);
                            }
                            for (int k = 1; k + 1 < a.Length; k++)
                            {
                                fp.Add(a[0]); fp.Add(a[k]); fp.Add(a[k + 1]); ft.Add(b[0]); ft.Add(b[k]); ft.Add(b[k + 1]); fg.Add(group);
                                int c; o.Groups.TryGetValue(group, out c); o.Groups[group] = c + 1;
                            }
                            break;
                        }
                }
            }
            if (vt.Count == 0) throw new Exception("OBJ에 UV(vt)가 없습니다. UV 포함으로 내보낸 OBJ가 필요합니다.");
            o.V = v.ToArray(); o.VT = vt.ToArray(); o.Fp = fp.ToArray(); o.Ft = ft.ToArray(); o.FaceGroup = fg.ToArray();
            return o;
        }
    }

    /// <summary>
    /// 범용 3D 메쉬 (바디·의상 모두). Material Editor 내보내기 기준 y 위.
    /// · 텍셀별 3D 위치·법선(투영 굽기용, 유효 텍셀만 압축 저장)
    /// · 직교 뷰 렌더링(보기용), 투영 굽기, 메쉬 UV 와이어 생성
    /// · 텍스처 크기는 문서 크기(가로·세로 따로, 512~4096 등)
    /// </summary>
    public class MeshModel
    {
        public readonly int TW, TH;                    // 텍스처(문서) 크기
        public readonly string FrontAxis;              // "+z" "-z" "+x" "-x" (캐릭터가 바라보는 방향)
        public string Path { get; private set; }
        public string[] SelectedGroups { get; private set; }
        float[] P, Nv, uvPx; int[] Fp, Ft;
        // 유효 텍셀만: 인덱스, 3D 위치, 법선
        int[] texIdx; float[] texPos, texNrm; int[] texSlot;    // texSlot[i] = texIdx 안 위치 (-1 = 비어 있음)
        int[] padSrc;                                           // 섬 바깥 4px 패딩이 값을 가져올 텍셀 (-1 = 없음)
        float minX, maxX, minY, maxY, minZ, maxZ;
        public int FaceCount { get { return Fp.Length / 3; } }
        public int ValidTexels { get { return texIdx.Length; } }

        public MeshModel(ObjFile obj, IEnumerable<string> groups, int texW, int texH, string frontAxis = "+z")
        {
            TW = texW; TH = texH; FrontAxis = frontAxis; Path = obj.Path;
            var sel = new HashSet<string>(groups ?? obj.Groups.Keys); SelectedGroups = sel.ToArray();
            var fp = new List<int>(); var ft = new List<int>();
            for (int f = 0; f < obj.FaceGroup.Length; f++)
            {
                if (!sel.Contains(obj.FaceGroup[f])) continue;
                if (obj.Ft[f * 3] < 0 || obj.Ft[f * 3 + 1] < 0 || obj.Ft[f * 3 + 2] < 0) continue;     // UV 없는 면 제외
                for (int k = 0; k < 3; k++) { fp.Add(obj.Fp[f * 3 + k]); ft.Add(obj.Ft[f * 3 + k]); }
            }
            if (fp.Count == 0) throw new Exception("선택한 그룹에 UV가 있는 면이 없습니다.");
            Fp = fp.ToArray(); Ft = ft.ToArray();
            // 앞 방향을 +z로 맞춰 저장 (나머지 계산은 모두 +z 기준)
            P = new float[obj.V.Length];
            for (int i = 0; i < P.Length; i += 3)
            {
                float x = obj.V[i], y = obj.V[i + 1], z = obj.V[i + 2];
                switch (frontAxis) { case "-z": x = -x; z = -z; break; case "+x": { var t = x; x = -z; z = t; break; } case "-x": { var t = x; x = z; z = -t; break; } }
                P[i] = x; P[i + 1] = y; P[i + 2] = z;
            }
            uvPx = new float[obj.VT.Length];
            for (int i = 0; i < obj.VT.Length / 2; i++) { uvPx[i * 2] = obj.VT[i * 2] * TW; uvPx[i * 2 + 1] = (1 - obj.VT[i * 2 + 1]) * TH; }
            var used = new HashSet<int>(Fp);
            minX = minY = minZ = float.MaxValue; maxX = maxY = maxZ = float.MinValue;
            foreach (var vi in used)
            {
                int i = vi * 3;
                minX = Math.Min(minX, P[i]); maxX = Math.Max(maxX, P[i]); minY = Math.Min(minY, P[i + 1]); maxY = Math.Max(maxY, P[i + 1]);
                minZ = Math.Min(minZ, P[i + 2]); maxZ = Math.Max(maxZ, P[i + 2]);
            }
            SmoothNormals(); BuildTexelMaps();
        }

        /// <summary>위치가 같은 정점끼리 법선 평균 (UV 이음새에서도 이어짐). 같은 위치라도 반대 방향 면(양면 의상 안쪽)은 따로</summary>
        void SmoothNormals()
        {
            int nv = P.Length / 3; var fn = new float[Fp.Length];      // 면 법선 (면마다 3개 값)
            for (int f = 0; f < Fp.Length; f += 3)
            {
                int a = Fp[f] * 3, b = Fp[f + 1] * 3, c = Fp[f + 2] * 3;
                float ux = P[b] - P[a], uy = P[b + 1] - P[a + 1], uz = P[b + 2] - P[a + 2], vx = P[c] - P[a], vy = P[c + 1] - P[a + 1], vz = P[c + 2] - P[a + 2];
                fn[f] = uy * vz - uz * vy; fn[f + 1] = uz * vx - ux * vz; fn[f + 2] = ux * vy - uy * vx;
            }
            var key = new long[nv];
            for (int i = 0; i < nv; i++)
                key[i] = ((long)Math.Round(P[i * 3] * 1e5) * 73856093L) ^ ((long)Math.Round(P[i * 3 + 1] * 1e5) * 19349663L) ^ ((long)Math.Round(P[i * 3 + 2] * 1e5) * 83492791L);
            // 정점별 원래 법선(자기 면들) → 같은 위치 정점 중 방향이 비슷한(내적>0) 것끼리만 합침
            var own = new float[nv * 3];
            for (int f = 0; f < Fp.Length; f += 3)
                for (int k = 0; k < 3; k++) { int v = Fp[f + k] * 3; own[v] += fn[f]; own[v + 1] += fn[f + 1]; own[v + 2] += fn[f + 2]; }
            var byKey = new Dictionary<long, List<int>>();
            for (int i = 0; i < nv; i++) { List<int> l; if (!byKey.TryGetValue(key[i], out l)) byKey[key[i]] = l = new List<int>(); l.Add(i); }
            Nv = new float[nv * 3];
            foreach (var l in byKey.Values)
                foreach (var i in l)
                {
                    float x = 0, y = 0, z = 0;
                    foreach (var j in l)
                        if (own[i * 3] * own[j * 3] + own[i * 3 + 1] * own[j * 3 + 1] + own[i * 3 + 2] * own[j * 3 + 2] >= 0) { x += own[j * 3]; y += own[j * 3 + 1]; z += own[j * 3 + 2]; }
                    float n = (float)Math.Sqrt(x * x + y * y + z * z) + 1e-12f;
                    Nv[i * 3] = x / n; Nv[i * 3 + 1] = y / n; Nv[i * 3 + 2] = z / n;
                }
        }

        /// <summary>
        /// UV 공간 래스터 → 텍셀별 3D 위치·법선.
        /// 같은 텍셀에 면이 여럿 겹치면(양면 의상의 안쪽 면 등) 메쉬 바깥을 향하는 면을 우선
        /// </summary>
        void BuildTexelMaps()
        {
            int N = TW * TH; texSlot = Enumerable.Repeat(-1, N).ToArray();
            // 예상 텍셀 수(UV 삼각형 면적 합)로 미리 용량을 잡아 4096에서도 리스트 재할당으로 메모리가 튀지 않게
            double area = 0;
            for (int f = 0; f < Fp.Length; f += 3)
            {
                float ax = uvPx[Ft[f] * 2], ay = uvPx[Ft[f] * 2 + 1], bx = uvPx[Ft[f + 1] * 2], by = uvPx[Ft[f + 1] * 2 + 1], qx = uvPx[Ft[f + 2] * 2], qy = uvPx[Ft[f + 2] * 2 + 1];
                area += Math.Abs((bx - ax) * (qy - ay) - (qx - ax) * (by - ay)) / 2;
            }
            int cap = (int)Math.Min(N, area * 1.1 + 1024);
            var pos = new List<float>(cap * 3); var nrm = new List<float>(cap * 3); var idx = new List<int>(cap); var score = new List<float>(cap);
            float cx = (minX + maxX) / 2, cy = (minY + maxY) / 2, cz = (minZ + maxZ) / 2;
            for (int f = 0; f < Fp.Length; f += 3)
            {
                float x0 = uvPx[Ft[f] * 2], y0 = uvPx[Ft[f] * 2 + 1], x1 = uvPx[Ft[f + 1] * 2], y1 = uvPx[Ft[f + 1] * 2 + 1], x2 = uvPx[Ft[f + 2] * 2], y2 = uvPx[Ft[f + 2] * 2 + 1];
                // 0~1 밖 UV(타일링): 삼각형을 통째로 0~1 칸으로 옮김
                float sx = (float)Math.Floor(Math.Min(x0, Math.Min(x1, x2)) / TW) * TW, sy = (float)Math.Floor(Math.Min(y0, Math.Min(y1, y2)) / TH) * TH;
                x0 -= sx; x1 -= sx; x2 -= sx; y0 -= sy; y1 -= sy; y2 -= sy;
                int f0 = f;
                Raster(x0, y0, x1, y1, x2, y2, TW, TH, 0.02f, (px, py, l0, l1, l2) =>
                {
                    int i = py * TW + px; float p0 = 0, p1 = 0, p2 = 0, n0 = 0, n1 = 0, n2 = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        int v = Fp[f0 + k] * 3; float l = k == 0 ? l0 : k == 1 ? l1 : l2;
                        p0 += l * P[v]; p1 += l * P[v + 1]; p2 += l * P[v + 2]; n0 += l * Nv[v]; n1 += l * Nv[v + 1]; n2 += l * Nv[v + 2];
                    }
                    float sc = n0 * (p0 - cx) + n1 * (p1 - cy) + n2 * (p2 - cz);       // 바깥을 향할수록 큼
                    int slot = texSlot[i];
                    if (slot < 0) { slot = idx.Count; texSlot[i] = slot; idx.Add(i); pos.Add(0); pos.Add(0); pos.Add(0); nrm.Add(0); nrm.Add(0); nrm.Add(0); score.Add(float.MinValue); }
                    if (sc < score[slot]) return;
                    score[slot] = sc;
                    pos[slot * 3] = p0; pos[slot * 3 + 1] = p1; pos[slot * 3 + 2] = p2; nrm[slot * 3] = n0; nrm[slot * 3 + 1] = n1; nrm[slot * 3 + 2] = n2;
                });
            }
            texIdx = idx.ToArray(); texPos = pos.ToArray(); texNrm = nrm.ToArray();
            // 패딩
            padSrc = Enumerable.Repeat(-1, N).ToArray(); var dist = new byte[N]; var q = new Queue<int>();
            foreach (var i in texIdx) { padSrc[i] = i; q.Enqueue(i); }
            while (q.Count > 0)
            {
                int i = q.Dequeue(); if (dist[i] >= 4) continue; int x = i % TW, y = i / TW;
                for (int k = 0; k < 4; k++)
                {
                    int j = k == 0 ? (x > 0 ? i - 1 : -1) : k == 1 ? (x < TW - 1 ? i + 1 : -1) : k == 2 ? (y > 0 ? i - TW : -1) : (y < TH - 1 ? i + TW : -1);
                    if (j >= 0 && padSrc[j] < 0) { padSrc[j] = padSrc[i]; dist[j] = (byte)(dist[i] + 1); q.Enqueue(j); }
                }
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

        // ------------------------------------------------------------ 직교 뷰 (문서와 같은 TW×TH 크기)
        float ViewScale() { float h = maxY - minY, w = Math.Max(maxX - minX, maxZ - minZ); return 0.94f * Math.Min(TW, TH) / Math.Max(h, Math.Max(w, 1e-6f)); }
        public void ViewProject(ViewKind v, float x, float y, float z, out float sx, out float sy, out float depth)
        {
            float s = ViewScale(), cy = (maxY + minY) / 2, cx = (maxX + minX) / 2, cz = (maxZ + minZ) / 2, a, d;
            switch (v)
            {
                case ViewKind.Back: a = -(x - cx); d = -z; break;
                case ViewKind.Left: a = (z - cz); d = -x; break;
                case ViewKind.Right: a = -(z - cz); d = x; break;
                default: a = x - cx; d = z; break;
            }
            sx = TW / 2f + a * s; sy = TH / 2f - (y - cy) * s; depth = d;
        }
        public static void ViewDir(ViewKind v, out float vx, out float vy, out float vz)
        {
            vx = vy = vz = 0;
            switch (v) { case ViewKind.Back: vz = -1; break; case ViewKind.Left: vx = -1; break; case ViewKind.Right: vx = 1; break; default: vz = 1; break; }
        }

        public void RenderBuffers(ViewKind v, out float[] zbuf, out float[] uvbuf, out float[] shade)
        {
            int N = TW * TH; var z = new float[N]; var uv = new float[N * 2]; var sh = new float[N];
            for (int i = 0; i < N; i++) z[i] = float.NegativeInfinity;
            float vx, vy, vz; ViewDir(v, out vx, out vy, out vz);
            var sx = new float[3]; var sy = new float[3]; var dd = new float[3];
            for (int f = 0; f < Fp.Length; f += 3)
            {
                for (int k = 0; k < 3; k++) { int p = Fp[f + k] * 3; ViewProject(v, P[p], P[p + 1], P[p + 2], out sx[k], out sy[k], out dd[k]); }
                int f0 = f; float d0 = dd[0], d1 = dd[1], d2 = dd[2];
                float u0 = uvPx[Ft[f0] * 2], u1 = uvPx[Ft[f0 + 1] * 2], u2 = uvPx[Ft[f0 + 2] * 2], w0 = uvPx[Ft[f0] * 2 + 1], w1 = uvPx[Ft[f0 + 1] * 2 + 1], w2 = uvPx[Ft[f0 + 2] * 2 + 1];
                Raster(sx[0], sy[0], sx[1], sy[1], sx[2], sy[2], TW, TH, 1e-4f, (x, y, l0, l1, l2) =>
                {
                    int i = y * TW + x; float d = l0 * d0 + l1 * d1 + l2 * d2;
                    if (d <= z[i]) return;
                    z[i] = d; uv[i * 2] = l0 * u0 + l1 * u1 + l2 * u2; uv[i * 2 + 1] = l0 * w0 + l1 * w1 + l2 * w2;
                    float nx = 0, ny = 0, nz = 0;
                    for (int k = 0; k < 3; k++) { int p = Fp[f0 + k] * 3; float l = k == 0 ? l0 : k == 1 ? l1 : l2; nx += l * Nv[p]; ny += l * Nv[p + 1]; nz += l * Nv[p + 2]; }
                    float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz) + 1e-9f;
                    sh[i] = Math.Abs((nx * vx + ny * vy + nz * vz) / nl);      // 양면(안쪽)도 밝게 보이게
                });
            }
            zbuf = z; uvbuf = uv; shade = sh;
        }

        public Bitmap RenderView(ViewKind v, Bitmap texture, float[] zbuf, float[] uvbuf, float[] shade)
        {
            var tex = Pixels.Read(texture); int tw = texture.Width, th = texture.Height;
            var o = new byte[TW * TH * 4];
            Parallel.For(0, TH, y =>
            {
                for (int x = 0; x < TW; x++)
                {
                    int i = y * TW + x, oi = i * 4;
                    if (float.IsNegativeInfinity(zbuf[i])) { o[oi] = 58; o[oi + 1] = 55; o[oi + 2] = 55; o[oi + 3] = 255; continue; }
                    float u = uvbuf[i * 2] / TW, w = uvbuf[i * 2 + 1] / TH; u -= (float)Math.Floor(u); w -= (float)Math.Floor(w);
                    int tx = Math.Min(tw - 1, (int)(u * tw)), ty = Math.Min(th - 1, (int)(w * th));
                    int ti = (ty * tw + tx) * 4; float a = tex[ti + 3] / 255f, lit = 0.55f + 0.45f * shade[i];
                    for (int c = 0; c < 3; c++) o[oi + c] = (byte)Math.Min(255, (tex[ti + c] * a + 255 * (1 - a)) * lit);
                    o[oi + 3] = 255;
                }
            });
            var bmp = new Bitmap(TW, TH, PixelFormat.Format32bppArgb); Pixels.Write(bmp, o); return bmp;
        }

        /// <summary>메쉬의 UV 와이어 (반투명 선) — 문서 크기</summary>
        public Bitmap RenderUVWire(Color col)
        {
            var bmp = new Bitmap(TW, TH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            using (var pen = new Pen(col, Math.Max(1f, TW / 2048f)))
            {
                g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
                var pts = new PointF[3];
                for (int f = 0; f < Fp.Length; f += 3)
                {
                    for (int k = 0; k < 3; k++) pts[k] = new PointF(uvPx[Ft[f + k] * 2], uvPx[Ft[f + k] * 2 + 1]);
                    float sx = (float)Math.Floor(Math.Min(pts[0].X, Math.Min(pts[1].X, pts[2].X)) / TW) * TW, sy = (float)Math.Floor(Math.Min(pts[0].Y, Math.Min(pts[1].Y, pts[2].Y)) / TH) * TH;
                    for (int k = 0; k < 3; k++) pts[k] = new PointF(pts[k].X - sx, pts[k].Y - sy);
                    g.DrawPolygon(pen, pts);
                }
            }
            return bmp;
        }

        // ------------------------------------------------------------ 뷰 이미지 내보내기 / 불러오기 (외부 툴에서 칠하기)
        /// <summary>편집용 뷰: 텍스처 색 그대로(음영 없음), 메쉬 밖은 투명</summary>
        public Bitmap RenderAlbedo(Bitmap texture, float[] zbuf, float[] uvbuf)
        {
            var tex = Pixels.Read(texture); int tw = texture.Width, th = texture.Height;
            var o = new byte[TW * TH * 4];
            Parallel.For(0, TH, y =>
            {
                for (int x = 0; x < TW; x++)
                {
                    int i = y * TW + x, oi = i * 4;
                    if (float.IsNegativeInfinity(zbuf[i])) continue;
                    float u = uvbuf[i * 2] / TW, w = uvbuf[i * 2 + 1] / TH; u -= (float)Math.Floor(u); w -= (float)Math.Floor(w);
                    int ti = (Math.Min(th - 1, (int)(w * th)) * tw + Math.Min(tw - 1, (int)(u * tw))) * 4;
                    o[oi] = tex[ti]; o[oi + 1] = tex[ti + 1]; o[oi + 2] = tex[ti + 2];
                    o[oi + 3] = Math.Max((byte)1, tex[ti + 3]);           // 메쉬 위는 완전 투명이 되지 않게 (밖과 구분)
                }
            });
            var bmp = new Bitmap(TW, TH, PixelFormat.Format32bppArgb); Pixels.Write(bmp, o); return bmp;
        }

        /// <summary>
        /// 여러 뷰에서 칠한 이미지를 한 번에 굽기 (kk_projbake 방식).
        /// 텍셀마다 보이는 뷰들 중 "원본과 달라진 픽셀"에서만 색을 받아 법선 가중 평균. 어느 뷰에서도 안 바뀐 텍셀은 그대로.
        /// views: (보기, 편집 이미지 BGRA, 변경 여부(픽셀별)). 반환: 바뀐 텍셀 수
        /// </summary>
        public int BakeViews(IList<Tuple<ViewKind, byte[], bool[]>> views, byte[] result, float power = 8f)
        {
            int n = texIdx.Length; var acc = new float[n * 4]; var wsum = new float[n];
            float span = 1f / ViewScale();
            foreach (var v in views)
            {
                float[] z, uv, sh; RenderBuffers(v.Item1, out z, out uv, out sh); uv = null; sh = null;
                float vx, vy, vz; ViewDir(v.Item1, out vx, out vy, out vz);
                var edit = v.Item2; var changed = v.Item3;
                Parallel.For(0, n, k =>
                {
                    float sx, sy, d; ViewProject(v.Item1, texPos[k * 3], texPos[k * 3 + 1], texPos[k * 3 + 2], out sx, out sy, out d);
                    int ix = (int)sx, iy = (int)sy; if (ix < 0 || iy < 0 || ix >= TW || iy >= TH) return;
                    int pi = iy * TW + ix; if (!changed[pi]) return;
                    float nx = texNrm[k * 3], ny = texNrm[k * 3 + 1], nz = texNrm[k * 3 + 2], nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz) + 1e-9f;
                    float ndv = (nx * vx + ny * vy + nz * vz) / nl; if (ndv < -0.05f) return;
                    float dtol = span * Math.Min(2 + 1.5f / Math.Max(Math.Abs(ndv), 0.02f), 40);   // 스치는 각도일수록 깊이 허용치를 크게
                    if (d < z[pi] - dtol) return;
                    float w = (float)Math.Pow(Math.Max(ndv, 0), power) + 1e-4f;
                    int ei = pi * 4;
                    wsum[k] += w; for (int c = 0; c < 4; c++) acc[k * 4 + c] += w * edit[ei + c];   // k마다 한 스레드 → 경쟁 없음
                });
            }
            int N = TW * TH; var touched = new bool[N]; int cnt = 0;
            for (int k = 0; k < n; k++)
            {
                if (wsum[k] <= 1e-6f) continue;
                int oi = texIdx[k] * 4; for (int c = 0; c < 4; c++) result[oi + c] = (byte)Math.Min(255, acc[k * 4 + c] / wsum[k] + 0.5f);
                touched[texIdx[k]] = true; cnt++;
            }
            for (int i = 0; i < N; i++)
            {
                int s0 = padSrc[i]; if (s0 < 0 || s0 == i || !touched[s0]) continue;
                for (int c = 0; c < 4; c++) result[i * 4 + c] = result[s0 * 4 + c];
            }
            return cnt;
        }

        // ------------------------------------------------------------ 투영 굽기
        public void Bake(ViewKind v, byte[] rendered, BlendKind blend, byte[] result, float[] zbuf)
        {
            float vx, vy, vz; ViewDir(v, out vx, out vy, out vz);
            float tol = 3f / ViewScale();
            int N = TW * TH; var touched = new bool[N];
            Parallel.For(0, texIdx.Length, k =>
            {
                int i = texIdx[k];
                float sx, sy, d; ViewProject(v, texPos[k * 3], texPos[k * 3 + 1], texPos[k * 3 + 2], out sx, out sy, out d);
                int ix = (int)sx, iy = (int)sy; if (ix < 0 || iy < 0 || ix >= TW || iy >= TH) return;
                if (d < zbuf[iy * TW + ix] - tol * 2) return;
                float nx = texNrm[k * 3], ny = texNrm[k * 3 + 1], nz = texNrm[k * 3 + 2], nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz) + 1e-9f;
                float wgt = Smooth(((nx * vx + ny * vy + nz * vz) / nl - 0.05f) / 0.25f);
                if (wgt <= 0) return;
                int ri = (iy * TW + ix) * 4, oi = i * 4;
                if (blend == BlendKind.Normal)
                {
                    float a = rendered[ri + 3] / 255f * wgt; if (a <= 0) return;
                    for (int c = 0; c < 3; c++) result[oi + c] = (byte)(result[oi + c] * (1 - a) + rendered[ri + c] * a + 0.5f);
                    result[oi + 3] = (byte)Math.Max(result[oi + 3], (int)(a * 255));
                }
                else
                {
                    bool any = false;
                    for (int c = 0; c < 3; c++)
                    {
                        float pc = 255 - (255 - rendered[ri + c]) * wgt; if (pc >= 254.5f) continue; any = true;
                        int bv = result[oi + c];
                        result[oi + c] = (byte)(blend == BlendKind.Darken ? Math.Min(bv, (int)(pc + 0.5f)) : bv * pc / 255f + 0.5f);
                    }
                    if (!any) return;
                    if (result[oi + 3] < 255)
                    {
                        int dk = 255 - (rendered[ri + 2] * 299 + rendered[ri + 1] * 587 + rendered[ri] * 114) / 1000;
                        result[oi + 3] = (byte)Math.Max(result[oi + 3], (int)(dk * wgt));
                    }
                }
                touched[i] = true;
            });
            for (int i = 0; i < N; i++)
            {
                int s0 = padSrc[i]; if (s0 < 0 || s0 == i || !touched[s0]) continue;
                for (int c = 0; c < 4; c++) result[i * 4 + c] = result[s0 * 4 + c];
            }
        }
        static float Smooth(float t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }
    }
}
