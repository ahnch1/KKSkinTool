using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace KKSkinTool
{
    /// <summary>
    /// 뷰 이미지 내보내기/불러오기.
    /// 내보내기: front/back/left/right.png(편집용, 텍스처 색 그대로) + *_guide.png(음영·높이선, 참고용) + .orig/(비교용 원본) + project.txt
    /// 불러오기: 원본과 달라진 픽셀만 모아 여러 뷰를 법선 가중으로 한 번에 굽기
    /// </summary>
    public static class ViewExchange
    {
        public static readonly ViewKind[] Views = { ViewKind.Front, ViewKind.Back, ViewKind.Left, ViewKind.Right };
        public static string FileName(ViewKind v) { return v.ToString().ToLowerInvariant(); }

        public static void Export(MeshModel mesh, Bitmap texture, string dir, Action<string> log)
        {
            Directory.CreateDirectory(dir); Directory.CreateDirectory(Path.Combine(dir, ".orig"));
            int step = Math.Max(16, mesh.TH / 32);
            foreach (var v in Views)
            {
                float[] z, uv, sh; mesh.RenderBuffers(v, out z, out uv, out sh);
                using (var a = mesh.RenderAlbedo(texture, z, uv))
                {
                    a.Save(Path.Combine(dir, FileName(v) + ".png"), ImageFormat.Png);
                    a.Save(Path.Combine(dir, ".orig", FileName(v) + ".png"), ImageFormat.Png);
                }
                using (var g0 = mesh.RenderView(v, texture, z, uv, sh))
                {
                    using (var g = Graphics.FromImage(g0))
                    using (var pen = new Pen(Color.FromArgb(110, 255, 60, 60), Math.Max(1, mesh.TH / 2048f)))
                    using (var font = new Font("Malgun Gothic", Math.Max(9, mesh.TH / 120f), GraphicsUnit.Pixel))
                        for (int y = 0; y < mesh.TH; y += step)
                        {
                            g.DrawLine(pen, 0, y, mesh.TW, y);
                            if ((y / step) % 4 == 0) g.DrawString(y.ToString(), font, Brushes.OrangeRed, 4, y + 1);
                        }
                    g0.Save(Path.Combine(dir, FileName(v) + "_guide.png"), ImageFormat.Png);
                }
                if (log != null) log(FileName(v));
            }
            var sb = new StringBuilder();
            sb.AppendLine("# KKSkinTool 뷰 내보내기 — 이 폴더의 front/back/left/right.png를 칠한 뒤 [뷰 불러오기]");
            sb.AppendLine("mesh=" + mesh.Path);
            sb.AppendLine("groups=" + string.Join("\t", mesh.SelectedGroups));
            sb.AppendLine("axis=" + mesh.FrontAxis);
            sb.AppendLine("size=" + mesh.TW + "x" + mesh.TH);
            sb.AppendLine("time=" + DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
            File.WriteAllText(Path.Combine(dir, "project.txt"), sb.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "README.txt"),
                "front/back/left/right.png : 이 이미지 위에 칠하세요 (크기·위치를 바꾸지 말 것, 투명 바탕 유지 권장)\r\n" +
                "*_guide.png : 음영과 높이 기준선이 있는 참고용 (레이어로 아래에 깔고 작업)\r\n" +
                "  모든 뷰는 같은 축척·같은 높이 기준 → 같은 y에 그린 띠는 앞·옆·뒤가 이어짐\r\n" +
                ".orig : 바뀐 픽셀을 찾기 위한 원본 (지우거나 고치지 말 것)\r\n" +
                "칠하지 않은 뷰는 그대로 두면 됩니다. 손대지 않은 픽셀은 원래 텍스처가 유지됩니다.\r\n", new UTF8Encoding(false));
        }

        /// <summary>project.txt 읽기 (없으면 null)</summary>
        public static Dictionary<string, string> ReadProject(string dir)
        {
            var p = Path.Combine(dir, "project.txt"); if (!File.Exists(p)) return null;
            var d = new Dictionary<string, string>();
            foreach (var l in File.ReadAllLines(p)) { int i = l.IndexOf('='); if (i > 0 && !l.StartsWith("#")) d[l.Substring(0, i)] = l.Substring(i + 1); }
            return d;
        }

        /// <summary>편집 이미지를 읽어 (보기, BGRA, 변경 마스크) 목록으로. tol: 채널 차이 허용치</summary>
        public static List<Tuple<ViewKind, byte[], bool[]>> LoadEdits(string dir, int w, int h, int tol, Action<string> log)
        {
            var list = new List<Tuple<ViewKind, byte[], bool[]>>();
            foreach (var v in Views)
            {
                string fe = Path.Combine(dir, FileName(v) + ".png"), fo = Path.Combine(dir, ".orig", FileName(v) + ".png");
                if (!File.Exists(fe) || !File.Exists(fo)) continue;
                var e = ReadSized(fe, w, h); var o = ReadSized(fo, w, h);
                var ch = new bool[w * h]; int n = 0;
                for (int i = 0; i < ch.Length; i++)
                {
                    int k = i * 4;
                    if (o[k + 3] == 0) continue;                                     // 메쉬 밖
                    int d = Math.Max(Math.Max(Math.Abs(e[k] - o[k]), Math.Abs(e[k + 1] - o[k + 1])), Math.Max(Math.Abs(e[k + 2] - o[k + 2]), Math.Abs(e[k + 3] - o[k + 3])));
                    if (d > tol) { ch[i] = true; n++; }
                }
                if (log != null) log(string.Format("{0}: 바뀐 픽셀 {1:N0}", FileName(v), n));
                if (n > 0) list.Add(Tuple.Create(v, e, ch));
            }
            return list;
        }

        static byte[] ReadSized(string path, int w, int h)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var src = new Bitmap(fs))
            {
                if (src.Width != w || src.Height != h)
                    throw new Exception(string.Format("{0} 크기({1}×{2})가 문서({3}×{4})와 다릅니다. 내보낸 크기 그대로 저장하세요.", Path.GetFileName(path), src.Width, src.Height, w, h));
                using (var b = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(b)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImage(src, new Rectangle(0, 0, w, h)); }
                    return Pixels.Read(b);
                }
            }
        }
    }
}
