using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BreakReminder
{
    public enum Glyph { None, Pause, Play, Gear, Chart }

    /// <summary>Круглая кнопка с подсветкой при наведении.</summary>
    public class RoundButton : Control
    {
        Color fill = Ui.Green;
        Glyph glyph = Glyph.None;
        bool hover, pressed;

        public RoundButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Bg;
            ForeColor = Color.White;
        }

        public Color Fill
        {
            get { return fill; }
            set { if (fill != value) { fill = value; Invalidate(); } }
        }

        public Glyph Glyph
        {
            get { return glyph; }
            set { if (glyph != value) { glyph = value; Invalidate(); } }
        }

        bool Inside(Point p)
        {
            float cx = Width / 2f, cy = Height / 2f, r = Math.Min(Width, Height) / 2f;
            float dx = p.X - cx, dy = p.Y - cy;
            return dx * dx + dy * dy <= r * r;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool h = Enabled && Inside(e.Location);
            if (h != hover)
            {
                hover = h;
                Cursor = h ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Inside(e.Location)) { pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnClick(EventArgs e)
        {
            if (Enabled && Inside(PointToClient(Cursor.Position))) base.OnClick(e);
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { hover = false; Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(BackColor);

            float size = Math.Min(Width, Height) - 2;
            float halo = size * 0.07f;
            var outer = new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size);
            var inner = RectangleF.Inflate(outer, -halo, -halo);
            if (pressed) inner.Inflate(-1.5f, -1.5f);

            Color c = !Enabled ? Ui.Mix(fill, BackColor, 0.55f)
                    : pressed ? Ui.Darken(fill, 0.15f)
                    : hover ? Ui.Lighten(fill, 0.12f)
                    : fill;

            using (var hb = new SolidBrush(Color.FromArgb(hover ? 80 : 40, c))) g.FillEllipse(hb, outer);
            using (var gb = new LinearGradientBrush(inner, Ui.Lighten(c, 0.15f), Ui.Darken(c, 0.08f), 90f))
                g.FillEllipse(gb, inner);

            Color fg = Enabled ? ForeColor : Color.FromArgb(140, ForeColor);
            float cx = inner.X + inner.Width / 2f, cy = inner.Y + inner.Height / 2f;

            using (var fb = new SolidBrush(fg))
            {
                switch (glyph)
                {
                    case Glyph.Pause:
                    {
                        float bw = inner.Width * 0.11f, bh = inner.Height * 0.36f, gap = bw * 0.9f;
                        g.FillRectangle(fb, cx - gap / 2 - bw, cy - bh / 2, bw, bh);
                        g.FillRectangle(fb, cx + gap / 2, cy - bh / 2, bw, bh);
                        break;
                    }
                    case Glyph.Play:
                    {
                        float h = inner.Height * 0.38f, w = h * 0.87f, x = cx - w * 0.38f;
                        g.FillPolygon(fb, new[] { new PointF(x, cy - h / 2), new PointF(x + w, cy), new PointF(x, cy + h / 2) });
                        break;
                    }
                    case Glyph.Gear:
                    {
                        float r = inner.Width * 0.22f, tw = r * 0.55f, th = r * 0.5f;
                        for (int i = 0; i < 8; i++)
                        {
                            var st = g.Save();
                            g.TranslateTransform(cx, cy);
                            g.RotateTransform(i * 45f);
                            g.FillRectangle(fb, -tw / 2, -r - th * 0.75f, tw, th + r * 0.3f);
                            g.Restore(st);
                        }
                        g.FillEllipse(fb, cx - r, cy - r, 2 * r, 2 * r);
                        float hr = r * 0.45f;
                        using (var cb = new SolidBrush(c)) g.FillEllipse(cb, cx - hr, cy - hr, 2 * hr, 2 * hr);
                        break;
                    }
                    case Glyph.Chart:
                    {
                        float bw = inner.Width * 0.1f, gap = bw * 0.55f, baseY = cy + inner.Height * 0.2f;
                        float[] hs = { 0.22f, 0.4f, 0.3f };
                        float x = cx - (3 * bw + 2 * gap) / 2;
                        for (int i = 0; i < 3; i++)
                        {
                            float h = inner.Height * hs[i];
                            g.FillRectangle(fb, x + i * (bw + gap), baseY - h, bw, h);
                        }
                        break;
                    }
                    default:
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString(Text, Font, fb, inner, sf);
                        break;
                }
            }
        }
    }
}
