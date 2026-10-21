using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KKSkinTool
{
    /// <summary>베이스(와이어) + 결과 + 배치 중인 파트를 보여주고 마우스로 이동/크기/회전, 확대/축소/이동</summary>
    public class CanvasView : Control
    {
        public Doc Doc;
        public PartLayer Part;
        public Bitmap Wire;
        public bool ShowWire = true;
        public float WireOpacity = 0.6f;

        public float Zoom { get; private set; }
        PointF pan = new PointF(20, 20);

        public event Action PartChanged;     // 변환 값이 바뀜 (UI 동기화)
        public event Action BeginEdit;       // 드래그 시작 직전 (undo 기록)
        public event Action<PointF> MouseDoc; // 상태 표시줄 좌표
        public event Action ViewChanged;

        enum Mode { None, Pan, Move, Scale, Rotate }
        Mode mode = Mode.None;
        Point downScreen; PointF downDoc; PartState downState; PointF downPan;
        public bool SpaceDown;
        const int HandlePx = 7, RotHandlePx = 34;
        TextureBrush checker;

        public CanvasView()
        {
            Zoom = 0.4f;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.FromArgb(55, 55, 58);
            var cb = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(cb))
            {
                g.Clear(Color.FromArgb(205, 205, 205));
                g.FillRectangle(Brushes.White, 0, 0, 8, 8); g.FillRectangle(Brushes.White, 8, 8, 8, 8);
            }
            checker = new TextureBrush(cb);
        }

        // ------------------------------------------------------------ 좌표
        public PointF ToDoc(Point s) { return new PointF((s.X - pan.X) / Zoom, (s.Y - pan.Y) / Zoom); }
        public PointF ToScreen(PointF d) { return new PointF(d.X * Zoom + pan.X, d.Y * Zoom + pan.Y); }

        public void FitView()
        {
            if (Doc == null) return;
            Zoom = Math.Min((Width - 40f) / Doc.W, (Height - 40f) / Doc.H);
            pan = new PointF((Width - Doc.W * Zoom) / 2, (Height - Doc.H * Zoom) / 2);
            Invalidate(); if (ViewChanged != null) ViewChanged();
        }
        public void SetZoom(float z, Point anchor)
        {
            z = Math.Max(0.05f, Math.Min(16f, z));
            var d = ToDoc(anchor); Zoom = z;
            pan = new PointF(anchor.X - d.X * Zoom, anchor.Y - d.Y * Zoom);
            Invalidate(); if (ViewChanged != null) ViewChanged();
        }
        public PointF ViewCenterDoc() { return ToDoc(new Point(Width / 2, Height / 2)); }

        // ------------------------------------------------------------ 그리기
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            if (Doc == null) return;
            var docRect = new RectangleF(pan.X, pan.Y, Doc.W * Zoom, Doc.H * Zoom);
            if (Doc.TransparentBg) g.FillRectangle(checker, docRect);
            g.InterpolationMode = Zoom >= 1.5f ? InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(Doc.Result, docRect);
            if (Part != null)
            {
                var st = g.Save();
                g.TranslateTransform(pan.X, pan.Y); g.ScaleTransform(Zoom, Zoom);
                using (var m = Part.GetMatrix()) g.MultiplyTransform(m);
                using (var ia = PartLayer.OpacityAttr(Part.S.Opacity))
                    g.DrawImage(Part.Display, new Rectangle(0, 0, Part.W, Part.H), 0, 0, Part.W, Part.H, GraphicsUnit.Pixel, ia);
                g.Restore(st);
            }
            if (ShowWire && Wire != null)
                using (var ia = PartLayer.OpacityAttr(WireOpacity)) g.DrawImage(Wire, Rectangle.Round(docRect), 0, 0, Wire.Width, Wire.Height, GraphicsUnit.Pixel, ia);
            using (var pen = new Pen(Color.FromArgb(120, 0, 0, 0))) g.DrawRectangle(pen, docRect.X, docRect.Y, docRect.Width, docRect.Height);
            if (Part != null) DrawHandles(g);
        }

        void DrawHandles(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var c = Part.Corners(); var s = new PointF[4];
            for (int i = 0; i < 4; i++) s[i] = ToScreen(c[i]);
            using (var pen = new Pen(Color.FromArgb(0, 160, 255), 1.5f) { DashStyle = DashStyle.Dash })
                g.DrawPolygon(pen, s);
            PointF top, rot; RotHandle(out top, out rot);
            using (var pen = new Pen(Color.FromArgb(0, 160, 255), 1.5f)) g.DrawLine(pen, top, rot);
            foreach (var p in s)
            {
                g.FillRectangle(Brushes.White, p.X - HandlePx, p.Y - HandlePx, HandlePx * 2, HandlePx * 2);
                g.DrawRectangle(Pens.DodgerBlue, p.X - HandlePx, p.Y - HandlePx, HandlePx * 2, HandlePx * 2);
            }
            g.FillEllipse(Brushes.Orange, rot.X - HandlePx, rot.Y - HandlePx, HandlePx * 2, HandlePx * 2);
            g.DrawEllipse(Pens.DarkOrange, rot.X - HandlePx, rot.Y - HandlePx, HandlePx * 2, HandlePx * 2);
            var ctr = ToScreen(new PointF(Part.S.Cx, Part.S.Cy));
            g.DrawLine(Pens.DodgerBlue, ctr.X - 6, ctr.Y, ctr.X + 6, ctr.Y); g.DrawLine(Pens.DodgerBlue, ctr.X, ctr.Y - 6, ctr.X, ctr.Y + 6);
        }

        /// <summary>회전 핸들: 윗변 중앙에서 화면 기준 일정 거리 바깥</summary>
        void RotHandle(out PointF top, out PointF rot)
        {
            var c = Part.Corners();
            top = ToScreen(new PointF((c[0].X + c[1].X) / 2, (c[0].Y + c[1].Y) / 2));
            var ctr = ToScreen(new PointF(Part.S.Cx, Part.S.Cy));
            float dx = top.X - ctr.X, dy = top.Y - ctr.Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) { dx = 0; dy = -1; len = 1; }
            rot = new PointF(top.X + dx / len * RotHandlePx, top.Y + dy / len * RotHandlePx);
        }

        // ------------------------------------------------------------ 마우스
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            downScreen = e.Location; downDoc = ToDoc(e.Location); downPan = pan;
            if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right || (e.Button == MouseButtons.Left && SpaceDown))
            { mode = Mode.Pan; Cursor = Cursors.SizeAll; return; }
            if (e.Button != MouseButtons.Left || Part == null) return;
            mode = HitTest(e.Location);
            if (mode != Mode.None) { if (BeginEdit != null) BeginEdit(); downState = Part.S.Clone(); }
        }

        Mode HitTest(Point p)
        {
            PointF top, rot; RotHandle(out top, out rot);
            if (Dist(p, rot) <= HandlePx + 3) return Mode.Rotate;
            foreach (var c in Part.Corners()) if (Dist(p, ToScreen(c)) <= HandlePx + 3) return Mode.Scale;
            if (Part.Contains(ToDoc(p))) return Mode.Move;
            return Mode.None;
        }
        static float Dist(PointF a, PointF b) { float dx = a.X - b.X, dy = a.Y - b.Y; return (float)Math.Sqrt(dx * dx + dy * dy); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var d = ToDoc(e.Location);
            if (MouseDoc != null) MouseDoc(d);
            switch (mode)
            {
                case Mode.Pan:
                    pan = new PointF(downPan.X + e.X - downScreen.X, downPan.Y + e.Y - downScreen.Y); Invalidate(); return;
                case Mode.Move:
                    Part.S.Cx = downState.Cx + d.X - downDoc.X; Part.S.Cy = downState.Cy + d.Y - downDoc.Y; Changed(); return;
                case Mode.Scale:
                    {
                        float d0 = Dist(downDoc, new PointF(downState.Cx, downState.Cy)), d1 = Dist(d, new PointF(downState.Cx, downState.Cy));
                        if (d0 < 1) return;
                        float k = d1 / d0;
                        Part.S.ScaleX = Math.Max(0.01f, downState.ScaleX * k); Part.S.ScaleY = Math.Max(0.01f, downState.ScaleY * k); Changed(); return;
                    }
                case Mode.Rotate:
                    {
                        double a0 = Math.Atan2(downDoc.Y - downState.Cy, downDoc.X - downState.Cx), a1 = Math.Atan2(d.Y - downState.Cy, d.X - downState.Cx);
                        float r = downState.Rotation + (float)((a1 - a0) * 180 / Math.PI);
                        if ((ModifierKeys & Keys.Shift) != 0) r = (float)Math.Round(r / 15) * 15;      // Shift: 15° 단위
                        r = ((r % 360) + 540) % 360 - 180;
                        Part.S.Rotation = r; Changed(); return;
                    }
            }
            if (Part != null && !SpaceDown)
            {
                var h = HitTest(e.Location);
                Cursor = h == Mode.Rotate ? Cursors.Hand : h == Mode.Scale ? Cursors.SizeNWSE : h == Mode.Move ? Cursors.SizeAll : Cursors.Default;
            }
            else Cursor = SpaceDown ? Cursors.SizeAll : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (mode == Mode.Pan && ViewChanged != null) ViewChanged();
            mode = Mode.None; Cursor = Cursors.Default;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Part != null && (ModifierKeys & Keys.Alt) != 0)
            {   // Alt+휠: 파트 크기 / Alt+Shift+휠: 파트 회전
                if (BeginEdit != null) BeginEdit();
                if ((ModifierKeys & Keys.Shift) != 0) Part.S.Rotation += e.Delta > 0 ? 1 : -1;
                else { float k = e.Delta > 0 ? 1.02f : 1 / 1.02f; Part.S.ScaleX *= k; Part.S.ScaleY *= k; }
                Changed(); return;
            }
            SetZoom(Zoom * (e.Delta > 0 ? 1.2f : 1 / 1.2f), e.Location);
        }

        void Changed() { Invalidate(); if (PartChanged != null) PartChanged(); }
        public void Nudge(float dx, float dy)
        {
            if (Part == null) return;
            if (BeginEdit != null) BeginEdit();
            Part.S.Cx += dx; Part.S.Cy += dy; Changed();
        }
        protected override bool IsInputKey(Keys k)
        {
            var kc = k & Keys.KeyCode;
            return kc == Keys.Left || kc == Keys.Right || kc == Keys.Up || kc == Keys.Down || base.IsInputKey(k);
        }
    }
}
