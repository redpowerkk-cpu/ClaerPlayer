using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClaerPlayer
{
    internal static class Theme
    {
        public static readonly Color Back = Color.FromArgb(15, 18, 20);
        public static readonly Color Panel = Color.FromArgb(25, 29, 32);
        public static readonly Color Hover = Color.FromArgb(43, 49, 53);
        public static readonly Color Text = Color.FromArgb(237, 242, 240);
        public static readonly Color Muted = Color.FromArgb(166, 180, 177);
        public static readonly Color Accent = Color.FromArgb(139, 226, 194);
    }
    internal sealed class IconButton : Button
    {
        public string Glyph;
        public bool Active;
        bool hover;
        public IconButton(string glyph, string label)
        {
            Glyph = glyph; AccessibleName = label; AccessibleRole = AccessibleRole.PushButton;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = Theme.Panel; ForeColor = Theme.Text;
            Size = new Size(38, 38); Cursor = Cursors.Hand; TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(hover || Focused ? Theme.Hover : BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            Color color = !Enabled ? Color.FromArgb(85, 99, 96) : Active ? Theme.Accent : ForeColor;
            using (Pen p = new Pen(color, 1.8f)) using (Brush b = new SolidBrush(color))
            {
                g.TranslateTransform((Width - 24) / 2f, (Height - 24) / 2f);
                switch (Glyph)
                {
                    case "play": g.FillPolygon(b, new[] { new PointF(8, 4), new PointF(20, 12), new PointF(8, 20) }); break;
                    case "pause": g.FillRectangle(b, 7, 5, 3, 14); g.FillRectangle(b, 15, 5, 3, 14); break;
                    case "next": g.DrawLine(p, 19, 5, 19, 19); g.FillPolygon(b, new[] { new PointF(6, 5), new PointF(16, 12), new PointF(6, 19) }); break;
                    case "previous": g.DrawLine(p, 5, 5, 5, 19); g.FillPolygon(b, new[] { new PointF(18, 5), new PointF(8, 12), new PointF(18, 19) }); break;
                    case "volume": case "muted":
                        g.DrawPolygon(p, new[] { new Point(3, 9), new Point(7, 9), new Point(12, 5), new Point(12, 19), new Point(7, 15), new Point(3, 15) });
                        if (Glyph == "muted") { g.DrawLine(p, 17, 9, 22, 15); g.DrawLine(p, 17, 15, 22, 9); }
                        else { g.DrawArc(p, 11, 5, 10, 14, -65, 130); } break;
                    case "full": g.DrawLines(p, new[] { new Point(4, 9), new Point(4, 4), new Point(9, 4) }); g.DrawLines(p, new[] { new Point(15, 4), new Point(20, 4), new Point(20, 9) }); g.DrawLines(p, new[] { new Point(20, 15), new Point(20, 20), new Point(15, 20) }); g.DrawLines(p, new[] { new Point(9, 20), new Point(4, 20), new Point(4, 15) }); break;
                    case "list": for (int i = 0; i < 3; i++) { g.FillEllipse(b, 3, 5 + i * 6, 2, 2); g.DrawLine(p, 9, 6 + i * 6, 21, 6 + i * 6); } break;
                    case "loop": g.DrawArc(p, 4, 5, 16, 14, 200, 140); g.DrawArc(p, 4, 5, 16, 14, 20, 140); g.DrawLines(p, new[] { new Point(17, 4), new Point(21, 8), new Point(16, 9) }); g.DrawLines(p, new[] { new Point(7, 20), new Point(3, 16), new Point(8, 15) }); break;
                    case "menu": for (int i = 0; i < 3; i++) g.FillEllipse(b, 4 + i * 7, 11, 3, 3); break;
                }
                g.ResetTransform();
                if (Focused) using (Pen f = new Pen(Theme.Accent)) g.DrawRectangle(f, 2, 2, Width - 5, Height - 5);
            }
        }
    }
    internal sealed class SeekBar : Control
    {
        double value;
        public double Value { get { return value; } set { this.value = Math.Max(0, Math.Min(1, value)); Invalidate(); } }
        public bool Dragging;
        public event Action<double> Seek;
        public SeekBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            Height = 26; TabStop = true; AccessibleName = "재생 위치"; AccessibleRole = AccessibleRole.Slider; Cursor = Cursors.Hand;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int y = Height / 2; float x = 9 + (float)Value * Math.Max(1, Width - 18);
            using (Pen p = new Pen(Theme.Hover, 3)) e.Graphics.DrawLine(p, 9, y, Width - 9, y);
            using (Pen p = new Pen(Enabled ? Theme.Accent : Theme.Muted, 3)) e.Graphics.DrawLine(p, 9, y, x, y);
            if (Enabled) using (Brush b = new SolidBrush(Theme.Accent)) e.Graphics.FillEllipse(b, x - 5, y - 5, 10, 10);
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(1, 1, Width - 2, Height - 2), Theme.Accent, BackColor);
        }
        void UpdateValue(int x) { Value = (double)(x - 9) / Math.Max(1, Width - 18); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button != MouseButtons.Left) return; Focus(); Dragging = true; Capture = true; UpdateValue(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { if (Dragging) UpdateValue(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { if (!Dragging) return; UpdateValue(e.X); Dragging = false; Capture = false; if (Seek != null) Seek(Value); }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) { Value += e.KeyCode == Keys.Left ? -0.01 : 0.01; if (Seek != null) Seek(Value); e.Handled = true; } base.OnKeyDown(e); }
    }
    internal sealed class VideoPanel : Panel
    {
        protected override CreateParams CreateParams { get { CreateParams p = base.CreateParams; p.Style |= 0x02000000; return p; } }
    }
}
