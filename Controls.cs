using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace WinActivationAssistant
{
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(243, 243, 243);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(226, 226, 226);
        public static readonly Color Text = Color.FromArgb(26, 26, 26);
        public static readonly Color SubText = Color.FromArgb(96, 96, 96);
        public static readonly Color Accent = Color.FromArgb(0, 103, 192);
        public static readonly Color AccentHover = Color.FromArgb(0, 92, 175);
        public static readonly Color AccentPressed = Color.FromArgb(0, 76, 148);
        public static readonly Color Good = Color.FromArgb(15, 123, 15);
        public static readonly Color GoodBg = Color.FromArgb(223, 246, 221);
        public static readonly Color Bad = Color.FromArgb(196, 43, 28);
        public static readonly Color BadBg = Color.FromArgb(253, 231, 233);
        public static readonly Color Warn = Color.FromArgb(157, 93, 0);
        public static readonly Color WarnBg = Color.FromArgb(255, 244, 206);
        public static readonly Color InfoBg = Color.FromArgb(232, 241, 251);

        public static Font F(float size, FontStyle st = FontStyle.Regular) => new Font("Segoe UI", size, st);
        public static Font Semi(float size)
        {
            try { var f = new Font("Segoe UI Semibold", size); if (f.Name == "Segoe UI Semibold") return f; } catch { }
            return new Font("Segoe UI", size, FontStyle.Bold);
        }
        static string iconFamily;
        public static Font Icons(float size)
        {
            if (iconFamily == null)
            {
                iconFamily = "Segoe MDL2 Assets";
                foreach (var ff in FontFamily.Families)
                    if (ff.Name == "Segoe Fluent Icons") { iconFamily = ff.Name; break; }
            }
            return new Font(iconFamily, size);
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            if (rad <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    static class Glyph
    {
        public const string Refresh = "\uE72C", Settings = "\uE713", Store = "\uE719", Delete = "\uE74D",
            Key = "\uE8D7", Server = "\uE968", Check = "\uE930", Error = "\uE783", Info = "\uE946",
            View = "\uE890", Hide = "\uED1A", Lock = "\uE72E", Shield = "\uEA18", Sync = "\uE895", Warning = "\uE7BA";
    }

    class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Theme.Round(r, 8))
            using (var b = new SolidBrush(Theme.Card))
            using (var pen = new Pen(Theme.Border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
        }
    }

    enum BtnStyle { Primary, Secondary, Toggle, Ghost }

    class ModernButton : Control
    {
        public BtnStyle Style = BtnStyle.Secondary;
        public string Icon = "";
        public Color Surface = Theme.Card;
        public bool LeftAlign;
        bool selected, hover, down;
        public bool Selected { get => selected; set { selected = value; Invalidate(); } }

        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            Cursor = Cursors.Hand;
            Font = Theme.F(9.75f);
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override bool IsInputKey(Keys k) => k == Keys.Enter || k == Keys.Space || base.IsInputKey(k);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && Enabled) { e.Handled = true; OnClick(EventArgs.Empty); }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color fill, border, fore;
            switch (Style)
            {
                case BtnStyle.Primary:
                    fill = !Enabled ? Color.FromArgb(204, 204, 204) : down ? Theme.AccentPressed : hover ? Theme.AccentHover : Theme.Accent;
                    border = fill; fore = Color.White; break;
                case BtnStyle.Toggle:
                    if (selected) { fill = Theme.Accent; border = Theme.Accent; fore = Color.White; }
                    else { fill = down ? Color.FromArgb(232, 232, 232) : hover ? Color.FromArgb(246, 246, 246) : Color.White; border = Theme.Border; fore = Theme.Text; }
                    break;
                case BtnStyle.Ghost:
                    fill = down ? Color.FromArgb(228, 228, 228) : hover ? Color.FromArgb(240, 240, 240) : Surface;
                    border = fill; fore = Enabled ? Theme.Text : Color.FromArgb(160, 160, 160); break;
                default:
                    fill = !Enabled ? Color.FromArgb(245, 245, 245) : down ? Color.FromArgb(234, 234, 234) : hover ? Color.FromArgb(244, 244, 244) : Color.FromArgb(251, 251, 251);
                    border = Color.FromArgb(214, 214, 214); fore = Enabled ? Theme.Text : Color.FromArgb(160, 160, 160); break;
            }

            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Theme.Round(r, 5))
            using (var b = new SolidBrush(fill))
            using (var pen = new Pen(border))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
            if (Focused && ShowFocusCues)
            {
                var fr = new RectangleF(2.5f, 2.5f, Width - 5.5f, Height - 5.5f);
                using (var p = Theme.Round(fr, 4))
                using (var pen = new Pen(Style == BtnStyle.Primary || selected ? Color.White : Theme.Accent, 1.5f))
                    g.DrawPath(pen, p);
            }

            var tf = TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            using (var iconFont = Theme.Icons(Font.Size + 1))
            {
                Size iconSize = string.IsNullOrEmpty(Icon) ? Size.Empty : TextRenderer.MeasureText(g, Icon, iconFont, Size.Empty, TextFormatFlags.NoPadding);
                Size textSize = string.IsNullOrEmpty(Text) ? Size.Empty : TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                int gap = (iconSize.Width > 0 && textSize.Width > 0) ? 8 : 0;
                int total = iconSize.Width + gap + textSize.Width;
                int x = LeftAlign ? 12 : Math.Max(8, (Width - total) / 2);
                if (iconSize.Width > 0)
                {
                    TextRenderer.DrawText(g, Icon, iconFont, new Rectangle(x, 0, iconSize.Width + 2, Height), Style == BtnStyle.Primary || selected ? fore : (Enabled ? Theme.Accent : fore), fill, tf);
                    x += iconSize.Width + gap;
                }
                if (textSize.Width > 0)
                    TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x - 8, Height), fore, fill, tf);
            }
        }
    }

    /// TextBox that paints a grey example hint while empty (the hint is never real input).
    class HintTextBox : TextBox
    {
        string hint = "";
        public string Hint { get => hint; set { hint = value ?? ""; Invalidate(); } }
        public HintTextBox() { TextChanged += (s, e) => Invalidate(); }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x000F && TextLength == 0 && hint.Length > 0)
                using (var g = CreateGraphics())
                    TextRenderer.DrawText(g, hint, Font, ClientRectangle, Color.FromArgb(150, 150, 150), BackColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }

    /// Rounded text input with optional show/hide toggle. The text only ever lives in this control's memory.
    class InputBox : Panel
    {
        public readonly HintTextBox Box = new HintTextBox();
        public readonly ModernButton Eye;
        bool focused;

        public InputBox(bool masked)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Color.White;
            Box.ForeColor = Theme.Text;
            Box.Font = new Font("Consolas", 12f);
            Box.UseSystemPasswordChar = masked;
            Box.ShortcutsEnabled = true;
            Box.GotFocus += (s, e) => { focused = true; Invalidate(); };
            Box.LostFocus += (s, e) => { focused = false; Invalidate(); };
            Controls.Add(Box);

            Eye = new ModernButton { Style = BtnStyle.Ghost, Icon = masked ? Glyph.View : Glyph.Hide, Size = new Size(32, 28), TabStop = false };
            Eye.Click += (s, e) =>
            {
                Box.UseSystemPasswordChar = !Box.UseSystemPasswordChar;
                Eye.Icon = Box.UseSystemPasswordChar ? Glyph.View : Glyph.Hide;
                Eye.Invalidate();
                Box.Focus();
                Box.SelectionStart = Box.TextLength;
            };
            Controls.Add(Eye);
            Click += (s, e) => Box.Focus();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Eye == null) return;
            Eye.Height = Height - 8;
            Eye.Width = Eye.Height + 4;
            Eye.Location = new Point(Width - Eye.Width - 4, 4);
            Box.Location = new Point(12, (Height - Box.Height) / 2);
            Box.Width = Eye.Left - 16;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Theme.Round(r, 5))
            using (var b = new SolidBrush(Color.White))
            using (var pen = new Pen(focused ? Theme.Accent : Color.FromArgb(200, 200, 200), focused ? 1.5f : 1f))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
            if (focused)
                using (var pen = new Pen(Theme.Accent, 2f))
                    g.DrawLine(pen, 5, Height - 1.5f, Width - 6, Height - 1.5f);
        }

        /// Overwrites and clears the text box so the secret doesn't linger in the control.
        public void Wipe()
        {
            int n = Box.TextLength;
            if (n > 0) Box.Text = new string('\0', n);
            Box.Clear();
            Box.ClearUndo();
        }
    }

    enum BannerKind { Info, Good, Bad, Warn, Busy }

    class Banner : Control
    {
        BannerKind kind = BannerKind.Info;
        string title = "", subtitle = "";
        readonly Timer spin = new Timer { Interval = 40 };
        float angle;

        public Banner()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            spin.Tick += (s, e) => { angle = (angle + 12) % 360; Invalidate(); };
        }

        public void Set(BannerKind k, string t, string sub)
        {
            kind = k; title = t ?? ""; subtitle = sub ?? "";
            spin.Enabled = k == BannerKind.Busy;
            Invalidate();
        }

        protected override void Dispose(bool disposing) { if (disposing) spin.Dispose(); base.Dispose(disposing); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Color bg, fg; string icon;
            switch (kind)
            {
                case BannerKind.Good: bg = Theme.GoodBg; fg = Theme.Good; icon = Glyph.Check; break;
                case BannerKind.Bad: bg = Theme.BadBg; fg = Theme.Bad; icon = Glyph.Error; break;
                case BannerKind.Warn: bg = Theme.WarnBg; fg = Theme.Warn; icon = Glyph.Warning; break;
                case BannerKind.Busy: bg = Theme.InfoBg; fg = Theme.Accent; icon = null; break;
                default: bg = Theme.InfoBg; fg = Theme.Accent; icon = Glyph.Info; break;
            }
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Theme.Round(r, 6))
            using (var b = new SolidBrush(bg))
                g.FillPath(b, p);

            int iconBox = 28;
            var ir = new Rectangle(14, (Height - iconBox) / 2, iconBox, iconBox);
            if (icon == null)
            {
                using (var pen = new Pen(fg, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, new Rectangle(ir.X + 3, ir.Y + 3, ir.Width - 6, ir.Height - 6), angle, 270);
            }
            else
            {
                using (var f = Theme.Icons(16f))
                    TextRenderer.DrawText(g, icon, f, ir, fg, bg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            int x = ir.Right + 12;
            using (var tfnt = Theme.Semi(11.5f))
            using (var sfnt = Theme.F(9f))
            {
                int th = TextRenderer.MeasureText(g, "Ag", tfnt).Height;
                int sh = string.IsNullOrEmpty(subtitle) ? 0 : TextRenderer.MeasureText(g, "Ag", sfnt).Height;
                int y = (Height - th - sh) / 2;
                TextRenderer.DrawText(g, title, tfnt, new Rectangle(x, y, Width - x - 10, th), fg, bg, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                if (sh > 0)
                    TextRenderer.DrawText(g, subtitle, sfnt, new Rectangle(x, y + th, Width - x - 10, sh), Theme.Text, bg, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }
        }
    }
}
