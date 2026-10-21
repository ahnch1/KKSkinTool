using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace KKSkinTool
{
    public class MainForm : Form
    {
        CanvasView canvas;
        Doc doc;
        Bitmap wireSrc;
        string wirePath;
        readonly List<PartState> partHistory = new List<PartState>();

        NumericUpDown numX, numY, numSX, numSY, numRot, numOpa;
        CheckBox chkLock, chkFlip, chkWire;
        ComboBox cmbBlend, cmbBg;
        TrackBar trkWire;
        Button btnCommit, btnCancel;
        Label lblPart;
        ToolStripStatusLabel stPos, stZoom, stInfo;
        bool syncing;
        readonly string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kkskintool_settings.txt");
        string lastPartDir = "", lastSaveDir = "";

        public MainForm()
        {
            Text = "KKSkinTool"; Width = 1400; Height = 950; StartPosition = FormStartPosition.CenterScreen; KeyPreview = true;
            Font = new Font("Malgun Gothic", 9f);

            // ---------------- 도구 모음
            var ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4) };
            ts.Items.Add(Btn("새 문서", "Ctrl+N", (s, e) => NewDoc()));
            ts.Items.Add(new ToolStripLabel("배경"));
            cmbBg = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            cmbBg.Items.AddRange(new object[] { "흰색 (탠라인)", "투명 (타투)" }); cmbBg.SelectedIndex = 0;
            ts.Items.Add(new ToolStripControlHost(cmbBg));
            ts.Items.Add(Btn("결과 열기", "이전에 저장한 결과 PNG를 이어서 작업", (s, e) => OpenResult()));
            ts.Items.Add(new ToolStripSeparator());
            ts.Items.Add(Btn("베이스(wireline) 불러오기", "", (s, e) => PickWire()));
            ts.Items.Add(Btn("파트 불러오기", "Ctrl+O", (s, e) => LoadPart()));
            ts.Items.Add(new ToolStripSeparator());
            ts.Items.Add(Btn("실행 취소", "Ctrl+Z", (s, e) => DoUndo()));
            ts.Items.Add(Btn("다시 실행", "Ctrl+Y", (s, e) => DoRedo()));
            ts.Items.Add(new ToolStripSeparator());
            ts.Items.Add(Btn("화면 맞춤", "Ctrl+0", (s, e) => canvas.FitView()));
            ts.Items.Add(Btn("100%", "Ctrl+1", (s, e) => canvas.SetZoom(1f, new Point(canvas.Width / 2, canvas.Height / 2))));
            ts.Items.Add(new ToolStripSeparator());
            ts.Items.Add(Btn("PNG 저장", "Ctrl+S", (s, e) => SaveResult()));

            // ---------------- 오른쪽 속성 패널
            var side = new TableLayoutPanel { Dock = DockStyle.Right, Width = 270, ColumnCount = 2, Padding = new Padding(8), AutoScroll = true };
            side.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            lblPart = new Label { Text = "파트 없음", AutoSize = false, Height = 40, Dock = DockStyle.Fill, ForeColor = Color.DimGray };
            side.Controls.Add(lblPart); side.SetColumnSpan(lblPart, 2);
            numX = Num(side, "X (px)", -4096, 8192, 1, 0);
            numY = Num(side, "Y (px)", -4096, 8192, 1, 0);
            numSX = Num(side, "가로 크기 %", 1, 2000, 1, 1);
            numSY = Num(side, "세로 크기 %", 1, 2000, 1, 1);
            chkLock = new CheckBox { Text = "비율 고정", Checked = true, AutoSize = true };
            side.Controls.Add(new Label()); side.Controls.Add(chkLock);
            numRot = Num(side, "회전 (°)", -180, 180, 1, 1);
            numOpa = Num(side, "불투명도 %", 0, 100, 1, 0);
            side.Controls.Add(new Label { Text = "합성", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            cmbBlend = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            cmbBlend.Items.AddRange(new object[] { "어둡게 (탠라인)", "곱하기", "일반 (타투)" });
            side.Controls.Add(cmbBlend);
            chkFlip = new CheckBox { Text = "좌우 반전", AutoSize = true };
            side.Controls.Add(new Label()); side.Controls.Add(chkFlip);
            btnCommit = new Button { Text = "확정 (Enter)", Dock = DockStyle.Fill, Height = 34 }; btnCommit.Click += (s, e) => CommitPart();
            btnCancel = new Button { Text = "취소 (Esc)", Dock = DockStyle.Fill, Height = 34 }; btnCancel.Click += (s, e) => CancelPart();
            side.Controls.Add(btnCommit); side.SetColumnSpan(btnCommit, 2);
            side.Controls.Add(btnCancel); side.SetColumnSpan(btnCancel, 2);
            var sep = new Label { Text = "보기", AutoSize = true, Padding = new Padding(0, 16, 0, 0), Font = new Font(Font, FontStyle.Bold) };
            side.Controls.Add(sep); side.SetColumnSpan(sep, 2);
            chkWire = new CheckBox { Text = "와이어 표시 (W)", Checked = true, AutoSize = true };
            side.Controls.Add(chkWire); side.SetColumnSpan(chkWire, 2);
            side.Controls.Add(new Label { Text = "와이어 진하기", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            trkWire = new TrackBar { Minimum = 5, Maximum = 100, Value = 60, TickFrequency = 10, Dock = DockStyle.Fill };
            side.Controls.Add(trkWire);
            var help = new Label
            {
                AutoSize = false, Dock = DockStyle.Fill, Height = 300, ForeColor = Color.DimGray,
                Text = "조작\n" +
                       "· 파트 안 드래그: 이동\n· 모서리 사각형: 크기\n· 주황 원: 회전 (Shift: 15° 단위)\n" +
                       "· 방향키: 1px 이동 (Shift: 10px)\n· Alt+휠: 크기, Alt+Shift+휠: 회전\n\n" +
                       "· 휠: 확대/축소\n· 가운데·오른쪽 드래그, Space+드래그: 화면 이동\n\n" +
                       "· Enter 확정, Esc 취소\n· Ctrl+Z 실행 취소, Ctrl+Y 다시 실행\n· Ctrl+O 파트, Ctrl+S 저장, Ctrl+0 맞춤"
            };
            side.Controls.Add(help); side.SetColumnSpan(help, 2);

            // ---------------- 캔버스 / 상태 표시줄
            canvas = new CanvasView { Dock = DockStyle.Fill };
            canvas.PartChanged += SyncFromPart;
            canvas.BeginEdit += RecordPartState;
            canvas.MouseDoc += p => stPos.Text = string.Format("x {0:0}, y {1:0}", p.X, p.Y);
            canvas.ViewChanged += () => stZoom.Text = string.Format("{0:0}%", canvas.Zoom * 100);
            var st = new StatusStrip();
            stPos = new ToolStripStatusLabel("x -, y -") { Width = 140, AutoSize = false };
            stZoom = new ToolStripStatusLabel("-") { Width = 70, AutoSize = false };
            stInfo = new ToolStripStatusLabel("") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            st.Items.AddRange(new ToolStripItem[] { stPos, stZoom, stInfo });

            Controls.Add(canvas); Controls.Add(side); Controls.Add(ts); Controls.Add(st);

            foreach (var n in new[] { numX, numY, numSX, numSY, numRot, numOpa }) n.ValueChanged += OnPropChanged;
            cmbBlend.SelectedIndexChanged += OnPropChanged; chkFlip.CheckedChanged += OnPropChanged;
            chkWire.CheckedChanged += (s, e) => { canvas.ShowWire = chkWire.Checked; canvas.Invalidate(); };
            trkWire.ValueChanged += (s, e) => { canvas.WireOpacity = trkWire.Value / 100f; canvas.Invalidate(); };

            UpdatePartUi();
            Shown += (s, e) => Startup();
            FormClosing += OnClosingForm;
        }

        static ToolStripButton Btn(string text, string tip, EventHandler click)
        {
            var b = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text }; b.Click += click; return b;
        }
        static NumericUpDown Num(TableLayoutPanel p, string label, decimal min, decimal max, decimal inc, int dec)
        {
            p.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            var n = new NumericUpDown { Minimum = min, Maximum = max, Increment = inc, DecimalPlaces = dec, Dock = DockStyle.Fill };
            p.Controls.Add(n); return n;
        }

        // ------------------------------------------------------------ 시작 / 설정
        void Startup()
        {
            LoadSettings();
            if (!string.IsNullOrEmpty(wirePath) && File.Exists(wirePath)) LoadWire(wirePath);
            else PickWire();
            if (doc == null) NewDoc(true);
        }
        void LoadSettings()
        {
            if (!File.Exists(settingsPath)) return;
            var l = File.ReadAllLines(settingsPath);
            if (l.Length > 0) wirePath = l[0];
            if (l.Length > 1) lastPartDir = l[1];
            if (l.Length > 2) lastSaveDir = l[2];
        }
        void SaveSettings()
        {
            try { File.WriteAllLines(settingsPath, new[] { wirePath ?? "", lastPartDir ?? "", lastSaveDir ?? "" }); } catch { }
        }

        // ------------------------------------------------------------ 베이스 / 문서
        void PickWire()
        {
            using (var d = new OpenFileDialog { Filter = "PNG|*.png", Title = "베이스 와이어프레임 (wireline.png)" })
                if (d.ShowDialog(this) == DialogResult.OK) { LoadWire(d.FileName); SaveSettings(); }
        }
        void LoadWire(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var b = new Bitmap(fs))
                {
                    var b32 = new Bitmap(b.Width, b.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(b32)) g.DrawImage(b, 0, 0, b.Width, b.Height);
                    if (wireSrc != null) wireSrc.Dispose(); wireSrc = b32;
                }
                if (canvas.Wire != null) canvas.Wire.Dispose();
                canvas.Wire = Pixels.WireOverlay(wireSrc, Color.FromArgb(20, 60, 140), 255);
                wirePath = path; stInfo.Text = "베이스: " + Path.GetFileName(path);
                if (doc != null && (doc.W != wireSrc.Width || doc.H != wireSrc.Height))
                    stInfo.Text += string.Format("  (문서 {0}×{1}과 크기가 다름 — 새 문서를 만들면 맞춰짐)", doc.W, doc.H);
                canvas.Invalidate();
            }
            catch (Exception ex) { MessageBox.Show(this, "베이스를 열 수 없습니다.\n" + ex.Message); }
        }
        int DocW { get { return wireSrc != null ? wireSrc.Width : 2048; } }
        int DocH { get { return wireSrc != null ? wireSrc.Height : 2048; } }

        bool ConfirmDiscard()
        {
            if (doc == null || !doc.Dirty) return true;
            return MessageBox.Show(this, "저장하지 않은 결과가 있습니다. 버릴까요?", "KKSkinTool", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }
        void NewDoc(bool silent = false)
        {
            if (!silent && !ConfirmDiscard()) return;
            CancelPart();
            if (doc != null) doc.Dispose();
            doc = new Doc(DocW, DocH, cmbBg.SelectedIndex == 1);
            canvas.Doc = doc; canvas.FitView();
        }
        void OpenResult()
        {
            if (!ConfirmDiscard()) return;
            using (var d = new OpenFileDialog { Filter = "PNG|*.png", Title = "결과 PNG 열기", InitialDirectory = lastSaveDir })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                CancelPart();
                if (doc != null) doc.Dispose();
                doc = Doc.Open(d.FileName, DocW, DocH);
                cmbBg.SelectedIndex = doc.TransparentBg ? 1 : 0;
                canvas.Doc = doc; canvas.FitView();
            }
        }
        void SaveResult()
        {
            if (doc == null) return;
            if (canvas.Part != null &&
                MessageBox.Show(this, "배치 중인 파트가 아직 확정되지 않았습니다. 확정하고 저장할까요?", "KKSkinTool", MessageBoxButtons.YesNo) == DialogResult.Yes)
                CommitPart();
            using (var d = new SaveFileDialog { Filter = "PNG|*.png", FileName = "skin_result.png", InitialDirectory = lastSaveDir })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                doc.Save(d.FileName); lastSaveDir = Path.GetDirectoryName(d.FileName); SaveSettings();
                stInfo.Text = "저장: " + d.FileName;
            }
        }

        // ------------------------------------------------------------ 파트
        void LoadPart()
        {
            if (doc == null) return;
            if (canvas.Part != null)
            {
                var r = MessageBox.Show(this, "배치 중인 파트를 확정할까요?\n(아니요 = 버리고 새 파트)", "KKSkinTool", MessageBoxButtons.YesNoCancel);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.Yes) CommitPart(); else CancelPart();
            }
            using (var d = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp", Title = "파트 이미지 (탠라인 파츠 / 타투)", InitialDirectory = lastPartDir })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                lastPartDir = Path.GetDirectoryName(d.FileName); SaveSettings();
                PartLayer p;
                try { p = new PartLayer(d.FileName); }
                catch (Exception ex) { MessageBox.Show(this, "이미지를 열 수 없습니다.\n" + ex.Message); return; }
                if (p.W == doc.W && p.H == doc.H) { p.S.Cx = doc.W / 2f; p.S.Cy = doc.H / 2f; }      // UV 전체 크기 파츠: 제자리
                else { var c = canvas.ViewCenterDoc(); p.S.Cx = c.X; p.S.Cy = c.Y; }                   // 작은 이미지: 화면 가운데
                canvas.Part = p; partHistory.Clear();
                UpdatePartUi(); SyncFromPart(); canvas.Invalidate(); canvas.Focus();
            }
        }
        void CommitPart()
        {
            if (canvas.Part == null || doc == null) return;
            Cursor = Cursors.WaitCursor;
            try { doc.Commit(canvas.Part); }
            finally { Cursor = Cursors.Default; }
            stInfo.Text = "확정: " + canvas.Part.Name;
            canvas.Part.Dispose(); canvas.Part = null; partHistory.Clear();
            UpdatePartUi(); canvas.Invalidate();
        }
        void CancelPart()
        {
            if (canvas.Part == null) return;
            canvas.Part.Dispose(); canvas.Part = null; partHistory.Clear();
            UpdatePartUi(); canvas.Invalidate();
        }

        // ------------------------------------------------------------ 실행 취소
        void RecordPartState()
        {
            if (canvas.Part == null) return;
            var s = canvas.Part.S.Clone();
            if (partHistory.Count > 0 && partHistory[partHistory.Count - 1].SameAs(s)) return;
            partHistory.Add(s);
            if (partHistory.Count > 300) partHistory.RemoveAt(0);
        }
        void DoUndo()
        {
            if (canvas.Part != null)
            {
                if (partHistory.Count > 0)
                {
                    canvas.Part.S = partHistory[partHistory.Count - 1]; partHistory.RemoveAt(partHistory.Count - 1);
                    canvas.Part.RefreshDisplay(); SyncFromPart(); canvas.Invalidate();
                }
                return;     // 파트 배치 중에는 파트 변환만 되돌림
            }
            if (doc != null && doc.CanUndo) { doc.Undo(); canvas.Invalidate(); stInfo.Text = "실행 취소"; }
        }
        void DoRedo()
        {
            if (canvas.Part == null && doc != null && doc.CanRedo) { doc.Redo(); canvas.Invalidate(); stInfo.Text = "다시 실행"; }
        }

        // ------------------------------------------------------------ 속성 동기화
        void UpdatePartUi()
        {
            bool on = canvas.Part != null;
            foreach (Control c in new Control[] { numX, numY, numSX, numSY, numRot, numOpa, chkLock, cmbBlend, chkFlip, btnCommit, btnCancel }) c.Enabled = on;
            lblPart.Text = on ? string.Format("{0}\n{1}×{2}", canvas.Part.Name, canvas.Part.W, canvas.Part.H) : "파트 없음 (Ctrl+O로 불러오기)";
        }
        static decimal Clamp(NumericUpDown n, double v) { return Math.Max(n.Minimum, Math.Min(n.Maximum, (decimal)v)); }
        void SyncFromPart()
        {
            var p = canvas.Part; if (p == null) return;
            syncing = true;
            numX.Value = Clamp(numX, p.S.Cx); numY.Value = Clamp(numY, p.S.Cy);
            numSX.Value = Clamp(numSX, p.S.ScaleX * 100); numSY.Value = Clamp(numSY, p.S.ScaleY * 100);
            numRot.Value = Clamp(numRot, p.S.Rotation); numOpa.Value = Clamp(numOpa, p.S.Opacity * 100);
            cmbBlend.SelectedIndex = (int)p.S.Blend; chkFlip.Checked = p.S.FlipH;
            syncing = false;
        }
        void OnPropChanged(object sender, EventArgs e)
        {
            var p = canvas.Part; if (syncing || p == null) return;
            RecordPartState();
            p.S.Cx = (float)numX.Value; p.S.Cy = (float)numY.Value;
            float sx = (float)numSX.Value / 100f, sy = (float)numSY.Value / 100f;
            if (chkLock.Checked)
            {
                float ratio = p.S.ScaleX > 0 ? p.S.ScaleY / p.S.ScaleX : 1;
                if (sender == numSX) sy = sx * ratio; else if (sender == numSY) sx = sy / ratio;
            }
            p.S.ScaleX = sx; p.S.ScaleY = sy;
            p.S.Rotation = (float)numRot.Value; p.S.Opacity = (float)numOpa.Value / 100f;
            p.S.Blend = (BlendKind)Math.Max(0, cmbBlend.SelectedIndex); p.S.FlipH = chkFlip.Checked;
            p.RefreshDisplay(); SyncFromPart(); canvas.Invalidate();
        }

        // ------------------------------------------------------------ 키보드
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            bool canvasFocus = ActiveControl == canvas || ActiveControl == null;
            switch (keyData)
            {
                case Keys.Control | Keys.Z: DoUndo(); return true;
                case Keys.Control | Keys.Y:
                case Keys.Control | Keys.Shift | Keys.Z: DoRedo(); return true;
                case Keys.Control | Keys.O: LoadPart(); return true;
                case Keys.Control | Keys.S: SaveResult(); return true;
                case Keys.Control | Keys.N: NewDoc(); return true;
                case Keys.Control | Keys.D0: canvas.FitView(); return true;
                case Keys.Control | Keys.D1: canvas.SetZoom(1f, new Point(canvas.Width / 2, canvas.Height / 2)); return true;
                case Keys.Control | Keys.Oemplus: canvas.SetZoom(canvas.Zoom * 1.25f, new Point(canvas.Width / 2, canvas.Height / 2)); return true;
                case Keys.Control | Keys.OemMinus: canvas.SetZoom(canvas.Zoom / 1.25f, new Point(canvas.Width / 2, canvas.Height / 2)); return true;
                case Keys.Enter: if (canvas.Part != null) { CommitPart(); return true; } break;
                case Keys.Escape: if (canvas.Part != null) { CancelPart(); return true; } break;
            }
            if (canvasFocus && canvas.Part != null)
            {
                float step = (keyData & Keys.Shift) != 0 ? 10 : 1;
                switch (keyData & Keys.KeyCode)
                {
                    case Keys.Left: canvas.Nudge(-step, 0); return true;
                    case Keys.Right: canvas.Nudge(step, 0); return true;
                    case Keys.Up: canvas.Nudge(0, -step); return true;
                    case Keys.Down: canvas.Nudge(0, step); return true;
                }
            }
            if (canvasFocus && keyData == Keys.W) { chkWire.Checked = !chkWire.Checked; return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && (ActiveControl == canvas)) { canvas.SpaceDown = true; e.Handled = true; e.SuppressKeyPress = true; }
            base.OnKeyDown(e);
        }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) canvas.SpaceDown = false;
            base.OnKeyUp(e);
        }

        void OnClosingForm(object sender, FormClosingEventArgs e)
        {
            if (canvas.Part != null || (doc != null && doc.Dirty))
                if (MessageBox.Show(this, "저장하지 않은 작업이 있습니다. 종료할까요?", "KKSkinTool", MessageBoxButtons.YesNo) != DialogResult.Yes) { e.Cancel = true; return; }
            SaveSettings();
        }
    }
}
