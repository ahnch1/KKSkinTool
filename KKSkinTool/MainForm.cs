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
        MeshModel mesh; ObjFile meshObj; string bodyPath = "", meshAxis = "+z"; string[] meshGroups;
        ComboBox cmbSize;
        static readonly int[] Sizes = { 512, 1024, 2048, 4096 };
        string lastViewDir = "";
        ComboBox cmbMap, cmbNFx, cmbDFx; NumericUpDown numFx; Label lblFx;
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
            // ---------------- 상태 보관용 콤보 (메뉴의 배경·크기 선택이 이 값을 바꿈)
            cmbBg = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cmbBg.Items.AddRange(new object[] { "흰색 (탠라인)", "투명 (타투)" }); cmbBg.SelectedIndex = 0;
            cmbSize = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var z in Sizes) cmbSize.Items.Add(z.ToString()); cmbSize.SelectedIndex = 2;

            // ---------------- 메뉴
            var menu = new MenuStrip { Padding = new Padding(4, 2, 0, 2) };
            // 파일
            var mFile = new ToolStripMenuItem("파일(&F)");
            mFile.DropDownItems.Add(Mi("새 문서", "Ctrl+N", (s, e) => NewDoc()));
            var mBg = new ToolStripMenuItem("배경");
            for (int i = 0; i < cmbBg.Items.Count; i++) { int k = i; mBg.DropDownItems.Add(Mi(cmbBg.Items[i].ToString(), null, (s, e) => cmbBg.SelectedIndex = k)); }
            mBg.DropDownOpening += (s, e) => { for (int i = 0; i < mBg.DropDownItems.Count; i++) ((ToolStripMenuItem)mBg.DropDownItems[i]).Checked = i == cmbBg.SelectedIndex; };
            var mSize = new ToolStripMenuItem("크기");
            for (int i = 0; i < Sizes.Length; i++) { int k = i; mSize.DropDownItems.Add(Mi(Sizes[i] + " × " + Sizes[i], null, (s, e) => cmbSize.SelectedIndex = k)); }
            mSize.DropDownOpening += (s, e) => { for (int i = 0; i < mSize.DropDownItems.Count; i++) ((ToolStripMenuItem)mSize.DropDownItems[i]).Checked = i == cmbSize.SelectedIndex; };
            mFile.DropDownItems.Add(mBg); mFile.DropDownItems.Add(mSize);
            mFile.DropDownItems.Add(new ToolStripMenuItem("  (배경·크기는 새 문서에 적용)") { Enabled = false });
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(Mi("결과 열기…", null, (s, e) => OpenResult()));
            mFile.DropDownItems.Add(Mi("PNG 저장… (지금 보는 맵)", "Ctrl+S", (s, e) => SaveResult()));
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(Mi("재질 세트 열기… (MainTex + NormalMap + DetailMask)", null, (s, e) => OpenMaterial()));
            mFile.DropDownItems.Add(Mi("재질 세트 저장…", null, (s, e) => SaveMaterial()));
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(Mi("끝내기", "Alt+F4", (s, e) => Close()));
            // 편집
            var mEdit = new ToolStripMenuItem("편집(&E)");
            mEdit.DropDownItems.Add(Mi("파트 불러오기…", "Ctrl+O", (s, e) => LoadPart()));
            var miRect = Mi("사각 선택", "M", (s, e) => ToggleSelect(CanvasView.SelectTool.Rect));
            var miLasso = Mi("올가미 선택", "Shift+M", (s, e) => ToggleSelect(CanvasView.SelectTool.Lasso));
            mEdit.DropDownItems.Add(miRect); mEdit.DropDownItems.Add(miLasso);
            mEdit.DropDownItems.Add(new ToolStripSeparator());
            var miLiq = Mi("유동화", "L", (s, e) => ToggleLiquify());
            mEdit.DropDownItems.Add(miLiq);
            mEdit.DropDownItems.Add(new ToolStripSeparator());
            mEdit.DropDownItems.Add(Mi("화면 맞춤", "Ctrl+0", (s, e) => canvas.FitView()));
            mEdit.DropDownItems.Add(Mi("100%", "Ctrl+1", (s, e) => canvas.SetZoom(1f, new Point(canvas.Width / 2, canvas.Height / 2))));
            mEdit.DropDownItems.Add(new ToolStripSeparator());
            var miUndo = Mi("실행 취소", "Ctrl+Z", (s, e) => DoUndo());
            var miRedo = Mi("다시 실행", "Ctrl+Y", (s, e) => DoRedo());
            mEdit.DropDownItems.Add(miUndo); mEdit.DropDownItems.Add(miRedo);
            mEdit.DropDownItems.Add(new ToolStripSeparator());
            var mMap = new ToolStripMenuItem("맵 보기");
            for (int i = 0; i < 3; i++) { int k = i; mMap.DropDownItems.Add(Mi(Doc.LayerNames[i], "F" + (7 + i), (s, e) => SetMap(k))); }
            mMap.DropDownOpening += (s, e) => { for (int i = 0; i < 3; i++) { var it = (ToolStripMenuItem)mMap.DropDownItems[i]; it.Checked = doc != null && doc.Active == i; it.Enabled = i == 0 || (doc != null && doc.Layers[i] != null); } };
            mEdit.DropDownItems.Add(mMap);
            mEdit.DropDownOpening += (s, e) =>
            {
                miRect.Checked = canvas.SelTool == CanvasView.SelectTool.Rect; miLasso.Checked = canvas.SelTool == CanvasView.SelectTool.Lasso;
                miLiq.Checked = Liquifying;
                miUndo.Enabled = Liquifying || canvas.Part != null || (doc != null && doc.CanUndo);
                miRedo.Enabled = !Liquifying && canvas.Part == null && doc != null && doc.CanRedo;
            };
            // 3D
            var m3d = new ToolStripMenuItem("&3D");
            m3d.DropDownItems.Add(Mi("베이스(wireline) 불러오기…", null, (s, e) => PickWire()));
            m3d.DropDownItems.Add(Mi("3D 메쉬 (OBJ)…", null, (s, e) => PickBody()));
            var mView = new ToolStripMenuItem("보기");
            var viewNames = new[] { "UV", "정면", "후면", "왼쪽", "오른쪽" };
            for (int i = 0; i < viewNames.Length; i++) { var k = (ViewKind)i; mView.DropDownItems.Add(Mi(viewNames[i], "F" + (i + 2), (s, e) => SetView(k))); }
            mView.DropDownOpening += (s, e) => { for (int i = 0; i < mView.DropDownItems.Count; i++) ((ToolStripMenuItem)mView.DropDownItems[i]).Checked = i == (int)view; };
            m3d.DropDownItems.Add(mView);
            m3d.DropDownItems.Add(new ToolStripSeparator());
            m3d.DropDownItems.Add(Mi("뷰 내보내기…", null, (s, e) => ExportViews()));
            m3d.DropDownItems.Add(Mi("뷰 불러오기…", null, (s, e) => ImportViews()));
            // 도구
            var mTool = new ToolStripMenuItem("도구(&T)");
            mTool.DropDownItems.Add(Mi("탠라인 → 타투 (가장 진한 곳 = 불투명)", null, (s, e) => TanToTattoo(true)));
            mTool.DropDownItems.Add(Mi("탠라인 → 타투 (진하기 그대로)", null, (s, e) => TanToTattoo(false)));
            mTool.DropDownItems.Add(new ToolStripSeparator());
            mTool.DropDownItems.Add(Mi("TOP × BOTTOM 일괄 조합…", "Ctrl+B", (s, e) => OpenBatch()));
            mTool.DropDownItems.Add(new ToolStripSeparator());
            mTool.DropDownItems.Add(Mi("NormalMap 만들기 (MainTex 밝기 기준)", null, (s, e) => MakeMap(Doc.NORMAL, false)));
            mTool.DropDownItems.Add(Mi("NormalMap 평평하게 만들기", null, (s, e) => MakeMap(Doc.NORMAL, true)));
            mTool.DropDownItems.Add(Mi("DetailMask 만들기 (MainTex 기준)", null, (s, e) => MakeMap(Doc.DETAIL, false)));
            menu.Items.AddRange(new ToolStripItem[] { mFile, mEdit, m3d, mTool });
            MainMenuStrip = menu;

            // ---------------- 도구 모음 (자주 쓰는 것만)
            var ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4) };
            ts.Items.Add(Btn("새 문서", "Ctrl+N (배경·크기는 파일 메뉴)", (s, e) => NewDoc()));
            ts.Items.Add(Btn("3D 메쉬 (OBJ)", "3D 보기용 메쉬 — 바디(o_body_*.obj)나 의상 OBJ (Material Editor 내보내기). 그룹·앞 방향 선택", (s, e) => PickBody()));
            ts.Items.Add(new ToolStripLabel("보기"));
            cmbView = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
            cmbView.Items.AddRange(new object[] { "UV (F2)", "정면 (F3)", "후면 (F4)", "왼쪽 (F5)", "오른쪽 (F6)" }); cmbView.SelectedIndex = 0;
            cmbView.SelectedIndexChanged += (s, e) => { if (!syncing) SetView((ViewKind)cmbView.SelectedIndex); };
            ts.Items.Add(new ToolStripControlHost(cmbView));
            ts.Items.Add(new ToolStripLabel("맵"));
            cmbMap = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            cmbMap.Items.AddRange(new object[] { "MainTex (F7)", "NormalMap (F8)", "DetailMask (F9)" }); cmbMap.SelectedIndex = 0;
            cmbMap.SelectedIndexChanged += (s, e) => { if (!syncing) SetMap(cmbMap.SelectedIndex); };
            ts.Items.Add(new ToolStripControlHost(cmbMap));
            ts.Items.Add(Btn("PNG 저장", "Ctrl+S", (s, e) => SaveResult()));
            ts.Items.Add(new ToolStripSeparator());
            ts.Items.Add(Btn("파트 불러오기", "Ctrl+O", (s, e) => LoadPart()));
            btnSelRect = Btn("사각 선택", "M: 결과(파트가 없을 때) 또는 배치 중인 파트의 일부를 사각형으로 선택해 이동·크기·회전", (s, e) => ToggleSelect(CanvasView.SelectTool.Rect));
            btnSelLasso = Btn("올가미 선택", "Shift+M: 자유 곡선으로 선택", (s, e) => ToggleSelect(CanvasView.SelectTool.Lasso));
            ts.Items.Add(btnSelRect); ts.Items.Add(btnSelLasso);
            btnLiq = Btn("유동화", "L: 파트가 있으면 파트에, 없으면 결과 이미지에 브러시 변형", (s, e) => ToggleLiquify());
            ts.Items.Add(btnLiq);
            ts.Items.Add(new ToolStripSeparator());
            ts.Items.Add(Btn("화면 맞춤", "Ctrl+0", (s, e) => canvas.FitView()));
            ts.Items.Add(Btn("100%", "Ctrl+1", (s, e) => canvas.SetZoom(1f, new Point(canvas.Width / 2, canvas.Height / 2))));

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
            lblFx = new Label { Text = "다른 맵 연동 (재질 세트, MainTex 편집 시)", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(0, 8, 0, 0) };
            side.Controls.Add(lblFx); side.SetColumnSpan(lblFx, 2);
            side.Controls.Add(new Label { Text = "노멀", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            cmbNFx = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            cmbNFx.Items.AddRange(new object[] { "없음", "볼록 (패치·자수)", "오목 (찢김·구멍)", "평평하게 (결 지움)" }); side.Controls.Add(cmbNFx);
            side.Controls.Add(new Label { Text = "디테일", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            cmbDFx = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            cmbDFx.Items.AddRange(new object[] { "없음", "가장자리 음영", "음영 진하게", "음영 지우기", "광택 부위" }); side.Controls.Add(cmbDFx);
            numFx = Num(side, "연동 세기 %", 0, 100, 5, 0); numFx.Value = 60;
            cmbNFx.SelectedIndex = 0; cmbDFx.SelectedIndex = 0;
            side.Controls.Add(new Label { Text = "합성", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            cmbBlend = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            cmbBlend.Items.AddRange(new object[] { "어둡게 (탠라인)", "곱하기", "일반 (타투)", "구멍 (알파 지우기)" });
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
                       "· Enter 확정, Esc 취소\n· Ctrl+Z 실행 취소, Ctrl+Y 다시 실행\n· Ctrl+O 파트, Ctrl+S 저장, Ctrl+0 맞춤\n· Ctrl+B TOP × BOTTOM 일괄 조합\n· L 유동화 (파트가 있으면 파트에, 없으면 결과에)\n· M 사각 선택 / Shift+M 올가미: 파트 일부 또는\n  결과 일부를 떼어 이동·크기·회전 → Enter/Esc\n· 색상 변경: 오른쪽 [변경…]\n· [탠라인 → 타투] 투명 바탕 흰색 변환\n  (Shift+클릭: 진하기 그대로)\n· F2 UV / F3 정면 / F4 후면 / F5·F6 옆 보기\n  (3D 보기에서 확정하면 메쉬에 투영)"
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

            Controls.Add(canvas); Controls.Add(side); Controls.Add(liqPanel); Controls.Add(ts); Controls.Add(menu); Controls.Add(st);
            canvas.LiquifyStrokeEnd += () => { if (canvas.Liquify != null) { syncing = true; numLiqSize.Value = Math.Max(numLiqSize.Minimum, Math.Min(numLiqSize.Maximum, (decimal)(canvas.Liquify.Radius * canvas.Liquify.ViewScale))); syncing = false; } };
            canvas.SelectionMade += OnSelection;

            foreach (var n in new[] { numX, numY, numSX, numSY, numRot, numOpa }) n.ValueChanged += OnPropChanged;
            cmbBlend.SelectedIndexChanged += OnPropChanged; cmbNFx.SelectedIndexChanged += OnPropChanged; cmbDFx.SelectedIndexChanged += OnPropChanged; numFx.ValueChanged += OnPropChanged; chkFlip.CheckedChanged += OnPropChanged;
            chkWire.CheckedChanged += (s, e) => { canvas.ShowWire = chkWire.Checked; canvas.Invalidate(); };
            trkWire.ValueChanged += (s, e) => { canvas.WireOpacity = trkWire.Value / 100f; canvas.Invalidate(); };

            UpdatePartUi();
            Shown += (s, e) => Startup();
            FormClosing += OnClosingForm;
        }

        /// <summary>메뉴 항목. 단축키는 표시만 (실제 처리는 ProcessCmdKey — 중복 실행 방지)</summary>
        static ToolStripMenuItem Mi(string text, string keys, EventHandler click)
        {
            var m = new ToolStripMenuItem(text); if (click != null) m.Click += click;
            if (!string.IsNullOrEmpty(keys)) m.ShortcutKeyDisplayString = keys;
            return m;
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
            if (l.Length > 7 && l[7].Length > 0) meshGroups = l[7].Split('\t');
            if (l.Length > 8 && l[8].Length > 0) meshAxis = l[8];
            if (l.Length > 10) lastViewDir = l[10];
            int sz; if (l.Length > 9 && int.TryParse(l[9], out sz) && Array.IndexOf(Sizes, sz) >= 0) cmbSize.SelectedIndex = Array.IndexOf(Sizes, sz);
        }
        void SaveSettings()
        {
            try { File.WriteAllLines(settingsPath, new[] { wirePath ?? "", lastPartDir ?? "", lastSaveDir ?? "", batchTop ?? "", batchBottom ?? "", batchOut ?? "", bodyPath ?? "", meshGroups != null ? string.Join("\t", meshGroups) : "", meshAxis ?? "+z", Sizes[Math.Max(0, cmbSize.SelectedIndex)].ToString(), lastViewDir ?? "" }); } catch { }
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
        /// <summary>새 문서 크기 (도구 모음 "크기": 512 / 1024 / 2048 / 4096)</summary>
        int DocW { get { return Sizes[Math.Max(0, cmbSize.SelectedIndex)]; } }
        int DocH { get { return DocW; } }

        /// <summary>문서 크기가 바뀌면 메쉬 텍셀 지도와 와이어를 그 크기로 다시 만듦</summary>
        void AfterDocChanged()
        {
            if (meshObj != null && (mesh == null || mesh.TW != doc.W || mesh.TH != doc.H)) BuildMesh();
            if (view != ViewKind.UV && mesh == null) { view = ViewKind.UV; SyncViewCombo(); }
            Text = string.Format("KKSkinTool — {0}×{1}{2}", doc.W, doc.H, doc.IsMaterial ? "  [재질 세트]" : "");
            SyncMapCombo();
            RefreshView(true);
        }

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
            canvas.Doc = doc; AfterDocChanged();
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
                doc = Doc.Open(d.FileName, 0, 0);                                    // 이미지 원래 크기 그대로 (예: 의상 MainTex 4096)
                int si = Array.IndexOf(Sizes, doc.W); if (si >= 0 && doc.W == doc.H) cmbSize.SelectedIndex = si;
                cmbBg.SelectedIndex = doc.TransparentBg ? 1 : 0;
                canvas.Doc = doc; AfterDocChanged();
            }
        }
        void SaveResult()
        {
            if (doc == null) return;
            if (Liquifying) EndLiquify(true);
            if (canvas.Part != null &&
                MessageBox.Show(this, "배치 중인 파트가 아직 확정되지 않았습니다. 확정하고 저장할까요?", "KKSkinTool", MessageBoxButtons.YesNo) == DialogResult.Yes)
                CommitPart();
            using (var d = new SaveFileDialog { Filter = "PNG|*.png", FileName = doc.LayerFiles[doc.Active] ?? (doc.IsMaterial ? Doc.LayerNames[doc.Active] + ".png" : "skin_result.png"), InitialDirectory = lastSaveDir })
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
            if (doc.Active != Doc.MAIN) SetMap(Doc.MAIN);
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
            using (var d = new OpenFileDialog { Filter = "OBJ|*.obj", Title = "3D 메쉬 OBJ (바디 / 의상, Material Editor 내보내기)" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                ObjFile obj;
                try { Cursor = Cursors.WaitCursor; obj = ObjFile.Load(d.FileName); }
                catch (Exception ex) { MessageBox.Show(this, "OBJ를 읽을 수 없습니다.\n" + ex.Message); return; }
                finally { Cursor = Cursors.Default; }
                string[] groups; string axis;
                if (!MeshOptionsDialog.Ask(this, obj, d.FileName == bodyPath ? meshGroups : null, meshAxis, out groups, out axis)) return;
                meshObj = obj; meshGroups = groups; meshAxis = axis; bodyPath = d.FileName;
                if (BuildMesh()) { SaveSettings(); if (view != ViewKind.UV) RefreshView(true); }
            }
        }
        /// <summary>현재 문서 크기로 메쉬(텍셀 지도) 만들기</summary>
        bool BuildMesh()
        {
            if (meshObj == null || doc == null) return false;
            try
            {
                Cursor = Cursors.WaitCursor;
                mesh = new MeshModel(meshObj, meshGroups, doc.W, doc.H, meshAxis); viewBuf.Clear();
                if (canvas.Wire != null) canvas.Wire.Dispose();
                canvas.Wire = mesh.RenderUVWire(Color.FromArgb(255, 20, 60, 140));
                stInfo.Text = string.Format("메쉬: {0} (면 {1:N0}, 텍셀 {2:N0}, {3}×{4}) — UV 와이어도 이 메쉬로 표시", Path.GetFileName(meshObj.Path), mesh.FaceCount, mesh.ValidTexels, doc.W, doc.H);
                canvas.Invalidate();
                return true;
            }
            catch (Exception ex) { MessageBox.Show(this, "메쉬를 만들 수 없습니다.\n" + ex.Message); mesh = null; return false; }
            finally { Cursor = Cursors.Default; }
        }
        bool EnsureBody()
        {
            if (mesh != null && doc != null && mesh.TW == doc.W && mesh.TH == doc.H) return true;
            if (meshObj == null && !string.IsNullOrEmpty(bodyPath) && File.Exists(bodyPath))
            {
                try { Cursor = Cursors.WaitCursor; meshObj = ObjFile.Load(bodyPath); }
                catch { meshObj = null; }
                finally { Cursor = Cursors.Default; }
                if (meshObj != null && meshGroups != null && !meshGroups.All(g => meshObj.Groups.ContainsKey(g))) meshGroups = null;
            }
            if (meshObj != null && BuildMesh()) return true;
            PickBody(); return mesh != null;
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
            stInfo.Text = v == ViewKind.UV ? "UV 보기" : "3D 보기: 이 화면에 놓은 파트는 확정할 때 메쉬에 투영되어 UV로 구워집니다";
        }
        void SyncViewCombo() { syncing = true; cmbView.SelectedIndex = (int)view; syncing = false; }
        /// <summary>3D 보기 이미지 갱신 (결과가 바뀔 때마다)</summary>
        void RefreshView(bool fit = false)
        {
            var old = canvas.ViewImage;
            if (view == ViewKind.UV || mesh == null) canvas.ViewImage = null;
            else
            {
                Cursor = Cursors.WaitCursor;
                float[][] b;
                if (!viewBuf.TryGetValue(view, out b))
                {
                    viewBuf.Clear();                                                  // 4096에서도 메모리 부담 없게 현재 보기만 보관
                    float[] z, uv, sh; mesh.RenderBuffers(view, out z, out uv, out sh); viewBuf[view] = b = new[] { z, uv, sh };
                }
                canvas.ViewImage = mesh.RenderView(view, doc.ActiveBitmap, b[0], b[1], b[2]);
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
                bool fx = HasFx(p);
                doc.ApplyAll(px =>
                {
                    mesh.Bake(view, rendered, p.S.Blend, px[doc.Active], b[0]);
                    if (!fx) return;
                    var cb = new byte[doc.W * doc.H * 4];                                  // 투영된 덮임 정도
                    mesh.Bake(view, MaterialGen.CoverageImage(rendered, p.S.Blend), BlendKind.Normal, cb, b[0]);
                    var cov = new float[doc.W * doc.H]; for (int i = 0; i < cov.Length; i++) cov[i] = cb[i * 4 + 3] / 255f;
                    if (px[Doc.NORMAL] != null) MaterialGen.ApplyNormalFx(px[Doc.NORMAL], cov, doc.W, doc.H, p.S.NFx, p.S.FxStrength);
                    if (px[Doc.DETAIL] != null) MaterialGen.ApplyDetailFx(px[Doc.DETAIL], cov, doc.W, doc.H, p.S.DFx, p.S.FxStrength);
                });
            }
        }

        // ------------------------------------------------------------ 재질 세트 (MainTex + NormalMap + DetailMask)
        bool HasFx(PartLayer p)
        {
            return doc != null && doc.IsMaterial && doc.Active == Doc.MAIN && !p.IsFloat && (p.S.NFx != NormalFx.None || p.S.DFx != DetailFx.None) && p.S.FxStrength > 0;
        }
        /// <summary>파트가 덮은 정도로 NormalMap/DetailMask 수정 (직전 확정과 같은 실행 취소 단계)</summary>
        void ApplyFx(float[] cov, PartState st)
        {
            if (doc.Layers[Doc.NORMAL] != null && st.NFx != NormalFx.None)
            { var n = Pixels.Read(doc.Layers[Doc.NORMAL]); MaterialGen.ApplyNormalFx(n, cov, doc.W, doc.H, st.NFx, st.FxStrength); Pixels.Write(doc.Layers[Doc.NORMAL], n); }
            if (doc.Layers[Doc.DETAIL] != null && st.DFx != DetailFx.None)
            { var d = Pixels.Read(doc.Layers[Doc.DETAIL]); MaterialGen.ApplyDetailFx(d, cov, doc.W, doc.H, st.DFx, st.FxStrength); Pixels.Write(doc.Layers[Doc.DETAIL], d); }
        }
        void SetMap(int i)
        {
            if (doc == null) return;
            if (i != Doc.MAIN && doc.Layers[i] == null)
            {
                if (MessageBox.Show(this, Doc.LayerNames[i] + "이(가) 없습니다. MainTex를 기준으로 만들까요?", "KKSkinTool", MessageBoxButtons.YesNo) != DialogResult.Yes) { SyncMapCombo(); return; }
                MakeMap(i, false); if (doc.Layers[i] == null) { SyncMapCombo(); return; }
            }
            if (i == doc.Active) { SyncMapCombo(); return; }
            if (canvas.Part != null && canvas.Part.IsFloat && !ResolveActive(true)) { SyncMapCombo(); return; }
            if (Liquifying) EndLiquify(true);
            doc.Active = i; SyncMapCombo(); UpdatePartUi(); RefreshView();
            stInfo.Text = "맵: " + Doc.LayerNames[i] + (i == Doc.MAIN ? "" : " — 이 맵에 직접 칠하기. 선택·유동화는 모든 맵에 같은 변형 적용");
        }
        void SyncMapCombo() { syncing = true; cmbMap.SelectedIndex = doc != null ? doc.Active : 0; syncing = false; }
        /// <summary>NormalMap/DetailMask 만들기 (있으면 덮어씀, 실행 취소 가능)</summary>
        void MakeMap(int i, bool flat)
        {
            if (doc == null) return;
            if (doc.Layers[i] != null && MessageBox.Show(this, Doc.LayerNames[i] + "을(를) 새로 만들어 덮어쓸까요? (Ctrl+Z로 되돌릴 수 있음)", "KKSkinTool", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            Cursor = Cursors.WaitCursor;
            try
            {
                var b = i == Doc.NORMAL ? (flat ? MaterialGen.FlatNormal(doc.W, doc.H) : MaterialGen.GenerateNormal(doc.Result)) : MaterialGen.GenerateDetail(doc.Result);
                if (doc.Layers[i] == null) doc.SetLayer(i, b, Doc.LayerNames[i] + ".png");
                else { var px = Pixels.Read(b); b.Dispose(); doc.ApplyLayer(i, d => Array.Copy(px, d, px.Length)); }
                doc.Dirty = true;
                stInfo.Text = Doc.LayerNames[i] + (flat ? " (평평)" : " (MainTex 기준)") + " 만듦";
            }
            finally { Cursor = Cursors.Default; }
            UpdatePartUi(); RefreshView();
        }
        static string FindCompanion(string dir, string mainFile, string[] keys)
        {
            foreach (var f in Directory.GetFiles(dir, "*.png"))
            {
                if (string.Equals(f, mainFile, StringComparison.OrdinalIgnoreCase)) continue;
                var n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                if (keys.Any(k => n.Contains(k))) return f;
            }
            return null;
        }
        void OpenMaterial()
        {
            if (Liquifying) EndLiquify(true);
            if (!ConfirmDiscard()) return;
            using (var d = new OpenFileDialog { Filter = "PNG|*.png", Title = "MainTex 선택 (같은 폴더의 NormalMap·DetailMask를 함께 엶)", InitialDirectory = lastSaveDir })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                while (canvas.Part != null) CancelPart();
                if (doc != null) doc.Dispose();
                Cursor = Cursors.WaitCursor;
                var notes = new List<string>();
                try
                {
                    doc = Doc.Open(d.FileName, 0, 0); doc.TransparentBg = false;
                    string dir = Path.GetDirectoryName(d.FileName);
                    var nf = FindCompanion(dir, d.FileName, new[] { "normalmap", "normal", "_nml", "_nrm", "bump" });
                    var df = FindCompanion(dir, d.FileName, new[] { "detailmask", "detail" });
                    if (nf != null) { doc.SetLayer(Doc.NORMAL, Doc.LoadBitmap(nf, 0, 0), Path.GetFileName(nf)); notes.Add("NormalMap: " + Path.GetFileName(nf)); }
                    else { doc.SetLayer(Doc.NORMAL, MaterialGen.GenerateNormal(doc.Result), "NormalMap.png"); notes.Add("NormalMap 없음 → MainTex 기준으로 만듦"); doc.Dirty = true; }
                    if (df != null) { doc.SetLayer(Doc.DETAIL, Doc.LoadBitmap(df, 0, 0), Path.GetFileName(df)); notes.Add("DetailMask: " + Path.GetFileName(df)); }
                    else { doc.SetLayer(Doc.DETAIL, MaterialGen.GenerateDetail(doc.Result), "DetailMask.png"); notes.Add("DetailMask 없음 → MainTex 기준으로 만듦"); doc.Dirty = true; }
                    lastSaveDir = dir; SaveSettings();
                }
                catch (Exception ex) { MessageBox.Show(this, "열 수 없습니다.\n" + ex.Message); NewDoc(true); return; }
                finally { Cursor = Cursors.Default; }
                int si = Array.IndexOf(Sizes, doc.W); if (si >= 0 && doc.W == doc.H) cmbSize.SelectedIndex = si;
                cmbBg.SelectedIndex = 0; canvas.Doc = doc; SyncMapCombo(); AfterDocChanged(); UpdatePartUi();
                stInfo.Text = "재질 세트: " + Path.GetFileName(d.FileName) + " | " + string.Join(" | ", notes) + "  (F7/F8/F9로 맵 전환)";
            }
        }
        void SaveMaterial()
        {
            if (doc == null) return;
            if (Liquifying) EndLiquify(true);
            if (!ResolveActive()) return;
            using (var d = new FolderBrowserDialog { Description = "재질 세트를 저장할 폴더 (같은 이름이 있으면 덮어씀)", SelectedPath = lastSaveDir })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var saved = new List<string>();
                for (int i = 0; i < 3; i++)
                {
                    if (doc.Layers[i] == null) continue;
                    var name = doc.LayerFiles[i] ?? (Doc.LayerNames[i] + ".png");
                    doc.Layers[i].Save(Path.Combine(d.SelectedPath, name), System.Drawing.Imaging.ImageFormat.Png); saved.Add(name);
                }
                doc.Dirty = false; lastSaveDir = d.SelectedPath; SaveSettings();
                stInfo.Text = "저장: " + string.Join(", ", saved) + " → " + d.SelectedPath;
            }
        }

        // ------------------------------------------------------------ 뷰 이미지 내보내기 / 불러오기
        void ExportViews()
        {
            if (doc == null) return;
            if (Liquifying) EndLiquify(true);
            if (!ResolveActive() || !EnsureBody()) return;
            using (var d = new FolderBrowserDialog { Description = "뷰 이미지를 저장할 폴더 (front/back/left/right.png 등이 만들어짐)", SelectedPath = lastViewDir })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (File.Exists(Path.Combine(d.SelectedPath, "front.png")) &&
                    MessageBox.Show(this, "이 폴더에 이미 뷰 이미지가 있습니다. 덮어쓸까요?", "KKSkinTool", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                Cursor = Cursors.WaitCursor;
                try { ViewExchange.Export(mesh, doc.ActiveBitmap, d.SelectedPath, v => { stInfo.Text = "내보내는 중: " + v; Application.DoEvents(); }); }
                catch (Exception ex) { MessageBox.Show(this, "내보내기 실패\n" + ex.Message); return; }
                finally { Cursor = Cursors.Default; }
                lastViewDir = d.SelectedPath; SaveSettings();
                stInfo.Text = "뷰 내보내기 완료: " + d.SelectedPath + "  (front/back/left/right.png를 칠한 뒤 [뷰 불러오기])";
                try { System.Diagnostics.Process.Start("explorer.exe", d.SelectedPath); } catch { }
            }
        }
        void ImportViews()
        {
            if (doc == null) return;
            if (Liquifying) EndLiquify(true);
            if (!ResolveActive() || !EnsureBody()) return;
            using (var d = new FolderBrowserDialog { Description = "칠한 뷰 이미지가 있는 폴더 (뷰 내보내기로 만든 폴더)", SelectedPath = lastViewDir })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var pj = ViewExchange.ReadProject(d.SelectedPath);
                if (pj == null) { MessageBox.Show(this, "project.txt가 없습니다. [뷰 내보내기]로 만든 폴더를 고르세요."); return; }
                string sz; pj.TryGetValue("size", out sz);
                if (sz != doc.W + "x" + doc.H) { MessageBox.Show(this, string.Format("내보낼 때 크기({0})와 지금 문서 크기({1}×{2})가 다릅니다.", sz, doc.W, doc.H)); return; }
                string mp; pj.TryGetValue("mesh", out mp); string ax; pj.TryGetValue("axis", out ax);
                if ((mp != null && !string.Equals(Path.GetFullPath(mp), Path.GetFullPath(mesh.Path), StringComparison.OrdinalIgnoreCase)) || (ax != null && ax != mesh.FrontAxis))
                    if (MessageBox.Show(this, "내보낼 때와 메쉬(또는 앞 방향)가 다릅니다. 그래도 진행할까요?", "KKSkinTool", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                Cursor = Cursors.WaitCursor;
                try
                {
                    var edits = ViewExchange.LoadEdits(d.SelectedPath, doc.W, doc.H, 6, m => { stInfo.Text = m; Application.DoEvents(); });
                    if (edits.Count == 0) { stInfo.Text = "바뀐 픽셀이 없습니다 (원본과 같음)"; return; }
                    int n = 0;
                    doc.ApplyLayer(doc.Active, px => n = mesh.BakeViews(edits, px));
                    lastViewDir = d.SelectedPath; SaveSettings();
                    RefreshView();
                    stInfo.Text = string.Format("뷰 불러오기: {0}개 뷰에서 텍셀 {1:N0}개 반영 (Ctrl+Z로 되돌리기)", edits.Count, n);
                }
                catch (Exception ex) { MessageBox.Show(this, "불러오기 실패\n" + ex.Message); }
                finally { Cursor = Cursors.Default; }
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
                // 지금 맵을 유동화하고, 재질 세트의 나머지 맵도 같은 변형을 받음 (위치가 어긋나지 않게)
                canvas.Liquify = new LiquifySession(doc.ActiveBitmap, doc.Layers.Where((l, i) => l != null && i != doc.Active).ToArray());
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
        void ToggleSelect(CanvasView.SelectTool t) { SetSelectTool(canvas.SelTool == t ? CanvasView.SelectTool.None : t); }
        void SetSelectTool(CanvasView.SelectTool t)
        {
            if (t != CanvasView.SelectTool.None)
            {
                if (Liquifying) EndLiquify(true);
                if (canvas.Part != null && canvas.Part.IsFloat) { stInfo.Text = "먼저 지금 선택 영역을 확정(Enter)하거나 취소(Esc)하세요"; t = CanvasView.SelectTool.None; }
                else if (canvas.Part == null && view != ViewKind.UV) { stInfo.Text = "결과 이미지 선택은 UV 보기(F2)에서 합니다"; t = CanvasView.SelectTool.None; }
            }
            canvas.SelTool = t; btnSelRect.Checked = t == CanvasView.SelectTool.Rect; btnSelLasso.Checked = t == CanvasView.SelectTool.Lasso;
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
            else if (doc.IsMaterial)
            {   // 재질 세트: 지금 맵을 떼고, 나머지 맵의 같은 자리도 함께 뗌. 빈 자리는 주변 원단으로 메움
                var backups = doc.Layers.Select(l => l == null ? null : (Bitmap)l.Clone()).ToArray();
                Point o = Point.Empty; Bitmap main = null; var extra = new Bitmap[3];
                for (int i = 0; i < 3; i++)
                {
                    if (doc.Layers[i] == null) continue;
                    byte[] mk; Rectangle bx; Point oi;
                    var piece = Pixels.Lift(doc.Layers[i], poly, Color.Transparent, out oi, out mk, out bx, true);
                    if (piece == null) continue;
                    if (i == doc.Active) { main = piece; o = oi; } else extra[i] = piece;
                }
                if (main == null) { foreach (var b in backups) if (b != null) b.Dispose(); foreach (var b in extra) if (b != null) b.Dispose(); return; }
                fl = new PartLayer(main, "선택 영역 (" + Doc.LayerNames[doc.Active] + " + 연결된 맵)", BlendKind.Normal)
                { Backup = (Bitmap)backups[doc.Active].Clone(), BackupLayers = backups, Extra = extra, FloatLayer = doc.Active };
                fl.S.Cx = o.X + main.Width / 2f; fl.S.Cy = o.Y + main.Height / 2f;
                parentHistory = null;
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
            using (var f = new BatchCombineForm(batchTop, batchBottom, batchOut, doc != null ? doc.W : DocW, doc != null ? doc.H : DocH))
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
                if (view == ViewKind.UV || p.IsFloat)
                {
                    doc.Commit(p);
                    if (!p.IsFloat && HasFx(p)) ApplyFx(MaterialGen.Coverage(p, doc.W, doc.H), p.S);     // 같은 실행 취소 단계 안에서
                }
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
            if (p.IsFloat) { if (p.BackupLayers != null) doc.RestoreFrom(p.BackupLayers); else doc.RestoreFrom(p.Backup); stInfo.Text = "선택 영역 취소"; }
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
            bool fx = on && doc != null && doc.IsMaterial && doc.Active == Doc.MAIN && !canvas.Part.IsFloat;
            foreach (Control c in new Control[] { cmbNFx, cmbDFx, numFx, lblFx }) c.Enabled = fx;
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
            cmbNFx.SelectedIndex = (int)p.S.NFx; cmbDFx.SelectedIndex = (int)p.S.DFx; numFx.Value = Clamp(numFx, p.S.FxStrength * 100);
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
            p.S.NFx = (NormalFx)Math.Max(0, cmbNFx.SelectedIndex); p.S.DFx = (DetailFx)Math.Max(0, cmbDFx.SelectedIndex); p.S.FxStrength = (float)numFx.Value / 100f;
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
                case Keys.F7: SetMap(Doc.MAIN); return true;
                case Keys.F8: SetMap(Doc.NORMAL); return true;
                case Keys.F9: SetMap(Doc.DETAIL); return true;
                case Keys.Control | Keys.D0: canvas.FitView(); return true;
                case Keys.Control | Keys.D1: canvas.SetZoom(1f, new Point(canvas.Width / 2, canvas.Height / 2)); return true;
                case Keys.Control | Keys.Oemplus: canvas.SetZoom(canvas.Zoom * 1.25f, new Point(canvas.Width / 2, canvas.Height / 2)); return true;
                case Keys.Control | Keys.OemMinus: canvas.SetZoom(canvas.Zoom / 1.25f, new Point(canvas.Width / 2, canvas.Height / 2)); return true;
                case Keys.Enter:
                    if (Liquifying) { EndLiquify(true); return true; }
                    if (canvas.Part != null) { CommitPart(); return true; } break;
                case Keys.Escape:
                    if (canvas.SelTool != CanvasView.SelectTool.None) { SetSelectTool(CanvasView.SelectTool.None); return true; }
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
