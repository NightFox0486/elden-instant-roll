using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace EldenInstantRoll
{
    internal static class ThemeColors
    {
        internal static readonly Color Background = Color.FromArgb(24, 26, 31);      // #181A1F
        internal static readonly Color CardBg = Color.FromArgb(35, 38, 46);          // #23262E
        internal static readonly Color CardBorder = Color.FromArgb(50, 54, 66);      // #323642
        internal static readonly Color InputBg = Color.FromArgb(25, 28, 34);         // #191C22
        internal static readonly Color InputBorder = Color.FromArgb(62, 68, 82);     // #3E4452
        internal static readonly Color Divider = Color.FromArgb(48, 52, 63);         // #30343F

        internal static readonly Color TextPrimary = Color.FromArgb(243, 244, 246);  // #F3F4F6
        internal static readonly Color TextSecondary = Color.FromArgb(156, 163, 175);// #9CA3AF
        internal static readonly Color TextDisabled = Color.FromArgb(95, 102, 117);  // #5F6675

        internal static readonly Color Emerald = Color.FromArgb(16, 185, 129);       // #10B981
        internal static readonly Color EmeraldDark = Color.FromArgb(16, 62, 52);
        internal static readonly Color Gold = Color.FromArgb(212, 168, 67);          // #D4A843
        internal static readonly Color GoldDark = Color.FromArgb(64, 54, 30);
        internal static readonly Color Muted = Color.FromArgb(107, 114, 128);        // #6B7280
        internal static readonly Color MutedDark = Color.FromArgb(44, 48, 57);
        internal static readonly Color Danger = Color.FromArgb(239, 68, 68);         // #EF4444

        internal static readonly Color ButtonBg = Color.FromArgb(46, 51, 62);
        internal static readonly Color ButtonHover = Color.FromArgb(58, 64, 78);
        internal static readonly Color ButtonPressed = Color.FromArgb(38, 42, 52);
    }

    internal static class GraphicsHelper
    {
        internal static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            if (diameter > rect.Width) diameter = rect.Width;
            if (diameter > rect.Height) diameter = rect.Height;
            if (diameter <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            Rectangle arc = new Rectangle(rect.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class RoundedCardPanel : Panel
    {
        public int CornerRadius { get; set; }
        public Color BorderColor { get; set; }
        public Color FillColor { get; set; }

        public RoundedCardPanel()
        {
            CornerRadius = 10;
            BorderColor = ThemeColors.CardBorder;
            FillColor = ThemeColors.CardBg;
            BackColor = ThemeColors.Background;
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(Parent != null ? Parent.BackColor : ThemeColors.Background))
            {
                e.Graphics.FillRectangle(bg, ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = GraphicsHelper.CreateRoundedRectangle(rect, CornerRadius))
            using (SolidBrush fill = new SolidBrush(FillColor))
            using (Pen border = new Pen(BorderColor, 1f))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }
        }
    }

    internal sealed class StatusBadgeControl : Control
    {
        private Color _dotColor = ThemeColors.Muted;
        private Color _fillColor = ThemeColors.MutedDark;
        private Color _borderColor = ThemeColors.CardBorder;
        private Color _textColor = ThemeColors.TextPrimary;

        public StatusBadgeControl()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(134, 30);
            BackColor = ThemeColors.Background;
        }

        public void SetBadge(string text, Color dotColor, Color fillColor, Color borderColor, Color textColor)
        {
            Text = text;
            _dotColor = dotColor;
            _fillColor = fillColor;
            _borderColor = borderColor;
            _textColor = textColor;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(Parent != null ? Parent.BackColor : ThemeColors.Background))
            {
                e.Graphics.FillRectangle(bg, ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Math.Max(4, Height / 2);

            using (GraphicsPath path = GraphicsHelper.CreateRoundedRectangle(rect, radius))
            using (SolidBrush fill = new SolidBrush(_fillColor))
            using (Pen pen = new Pen(_borderColor, 1f))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }

            int dotSize = Math.Max(7, Height / 3);
            int dotX = 12;
            int dotY = (Height - dotSize) / 2;
            using (SolidBrush dotBrush = new SolidBrush(_dotColor))
            {
                e.Graphics.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);
            }

            Rectangle textRect = new Rectangle(dotX + dotSize + 7, 0, Width - (dotX + dotSize + 14), Height);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textRect,
                _textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class ToggleSwitch : Control
    {
        private bool _checked;
        private bool _hovered;
        private Color _onColor = ThemeColors.Emerald;

        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(48, 26);
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked != value)
                {
                    _checked = value;
                    Invalidate();
                    EventHandler handler = CheckedChanged;
                    if (handler != null)
                    {
                        handler(this, EventArgs.Empty);
                    }
                }
            }
        }

        public Color OnColor
        {
            get { return _onColor; }
            set
            {
                _onColor = value;
                Invalidate();
            }
        }

        public void SetCheckedSilent(bool value)
        {
            if (_checked != value)
            {
                _checked = value;
                Invalidate();
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (Enabled && e.Button == MouseButtons.Left)
            {
                Checked = !Checked;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
            {
                Checked = !Checked;
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(Parent != null ? Parent.BackColor : ThemeColors.CardBg))
            {
                e.Graphics.FillRectangle(bg, ClientRectangle);
            }

            Rectangle track = new Rectangle(1, 1, Width - 3, Height - 3);
            int radius = Math.Max(4, track.Height / 2);

            Color trackColor;
            Color thumbColor;
            if (!Enabled)
            {
                trackColor = Color.FromArgb(40, 44, 54);
                thumbColor = Color.FromArgb(95, 102, 117);
            }
            else if (_checked)
            {
                trackColor = _hovered
                    ? ControlPaint.Light(_onColor, 0.12f)
                    : _onColor;
                thumbColor = Color.White;
            }
            else
            {
                trackColor = _hovered
                    ? Color.FromArgb(72, 78, 94)
                    : Color.FromArgb(58, 63, 76);
                thumbColor = Color.FromArgb(220, 224, 232);
            }

            using (GraphicsPath path = GraphicsHelper.CreateRoundedRectangle(track, radius))
            using (SolidBrush trackBrush = new SolidBrush(trackColor))
            {
                e.Graphics.FillPath(trackBrush, path);
            }

            int pad = 3;
            int thumbSize = Math.Max(8, track.Height - pad * 2);
            int thumbX = _checked ? (track.Right - thumbSize - pad) : (track.Left + pad);
            int thumbY = track.Top + pad;

            using (SolidBrush thumbBrush = new SolidBrush(thumbColor))
            {
                e.Graphics.FillEllipse(thumbBrush, thumbX, thumbY, thumbSize, thumbSize);
            }
        }
    }

    internal sealed class ModernButton : Button
    {
        private bool _hovered;
        private bool _pressed;

        public Color NormalFill { get; set; }
        public Color HoverFill { get; set; }
        public Color PressedFill { get; set; }
        public Color OutlineColor { get; set; }
        public int CornerRadius { get; set; }

        public ModernButton()
        {
            NormalFill = ThemeColors.ButtonBg;
            HoverFill = ThemeColors.ButtonHover;
            PressedFill = ThemeColors.ButtonPressed;
            OutlineColor = ThemeColors.InputBorder;
            CornerRadius = 7;

            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            ForeColor = ThemeColors.TextPrimary;
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            if (mevent.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            _pressed = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(Parent != null ? Parent.BackColor : ThemeColors.CardBg))
            {
                e.Graphics.FillRectangle(bg, ClientRectangle);
            }

            Color fillColor = !Enabled
                ? Color.FromArgb(36, 40, 48)
                : (_pressed ? PressedFill : (_hovered ? HoverFill : NormalFill));
            Color textColor = Enabled ? ForeColor : ThemeColors.TextDisabled;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = GraphicsHelper.CreateRoundedRectangle(rect, CornerRadius))
            using (SolidBrush fill = new SolidBrush(fillColor))
            using (Pen border = new Pen(OutlineColor, 1f))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    internal static class IconFactory
    {
        internal static Icon CreateStatusIcon(EngineRunState state, int size)
        {
            Color ringColor;
            Color innerColor;

            if (state == EngineRunState.Active)
            {
                ringColor = ThemeColors.Emerald;
                innerColor = Color.FromArgb(16, 56, 46);
            }
            else if (state == EngineRunState.WaitingForGame)
            {
                ringColor = ThemeColors.Gold;
                innerColor = Color.FromArgb(56, 46, 22);
            }
            else
            {
                ringColor = ThemeColors.Muted;
                innerColor = Color.FromArgb(38, 42, 50);
            }

            using (Bitmap bmp = new Bitmap(size, size))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                float pad = size * 0.08f;
                float ringWidth = Math.Max(2.2f, size * 0.13f);
                RectangleF outer = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);

                using (SolidBrush fill = new SolidBrush(innerColor))
                {
                    g.FillEllipse(fill, outer);
                }

                // Elden-inspired double ring motif
                using (Pen outerPen = new Pen(ringColor, ringWidth))
                {
                    g.DrawEllipse(outerPen, outer);
                }

                float innerPad = size * 0.27f;
                RectangleF innerCircle = new RectangleF(innerPad, innerPad, size - innerPad * 2, size - innerPad * 2);
                using (Pen innerPen = new Pen(ringColor, Math.Max(1.4f, ringWidth * 0.6f)))
                {
                    g.DrawEllipse(innerPen, innerCircle);
                }

                // Vertical rune spear line
                float cx = size * 0.5f;
                using (Pen linePen = new Pen(ringColor, Math.Max(1.5f, ringWidth * 0.65f)))
                {
                    linePen.StartCap = LineCap.Round;
                    linePen.EndCap = LineCap.Round;
                    g.DrawLine(linePen, cx, size * 0.18f, cx, size * 0.82f);
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(hIcon))
                    {
                        return (Icon)temp.Clone();
                    }
                }
                finally
                {
                    NativeMethods.DestroyIcon(hIcon);
                }
            }
        }
    }
}
