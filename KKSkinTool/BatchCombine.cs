using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KKSkinTool
{
    /// <summary>TOP 폴더 × BOTTOM 폴더의 모든 조합을 합성해 TOP_BOTTOM.png로 저장</summary>
    public static class BatchCombine
    {
        public class Options
        {
            public string TopDir, BottomDir, OutDir;
            public BlendKind Blend = BlendKind.Darken;
            public bool StripPrefix = true, Overwrite = true;
            public int W = 2048, H = 2048;
        }

        static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".bmp" };
        public static List<string> ListImages(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return new List<string>();
            return Directory.GetFiles(dir)
                .Where(f => Exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Where(f => !Path.GetFileName(f).StartsWith("_"))          // _overview 같은 미리보기 제외
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>파일명에서 확장자와 top_/bottom_/tan_ 등 접두어 제거</summary>
        public static string Stem(string path, bool strip)
        {
            string s = Path.GetFileNameWithoutExtension(path);
            if (!strip) return s;
            foreach (var p in new[] { "top_", "bottom_", "tan_", "onepiece_", "strap_" })
                if (s.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { s = s.Substring(p.Length); break; }
            return s;
        }

        /// <summary>이미지를 W×H로 맞추고 흰 바탕 위에 평탄화한 BGRA 바이트</summary>
        public static byte[] LoadFlat(string path, int w, int h)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var src = new Bitmap(fs))
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, 0, 0, w, h);
                }
                return Pixels.Read(bmp);
            }
        }

        public static void Blend(byte[] dst, byte[] src, BlendKind kind)
        {
            Parallel.For(0, dst.Length / 4, k =>
            {
                int i = k * 4;
                for (int c = 0; c < 3; c++)
                {
                    int a = dst[i + c], b = src[i + c];
                    dst[i + c] = (byte)(kind == BlendKind.Multiply ? a * b / 255 : Math.Min(a, b));
                }
                dst[i + 3] = 255;
            });
        }

        /// <summary>일괄 조합 실행. progress(완료 수, 전체 수, 현재 파일명)</summary>
        public static int Run(Options o, Action<int, int, string> progress, CancellationToken ct)
        {
            var tops = ListImages(o.TopDir); var bottoms = ListImages(o.BottomDir);
            if (tops.Count == 0) throw new Exception("TOP 폴더에 이미지가 없습니다.");
            if (bottoms.Count == 0) throw new Exception("BOTTOM 폴더에 이미지가 없습니다.");
            Directory.CreateDirectory(o.OutDir);
            int total = tops.Count * bottoms.Count, done = 0, saved = 0;
            var cache = new Dictionary<string, byte[]>();                    // 하의는 한 번만 읽음
            bool canCache = bottoms.Count <= 40;
            using (var outBmp = new Bitmap(o.W, o.H, PixelFormat.Format32bppArgb))
            {
                foreach (var t in tops)
                {
                    var top = LoadFlat(t, o.W, o.H);
                    foreach (var b in bottoms)
                    {
                        ct.ThrowIfCancellationRequested();
                        string name = Stem(t, o.StripPrefix) + "_" + Stem(b, o.StripPrefix) + ".png";
                        string path = Path.Combine(o.OutDir, name);
                        if (progress != null) progress(done, total, name);
                        if (o.Overwrite || !File.Exists(path))
                        {
                            byte[] bot;
                            if (!cache.TryGetValue(b, out bot)) { bot = LoadFlat(b, o.W, o.H); if (canCache) cache[b] = bot; }
                            var buf = (byte[])top.Clone();
                            Blend(buf, bot, o.Blend);
                            Pixels.Write(outBmp, buf);
                            outBmp.Save(path, ImageFormat.Png); saved++;
                        }
                        done++;
                    }
                }
            }
            if (progress != null) progress(done, total, "");
            return saved;
        }
    }

    /// <summary>TOP × BOTTOM 일괄 조합 창</summary>
    public class BatchCombineForm : Form
    {
        TextBox txtTop, txtBottom, txtOut, txtLog;
        ComboBox cmbBlend;
        CheckBox chkStrip, chkOverwrite;
        Label lblCount;
        ProgressBar bar;
        Button btnRun, btnStop, btnOpen;
        CancellationTokenSource cts;
        readonly int docW, docH;
        public string TopDir { get { return txtTop.Text; } }
        public string BottomDir { get { return txtBottom.Text; } }
        public string OutDir { get { return txtOut.Text; } }

        public BatchCombineForm(string top, string bottom, string outDir, int w, int h)
        {
            docW = w; docH = h;
            Text = "TOP × BOTTOM 일괄 조합"; Width = 760; Height = 560; StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Malgun Gothic", 9f); MinimizeBox = false;

            var t = new TableLayoutPanel { Dock = DockStyle.Top, Height = 190, ColumnCount = 3, Padding = new Padding(10) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            txtTop = Row(t, "TOP 폴더", top); txtBottom = Row(t, "BOTTOM 폴더", bottom); txtOut = Row(t, "저장 폴더", outDir);
            t.Controls.Add(new Label { Text = "합성", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            var opt = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            cmbBlend = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
            cmbBlend.Items.AddRange(new object[] { "어둡게 (탠라인)", "곱하기" }); cmbBlend.SelectedIndex = 0;
            chkStrip = new CheckBox { Text = "파일명 접두어 제거 (top_, bottom_ …)", Checked = true, AutoSize = true, Padding = new Padding(10, 3, 0, 0) };
            chkOverwrite = new CheckBox { Text = "같은 이름 덮어쓰기", Checked = true, AutoSize = true, Padding = new Padding(10, 3, 0, 0) };
            opt.Controls.Add(cmbBlend); opt.Controls.Add(chkStrip); opt.Controls.Add(chkOverwrite);
            t.Controls.Add(opt); t.SetColumnSpan(opt, 2);
            lblCount = new Label { AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(0, 4, 0, 0) };
            t.Controls.Add(new Label()); t.Controls.Add(lblCount); t.SetColumnSpan(lblCount, 2);

            var bottomBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(10, 4, 10, 4) };
            btnRun = new Button { Text = "조합 시작", Width = 100, Height = 30 };
            btnStop = new Button { Text = "중지", Width = 70, Height = 30, Enabled = false };
            btnOpen = new Button { Text = "저장 폴더 열기", Width = 110, Height = 30 };
            bar = new ProgressBar { Width = 380, Height = 22, Margin = new Padding(10, 6, 0, 0) };
            bottomBar.Controls.AddRange(new Control[] { btnRun, btnStop, btnOpen, bar });
            txtLog = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };

            Controls.Add(txtLog); Controls.Add(bottomBar); Controls.Add(t);
            btnRun.Click += async (s, e) => await RunAsync();
            btnStop.Click += (s, e) => { if (cts != null) cts.Cancel(); };
            btnOpen.Click += (s, e) => { if (Directory.Exists(txtOut.Text)) System.Diagnostics.Process.Start("explorer.exe", txtOut.Text); };
            txtTop.TextChanged += (s, e) => UpdateCount(); txtBottom.TextChanged += (s, e) => UpdateCount();
            chkStrip.CheckedChanged += (s, e) => UpdateCount();
            FormClosing += (s, e) => { if (cts != null) { cts.Cancel(); } };
            UpdateCount();
        }

        TextBox Row(TableLayoutPanel p, string label, string value)
        {
            p.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            var tb = new TextBox { Dock = DockStyle.Fill, Text = value ?? "" };
            var bt = new Button { Text = "찾기", Dock = DockStyle.Fill };
            bt.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog { SelectedPath = tb.Text }) if (d.ShowDialog(this) == DialogResult.OK) tb.Text = d.SelectedPath;
            };
            p.Controls.Add(tb); p.Controls.Add(bt);
            return tb;
        }

        void UpdateCount()
        {
            var tops = BatchCombine.ListImages(txtTop.Text); var bots = BatchCombine.ListImages(txtBottom.Text);
            lblCount.Text = string.Format("TOP {0}개 × BOTTOM {1}개 = {2}개 조합", tops.Count, bots.Count, tops.Count * bots.Count);
            if (tops.Count > 0 && bots.Count > 0)
                lblCount.Text += "   예: " + BatchCombine.Stem(tops[0], chkStrip.Checked) + "_" + BatchCombine.Stem(bots[0], chkStrip.Checked) + ".png";
        }

        void Log(string s) { txtLog.AppendText(s + Environment.NewLine); }

        async Task RunAsync()
        {
            if (string.IsNullOrWhiteSpace(txtOut.Text)) { MessageBox.Show(this, "저장 폴더를 지정하세요."); return; }
            var o = new BatchCombine.Options
            {
                TopDir = txtTop.Text, BottomDir = txtBottom.Text, OutDir = txtOut.Text,
                Blend = cmbBlend.SelectedIndex == 1 ? BlendKind.Multiply : BlendKind.Darken,
                StripPrefix = chkStrip.Checked, Overwrite = chkOverwrite.Checked, W = docW, H = docH
            };
            cts = new CancellationTokenSource(); var ct = cts.Token;
            btnRun.Enabled = false; btnStop.Enabled = true; bar.Value = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Log("시작: " + lblCount.Text);
            try
            {
                int saved = await Task.Run(() => BatchCombine.Run(o, (done, total, name) =>
                {
                    BeginInvoke(new Action(() =>
                    {
                        bar.Maximum = Math.Max(total, 1); bar.Value = Math.Min(done, bar.Maximum);
                        if (name.Length > 0) Text = string.Format("TOP × BOTTOM 일괄 조합 — {0}/{1} {2}", done + 1, total, name);
                    }));
                }, ct), ct);
                Log(string.Format("완료: {0}개 저장 ({1:F1}초) → {2}", saved, sw.Elapsed.TotalSeconds, o.OutDir));
            }
            catch (OperationCanceledException) { Log("중지됨"); }
            catch (Exception ex) { Log("오류: " + ex.Message); MessageBox.Show(this, ex.Message, "오류"); }
            finally
            {
                btnRun.Enabled = true; btnStop.Enabled = false; Text = "TOP × BOTTOM 일괄 조합";
                cts.Dispose(); cts = null;
            }
        }
    }
}
