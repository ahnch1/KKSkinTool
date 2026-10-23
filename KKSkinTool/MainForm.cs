using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
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
        // 영역 선택 / 색상 / 파트 유동화
        ToolStripButton btnSelRect, btnSelLasso;
        Button btnTint, btnTintClear;
        PartLayer liquifyPart;
        List<PartState> parentHistory;
        // 3D 보기
        BodyMesh body; string bodyPath = "";
        ViewKind view = ViewKind.UV;
        ComboBox cmbView;
        readonly System.Collections.Generic.Dictionary<ViewKind, float[][]> viewBuf = new System.Collections.Generic.Dictionary<ViewKind, float[][]>();
        // 유동화
        ToolStripButton btnLiq;
        Panel liqPanel;
        ComboBox cmbLiqTool;
        NumericUpDown numLiqSize, numLiqStr;
        readonly string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kkskintool_settings.txt");
        string lastPartDir = "", lastSaveDir = "", batchTop = "", batchBottom = "", batchOut = "";

        public MainForm()
        {
            Text = "KKSkinTool"; Width = 1400; Height = 950; StartPosition = FormStartPosition.CenterScreen; KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
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
            ts.Items.Add(Btn("바디 OBJ", "3D 보기용 바디 메쉬 (Material Editor로 내보낸 o_body_*.obj)", (s, e) => PickBody()));
            ts.Items.Add(new ToolStripLabel("보기"));
            cmbView = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
            cmbView.Items.AddRange(new object[] { "UV (F2)", "정면 (F3)", "후면 (F4)", "왼쪽 (F5)", "오른쪽 (F6)" }); cmbView.SelectedIndex = 0;
            cmbView.SelectedIndexChanged += (s, e) => { if (!syncing) SetView((ViewKind)cmbView.SelectedIndex); };
            ts.Items.Add(new ToolStripControlHost(cmbView));
            ts.Items.Add(new ToolStripSeparator());
            ts.Items.Add(Btn("파트 불러오기", "Ctrl+O", (s, e) => LoadPart()));
            btnSelRect = Btn("사각 선택", "M: 결과(파트가 없을 때) 또는 배치 중인 파트의 일부를 사각형으로 선택해 이동·크기·회전", (s, e) => ToggleSelect(CanvasView.SelectTool.Rect));
            btnSelLasso = Btn("올가미 선택", "Shift+M: 자유 곡선으로 선택", (s, e) => ToggleSelect(CanvasView.SelectTool.Lasso));
            ts.Items.Add(btnSelRect); ts.Items.Add(btnSelLasso);
            ts.Items.Add(new ToolStripSeparator());
            ts.Items.Add(Btn("실행 취소", "Ctrl+Z", (s, e) => DoUndo()));
            ts.Items.Add(Btn("다시 실행", "Ctrl+Y", (s, e) => DoRedo()));
            ts.Items.Add(new ToolStripSeparator());
            ts.Items.Add(Btn("화면 맞춤", "Ctrl+0", (s, e) => canvas.FitView()));
            ts.Items.Add(Btn("100%", "Ctrl+1", (s, e) => canvas.SetZoom(1f, new Point(canvas.Width / 2, canvas.Height / 2))));
            ts.Items.Add(new ToolStripSeparator());
            ts.Items.Add(Btn("PNG 저장", "Ctrl+S", (s, e) => SaveResult()));
            ts.Items.Add(Btn("탠라인 → 타투", "흰 바탕 + 검은 탠라인을 투명 바탕 + 흰색으로 변환 (가장 진한 곳 = 불투명). Shift+클릭: 진하기 그대로", (s, e) => TanToTattoo((ModifierKeys & Keys.Shift) == 0)));
            ts.Items.Add(new ToolStripSeparator());
            btnLiq = Btn("유동화", "L: 결과 이미지를 브러시로 밀기/부풀리기/오므리기/소용돌이/복원", (s, e) => ToggleLiquify());
            btnLiq.CheckOnClick = false; ts.Items.Add(btnLiq);
            ts.Items.Add(new ToolStripSeparator());
            ts.Items.Add(Btn("TOP × BOTTOM 일괄 조합", "Ctrl+B: TOP 폴더와 BOTTOM 폴더의 모든 조합을 TOP_BOTTOM.png로 저장", (s, e) => OpenBatch()));

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
            numOpa = Num(side, "알파(불투명도) %", 0, 100, 1, 0);
            side.Controls.Add(new Label { Text = "색상", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            var tintRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
            btnTint = new Button { Text = "변경…", Width = 90, Height = 26, BackColor = SystemColors.Control };
            btnTintClear = new Button { Text = "원래대로", Width = 70, Height = 26 };
            btnTint.Click += (s, e) => PickTint(); btnTintClear.Click += (s, e) => SetTint(0);
            tintRow.Controls.Add(btnTint); tintRow.Controls.Add(btnTintClear); side.Controls.Add(tintRow);
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
                AutoSize = false, Dock = DockStyle.Fill, Height = 400, ForeColor = Color.DimGray,
                Text = "조작\n" +
                       "· 파트 안 드래그: 이동\n· 모서리 사각형: 크기\n· 주황 원: 회전 (Shift: 15° 단위)\n" +
                       "· 방향키: 1px 이동 (Shift: 10px)\n· Alt+휠: 크기, Alt+Shift+휠: 회전\n\n" +
                       "· 휠: 확대/축소\n· 가운데·오른쪽 드래그, Space+드래그: 화면 이동\n\n" +
                       "· Enter 확정, Esc 취소\n· Ctrl+Z 실행 취소, Ctrl+Y 다시 실행\n· Ctrl+O 파트, Ctrl+S 저장, Ctrl+0 맞춤\n· Ctrl+B TOP × BOTTOM 일괄 조합\n· L 유동화 (파트가 있으면 파트에, 없으면 결과에)\n· M 사각 선택 / Shift+M 올가미: 파트 일부 또는\n  결과 일부를 떼어 이동·크기·회전 → Enter/Esc\n· 색상 변경: 오른쪽 [변경…]\n· [탠라인 → 타투] 투명 바탕 흰색 변환\n  (Shift+클릭: 진하기 그대로)\n· F2 UV / F3 정면 / F4 후면 / F5·F6 옆 보기\n  (3D 보기에서 확정하면 몸에 투영)"
            };
            side.Controls.Add(help); side.SetColumnSpan(help, 2);

            // ---------------- 유동화 패널 (유동화 모드에서만 보임)
            liqPanel = new Panel { Dock = DockStyle.Right, Width = 270, Visible = false, Padding = new Padding(8) };
            var lt = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            lt.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); lt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var lh = new Label { Text = "유동화", AutoSize = true, Font = new Font(Font.FontFamily, 11f, FontStyle.Bold), Padding = new Padding(0, 0, 0, 8) };
            lt.Controls.Add(lh); lt.SetColumnSpan(lh, 2);
            lt.Controls.Add(new Label { Text = "도구", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            cmbLiqTool = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            cmbLiqTool.Items.AddRange(new object[] { "밀기 (1)", "부풀리기 (2)", "오므리기 (3)", "소용돌이 시계 (4)", "소용돌이 반시계 (5)", "매끄럽게 (6)", "복원 (7)" });
            cmbLiqTool.SelectedIndex = 0; lt.Controls.Add(cmbLiqTool);
            numLiqSize = Num(lt, "브러시 크기", 3, 800, 5, 0); numLiqSize.Value = 80;
            numLiqStr = Num(lt, "강도 %", 1, 100, 5, 0); numLiqStr.Value = 50;
            var bApply = new Button { Text = "적용 (Enter)", Dock = DockStyle.Fill, Height = 34 }; bApply.Click += (s, e) => EndLiquify(true);
            var bReset = new Button { Text = "전체 복원", Dock = DockStyle.Fill, Height = 30 };
            bReset.Click += (s, e) => { if (canvas.Liquify != null) { canvas.Liquify.ResetAll(); canvas.Invalidate(); } };
            var bCancel = new Button { Text = "취소 (Esc)", Dock = DockStyle.Fill, Height = 30 }; bCancel.Click += (s, e) => EndLiquify(false);
            foreach (var b in new[] { bApply, bReset, bCancel }) { lt.Controls.Add(b); lt.SetColumnSpan(b, 2); }
            var lhelp = new Label
            {
                AutoSize = false, Dock = DockStyle.Fill, Height = 260, ForeColor = Color.DimGray,
                Text = "· 왼쪽 드래그: 브러시 적용\n  (부풀리기/오므리기/소용돌이/복원은\n   누르고 있으면 계속 적용)\n" +
                       "· [ / ] 또는 Alt+휠: 브러시 크기\n· 1~7: 도구 선택\n· Ctrl+Z: 획 단위 실행 취소\n" +
                       "· 휠: 확대/축소, Space/가운데 드래그: 이동\n\n" +
                       "원본에서 한 번만 다시 샘플링하므로\n여러 번 문질러도 흐려지지 않습니다.\n적용 후에는 문서 실행 취소(Ctrl+Z)로\n유동화 전체를 되돌릴 수 있습니다."
            };
            lt.Controls.Add(lhelp); lt.SetColumnSpan(lhelp, 2);
            liqPanel.Controls.Add(lt);
            cmbLiqTool.SelectedIndexChanged += (s, e) => ApplyLiqSettings();
            numLiqSize.ValueChanged += (s, e) => ApplyLiqSettings();
            numLiqStr.ValueChanged += (s, e) => ApplyLiqSettings();

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

            Controls.Add(canvas); Controls.Add(side); Controls.Add(liqPanel); Controls.Add(ts); Controls.Add(st);
            canvas.LiquifyStrokeEnd += () => { if (canvas.Liquify != null) { syncing = true; numLiqSize.Value = Math.Max(numLiqSize.Minimum, Math.Min(numLiqSize.Maximum, (decimal)(canvas.Liquify.Radius * canvas.Liquify.ViewScale))); syncing = false; } };
            canvas.SelectionMade += OnSelection;

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
            if (l.Length > 3) batchTop = l[3];
            if (l.Length > 4) batchBottom = l[4];
            if (l.Length > 5) batchOut = l[5];
            if (l.Length > 6) bodyPath = l[6];
        }
        void SaveSettings()
        {
            try { File.WriteAllLines(settingsPath, new[] { wirePath ?? "", lastPartDir ?? "", lastSaveDir ?? "", batchTop ?? "", batchBottom ?? "", batchOut ?? "", bodyPath ?? "" }); } catch { }
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
            if (Liquifying) EndLiquify(false);
            if (!silent && !ConfirmDiscard()) return;
            while (canvas.Part != null) CancelPart();
            if (doc != null) doc.Dispose();
            doc = new Doc(DocW, DocH, cmbBg.SelectedIndex == 1);
            canvas.Doc = doc; RefreshView(true);
        }
        void OpenResult()
        {
            if (Liquifying) EndLiquify(true);
            if (!ConfirmDiscard()) return;
            using (var d = new OpenFileDialog { Filter = "PNG|*.png", Title = "결과 PNG 열기", InitialDirectory = lastSaveDir })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                while (canvas.Part != null) CancelPart();
                if (doc != null) doc.Dispose();
                doc = Doc.Open(d.FileName, DocW, DocH);
                cmbBg.SelectedIndex = doc.TransparentBg ? 1 : 0;
                canvas.Doc = doc; RefreshView(true);
            }
        }
        void SaveResult()
        {
            if (doc == null) return;
            if (Liquifying) EndLiquify(true);
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

        // ------------------------------------------------------------ 탠라인 → 타투 오버레이
        /// <summary>결과 이미지를 투명 바탕 + 흰색으로 변환 (실행 취소 가능)</summary>
        void TanToTattoo(bool normalize)
        {
            if (doc == null) return;
            if (Liquifying) EndLiquify(true);
            if (canvas.Part != null)
            {
                var r = MessageBox.Show(this, "배치 중인 파트를 먼저 확정할까요?\n(아니요 = 버림)", "KKSkinTool", MessageBoxButtons.YesNoCancel);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.Yes) CommitPart(); else CancelPart();
            }
            int max = 0;
            Cursor = Cursors.WaitCursor;
            try { doc.ApplyPixels(px => max = Pixels.TanToWhiteAlpha(px, normalize)); }
            finally { Cursor = Cursors.Default; }
            if (max == 0) { doc.Undo(); stInfo.Text = "변환할 어두운 영역이 없습니다"; return; }
            doc.TransparentBg = true; cmbBg.SelectedIndex = 1;
            RefreshView();
            stInfo.Text = normalize
                ? string.Format("탠라인 → 타투 변환 완료 (가장 진한 곳 진하기 {0} → 불투명). Ctrl+Z로 되돌리기", max)
                : string.Format("탠라인 → 타투 변환 완료 (진하기 그대로, 최대 알파 {0}). Ctrl+Z로 되돌리기", max);
        }

        // ------------------------------------------------------------ 3D 보기 / 투영
        void PickBody()
        {
            using (var d = new OpenFileDialog { Filter = "OBJ|*.obj", Title = "바디 메쉬 OBJ (Material Editor 내보내기)" })
                if (d.ShowDialog(this) == DialogResult.OK && LoadBody(d.FileName)) { SaveSettings(); if (view != ViewKind.UV) RefreshView(true); }
        }
        bool LoadBody(string path)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                body = new BodyMesh(path, doc != null ? doc.W : DocW); bodyPath = path; viewBuf.Clear();
                stInfo.Text = string.Format("바디: {0} (면 {1:N0})", Path.GetFileName(path), body.FaceCount);
                return true;
            }
            catch (Exception ex) { MessageBox.Show(this, "바디 OBJ를 읽을 수 없습니다.\n" + ex.Message); body = null; return false; }
            finally { Cursor = Cursors.Default; }
        }
        bool EnsureBody()
        {
            if (body != null && doc != null && body.N == doc.W) return true;
            if (!string.IsNullOrEmpty(bodyPath) && File.Exists(bodyPath) && LoadBody(bodyPath)) return true;
            PickBody(); return body != null;
        }
        void SetView(ViewKind v)
        {
            if (v == view || doc == null) { SyncViewCombo(); return; }
            if (canvas.Part != null)
            {
                var r = MessageBox.Show(this, "배치 중인 파트를 현재 보기에서 확정할까요?\n(아니요 = 버림)", "KKSkinTool", MessageBoxButtons.YesNoCancel);
                if (r == DialogResult.Cancel) { SyncViewCombo(); return; }
                if (r == DialogResult.Yes) CommitPart(); else CancelPart();
            }
            if (Liquifying) EndLiquify(true);
            if (v != ViewKind.UV && !EnsureBody()) { SyncViewCombo(); return; }
            view = v; SyncViewCombo();
            RefreshView(true);
            stInfo.Text = v == ViewKind.UV ? "UV 보기" : "3D 보기: 이 화면에 놓은 파트는 확정할 때 몸에 투영되어 UV로 구워집니다";
        }
        void SyncViewCombo() { syncing = true; cmbView.SelectedIndex = (int)view; syncing = false; }
        /// <summary>3D 보기 이미지 갱신 (결과가 바뀔 때마다)</summary>
        void RefreshView(bool fit = false)
        {
            var old = canvas.ViewImage;
            if (view == ViewKind.UV || body == null) canvas.ViewImage = null;
            else
            {
                Cursor = Cursors.WaitCursor;
                float[][] b;
                if (!viewBuf.TryGetValue(view, out b))
                {
                    float[] z, uv, sh; body.RenderBuffers(view, out z, out uv, out sh); viewBuf[view] = b = new[] { z, uv, sh };
                }
                canvas.ViewImage = body.RenderView(view, doc.Result, b[0], b[1], b[2]);
                Cursor = Cursors.Default;
            }
            if (old != null && old != canvas.ViewImage) old.Dispose();
            if (fit) canvas.FitView(); else canvas.Invalidate();
        }
        /// <summary>3D 보기에서 확정: 파트를 뷰 크기 이미지로 그린 뒤 메쉬에 투영해 UV 결과에 굽기</summary>
        void CommitProjected(PartLayer p)
        {
            var b = viewBuf[view];
            using (var tmp = new Bitmap(doc.W, doc.H, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(tmp))
                using (var m = p.GetMatrix())
                using (var ia = p.Attr(false))
                {
                    g.Clear(p.S.Blend == BlendKind.Normal ? Color.Transparent : Color.White);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.Transform = m;
                    g.DrawImage(p.Source, new Rectangle(0, 0, p.W, p.H), 0, 0, p.W, p.H, GraphicsUnit.Pixel, ia);
                }
                var rendered = Pixels.Read(tmp);
                doc.ApplyPixels(px => body.Bake(view, rendered, p.S.Blend, px, b[0]));
            }
        }

        // ------------------------------------------------------------ 유동화
        bool Liquifying { get { return canvas.Liquify != null; } }
        void ToggleLiquify() { if (Liquifying) EndLiquify(true); else StartLiquify(); }
        void StartLiquify()
        {
            if (doc == null) return;
            if (canvas.Part != null && canvas.Part.IsFloat && !ResolveActive(true)) return;
            var part = canvas.Part;
            if (part != null)
            {   // 배치 중인 파트 자체를 유동화 (파트 원본 이미지에 적용)
                liquifyPart = part;
                canvas.Liquify = new LiquifySession(part.Source)
                {
                    MapIn = d => part.ToLocal(d), ViewScale = part.DocPerLocal(),
                    Rendered = r => part.RefreshDisplayRegion(r)
                };
                stInfo.Text = "파트 유동화 — Enter 적용, Esc 취소 (파트는 계속 배치 중)";
            }
            else
            {
                if (view != ViewKind.UV) { SetView(ViewKind.UV); if (view != ViewKind.UV) return; }
                doc.SaveUndoPoint();
                canvas.Liquify = new LiquifySession(doc.Result);
                stInfo.Text = "결과 유동화 — Enter 적용, Esc 취소";
            }
            SetSelectTool(CanvasView.SelectTool.None);
            ApplyLiqSettings();
            liqPanel.Visible = true; liqPanel.BringToFront(); btnLiq.Checked = true;
            canvas.Focus(); canvas.Invalidate();
        }
        void EndLiquify(bool apply)
        {
            var l = canvas.Liquify; if (l == null) return;
            if (liquifyPart != null)
            {
                if (!apply) { l.Cancel(); liquifyPart.RefreshDisplay(true); stInfo.Text = "파트 유동화 취소"; }
                else stInfo.Text = l.Changed ? "파트 유동화 적용" : "파트 유동화 종료 (변경 없음)";
                liquifyPart = null;
            }
            else if (!apply) { l.Cancel(); doc.DropLastUndo(); stInfo.Text = "유동화 취소"; }
            else if (!l.Changed) { doc.DropLastUndo(); stInfo.Text = "유동화 종료 (변경 없음)"; }
            else { doc.Dirty = true; stInfo.Text = "유동화 적용"; }
            canvas.Liquify = null; liqPanel.Visible = false; btnLiq.Checked = false; canvas.Invalidate();
        }
        void ApplyLiqSettings()
        {
            var l = canvas.Liquify; if (l == null || syncing) return;
            l.Tool = (LiquifyTool)Math.Max(0, cmbLiqTool.SelectedIndex);
            l.Radius = (float)numLiqSize.Value / l.ViewScale; l.Strength = (float)numLiqStr.Value / 100f;
            canvas.Invalidate();
        }

        // ------------------------------------------------------------ 영역 선택 → 떠 있는 선택(이동·크기·회전 후 확정/취소)
        void ToggleSelect(CanvasView.SelectTool t) { SetSelectTool(canvas.Select == t ? CanvasView.SelectTool.None : t); }
        void SetSelectTool(CanvasView.SelectTool t)
        {
            if (t != CanvasView.SelectTool.None)
            {
                if (Liquifying) EndLiquify(true);
                if (canvas.Part != null && canvas.Part.IsFloat) { stInfo.Text = "먼저 지금 선택 영역을 확정(Enter)하거나 취소(Esc)하세요"; t = CanvasView.SelectTool.None; }
                else if (canvas.Part == null && view != ViewKind.UV) { stInfo.Text = "결과 이미지 선택은 UV 보기(F2)에서 합니다"; t = CanvasView.SelectTool.None; }
            }
            canvas.Select = t; btnSelRect.Checked = t == CanvasView.SelectTool.Rect; btnSelLasso.Checked = t == CanvasView.SelectTool.Lasso;
            if (t != CanvasView.SelectTool.None)
                stInfo.Text = canvas.Part != null ? "파트에서 변형할 부분을 드래그로 선택하세요" : "결과 이미지에서 변형할 영역을 드래그로 선택하세요";
        }
        void OnSelection(PointF[] poly)
        {
            SetSelectTool(CanvasView.SelectTool.None);
            if (doc == null) return;
            var part = canvas.Part;
            PartLayer fl;
            if (part != null)
            {   // 파트 안 일부: 파트 로컬 좌표로 잘라냄
                var local = poly.Select(part.ToLocal).ToArray();
                var backup = (Bitmap)part.Source.Clone();
                Point o; var bmp = Pixels.Lift(part.Source, local, part.S.Blend == BlendKind.Normal ? Color.Transparent : Color.White, out o);
                if (bmp == null) { backup.Dispose(); stInfo.Text = "선택 영역이 파트 밖입니다"; return; }
                part.RefreshDisplay(true);
                fl = new PartLayer(bmp, "선택 영역 (" + part.Name + ")", part.S.Blend) { Parent = part, Backup = backup };
                fl.S.Cx = o.X + bmp.Width / 2f; fl.S.Cy = o.Y + bmp.Height / 2f;
                parentHistory = new List<PartState>(partHistory);
            }
            else
            {   // 결과 이미지 일부
                var backup = (Bitmap)doc.Result.Clone();
                Point o; var bmp = Pixels.Lift(doc.Result, poly, doc.TransparentBg ? Color.Transparent : Color.White, out o);
                if (bmp == null) { backup.Dispose(); return; }
                fl = new PartLayer(bmp, "선택 영역 (결과)", doc.TransparentBg ? BlendKind.Normal : BlendKind.Darken) { Backup = backup };
                fl.S.Cx = o.X + bmp.Width / 2f; fl.S.Cy = o.Y + bmp.Height / 2f;
                parentHistory = null;
            }
            canvas.Part = fl; partHistory.Clear();
            UpdatePartUi(); SyncFromPart(); RefreshView(); canvas.Focus();
            stInfo.Text = "선택 영역 변형 중 — 이동·크기·회전 후 Enter 확정 / Esc 취소";
        }
        /// <summary>배치 중인 것(선택 영역 → 파트 순)을 확정/취소할지 물어봄. floatOnly면 선택 영역만</summary>
        bool ResolveActive(bool floatOnly = false)
        {
            while (canvas.Part != null && (!floatOnly || canvas.Part.IsFloat))
            {
                var what = canvas.Part.IsFloat ? "선택 영역" : "배치 중인 파트";
                var r = MessageBox.Show(this, what + "을(를) 확정할까요?\n(아니요 = 취소)", "KKSkinTool", MessageBoxButtons.YesNoCancel);
                if (r == DialogResult.Cancel) return false;
                if (r == DialogResult.Yes) CommitPart(); else CancelPart();
            }
            return true;
        }

        // ------------------------------------------------------------ 색상
        void PickTint()
        {
            var p = canvas.Part; if (p == null) return;
            using (var d = new ColorDialog { FullOpen = true, Color = p.S.Tint != 0 ? Color.FromArgb(p.S.Tint) : (p.S.Blend == BlendKind.Normal ? Color.White : Color.Black) })
                if (d.ShowDialog(this) == DialogResult.OK) SetTint(Color.FromArgb(255, d.Color).ToArgb());
        }
        void SetTint(int argb)
        {
            var p = canvas.Part; if (p == null) return;
            RecordPartState(); p.S.Tint = argb; SyncFromPart(); canvas.Invalidate();
        }

        // ------------------------------------------------------------ 일괄 조합
        void OpenBatch()
        {
            using (var f = new BatchCombineForm(batchTop, batchBottom, batchOut, DocW, DocH))
            {
                f.Icon = Icon;
                f.ShowDialog(this);
                batchTop = f.TopDir; batchBottom = f.BottomDir; batchOut = f.OutDir; SaveSettings();
            }
        }

        // ------------------------------------------------------------ 파트
        void LoadPart()
        {
            if (doc == null) return;
            if (Liquifying) EndLiquify(true);
            if (!ResolveActive()) return;
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
            if (Liquifying) EndLiquify(true);
            var p = canvas.Part;
            Cursor = Cursors.WaitCursor;
            try
            {
                if (p.IsFloat && p.Parent != null)
                {   // 파트 안 선택 영역 → 부모 파트 원본에 합침, 부모 파트 배치는 계속
                    using (var m = p.GetLocalMatrix()) Doc.DrawPart(p.Parent.Source, p, m, false);
                    p.Parent.RefreshDisplay(true);
                    var parent = p.Parent; p.Parent = null; p.Dispose();
                    canvas.Part = parent; partHistory.Clear(); if (parentHistory != null) partHistory.AddRange(parentHistory);
                    stInfo.Text = "선택 영역을 파트에 반영 — 파트는 계속 배치 중";
                    UpdatePartUi(); SyncFromPart(); canvas.Invalidate(); return;
                }
                if (view == ViewKind.UV || p.IsFloat) doc.Commit(p);
                else CommitProjected(p);
            }
            finally { Cursor = Cursors.Default; }
            stInfo.Text = "확정: " + p.Name;
            p.Dispose(); canvas.Part = null; partHistory.Clear();
            UpdatePartUi(); RefreshView();
        }
        void CancelPart()
        {
            var p = canvas.Part; if (p == null) return;
            if (Liquifying) EndLiquify(false);
            if (p.IsFloat && p.Parent != null)
            {   // 부모 파트를 잘라내기 전으로
                using (var g = Graphics.FromImage(p.Parent.Source)) { g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy; g.DrawImageUnscaled(p.Backup, 0, 0); }
                p.Parent.RefreshDisplay(true);
                var parent = p.Parent; p.Parent = null; p.Dispose();
                canvas.Part = parent; partHistory.Clear(); if (parentHistory != null) partHistory.AddRange(parentHistory);
                stInfo.Text = "선택 영역 취소"; UpdatePartUi(); SyncFromPart(); canvas.Invalidate(); return;
            }
            if (p.IsFloat) { doc.RestoreFrom(p.Backup); stInfo.Text = "선택 영역 취소"; }
            p.Dispose(); canvas.Part = null; partHistory.Clear();
            UpdatePartUi(); RefreshView();
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
            if (Liquifying) { canvas.Liquify.Undo(); canvas.Invalidate(); return; }
            if (canvas.Part != null)
            {
                if (partHistory.Count > 0)
                {
                    canvas.Part.S = partHistory[partHistory.Count - 1]; partHistory.RemoveAt(partHistory.Count - 1);
                    canvas.Part.RefreshDisplay(); SyncFromPart(); canvas.Invalidate();
                }
                return;     // 파트 배치 중에는 파트 변환만 되돌림
            }
            if (doc != null && doc.CanUndo) { doc.Undo(); RefreshView(); stInfo.Text = "실행 취소"; }
        }
        void DoRedo()
        {
            if (Liquifying) return;
            if (canvas.Part == null && doc != null && doc.CanRedo) { doc.Redo(); RefreshView(); stInfo.Text = "다시 실행"; }
        }

        // ------------------------------------------------------------ 속성 동기화
        void UpdatePartUi()
        {
            bool on = canvas.Part != null;
            foreach (Control c in new Control[] { numX, numY, numSX, numSY, numRot, numOpa, chkLock, cmbBlend, chkFlip, btnCommit, btnCancel, btnTint, btnTintClear }) c.Enabled = on;
            lblPart.Text = on ? string.Format("{0}\n{1}×{2}{3}", canvas.Part.Name, canvas.Part.W, canvas.Part.H, canvas.Part.Parent != null ? "  (좌표: 파트 기준)" : "")
                              : "파트 없음 (Ctrl+O 불러오기 / M 영역 선택)";
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
            btnTint.BackColor = p.S.Tint != 0 ? Color.FromArgb(p.S.Tint) : SystemColors.Control;
            btnTint.ForeColor = p.S.Tint != 0 && Color.FromArgb(p.S.Tint).GetBrightness() < 0.5f ? Color.White : Color.Black;
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
                case Keys.Control | Keys.B: OpenBatch(); return true;
                case Keys.F2: SetView(ViewKind.UV); return true;
                case Keys.F3: SetView(ViewKind.Front); return true;
                case Keys.F4: SetView(ViewKind.Back); return true;
                case Keys.F5: SetView(ViewKind.Left); return true;
                case Keys.F6: SetView(ViewKind.Right); return true;
                case Keys.Control | Keys.D0: canvas.FitView(); return true;
                case Keys.Control | Keys.D1: canvas.SetZoom(1f, new Point(canvas.Width / 2, canvas.Height / 2)); return true;
                case Keys.Control | Keys.Oemplus: canvas.SetZoom(canvas.Zoom * 1.25f, new Point(canvas.Width / 2, canvas.Height / 2)); return true;
                case Keys.Control | Keys.OemMinus: canvas.SetZoom(canvas.Zoom / 1.25f, new Point(canvas.Width / 2, canvas.Height / 2)); return true;
                case Keys.Enter:
                    if (Liquifying) { EndLiquify(true); return true; }
                    if (canvas.Part != null) { CommitPart(); return true; } break;
                case Keys.Escape:
                    if (canvas.Select != CanvasView.SelectTool.None) { SetSelectTool(CanvasView.SelectTool.None); return true; }
                    if (Liquifying) { EndLiquify(false); return true; }
                    if (canvas.Part != null) { CancelPart(); return true; } break;
                case Keys.L: if (canvasFocus) { ToggleLiquify(); return true; } break;
                case Keys.M: if (canvasFocus) { ToggleSelect(CanvasView.SelectTool.Rect); return true; } break;
                case Keys.Shift | Keys.M: if (canvasFocus) { ToggleSelect(CanvasView.SelectTool.Lasso); return true; } break;
            }
            if (canvasFocus && Liquifying)
            {
                if (keyData == Keys.OemOpenBrackets) { numLiqSize.Value = Math.Max(numLiqSize.Minimum, numLiqSize.Value / 1.15M); return true; }
                if (keyData == Keys.OemCloseBrackets) { numLiqSize.Value = Math.Min(numLiqSize.Maximum, numLiqSize.Value * 1.15M); return true; }
                if (keyData >= Keys.D1 && keyData <= Keys.D7) { cmbLiqTool.SelectedIndex = keyData - Keys.D1; return true; }
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
            if (Liquifying) EndLiquify(true);
            if (canvas.Part != null || (doc != null && doc.Dirty))
                if (MessageBox.Show(this, "저장하지 않은 작업이 있습니다. 종료할까요?", "KKSkinTool", MessageBoxButtons.YesNo) != DialogResult.Yes) { e.Cancel = true; return; }
            SaveSettings();
        }
    }
}
