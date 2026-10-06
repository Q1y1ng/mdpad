using System;
using System.Drawing;
using System.Windows.Forms;

namespace MdPad
{
    /// <summary>
    /// 分栏边上的折叠按钮（把左边「原始文档」一栏收起来 / 放出来）。
    /// 自绘而非用 Win32 Button：实测 Button 在深色模式下会被系统主题接管，
    /// 即使设了 BackColor 也会画出亮黄色底（FlatStyle.Flat + UseVisualStyleBackColor 的坑）。
    /// </summary>
    internal sealed class CollapseButton : Control
    {
        private bool hover;
        private string glyph = "\uE76B";
        private Color back = Color.FromArgb(243, 243, 243);
        private Color fore = Color.FromArgb(32, 32, 32);
        private Color hoverOverlay = Color.FromArgb(0, 0, 0, 0);
        private readonly Font iconFont;

        public CollapseButton(string iconFontName)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            Cursor = Cursors.Hand;
            Width = 16;
            Dock = DockStyle.Left;
            iconFont = new Font(iconFontName, 8f);
        }

        public string Glyph
        {
            get { return glyph; }
            set { if (glyph != value) { glyph = value; Invalidate(); } }
        }

        public void SetTheme(Color bg, Color fg, Color hoverBgOverlay)
        {
            back = bg;
            fore = fg;
            hoverOverlay = hoverBgOverlay;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                using (SolidBrush b = new SolidBrush(hover ? Blend(back, hoverOverlay) : back))
                    e.Graphics.FillRectangle(b, ClientRectangle);
                TextRenderer.DrawText(e.Graphics, glyph, iconFont, ClientRectangle, fore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            catch { }
        }

        /// <summary>把带 alpha 的高亮色叠到底色上（ToolStrip 那种半透明悬停效果）</summary>
        private static Color Blend(Color baseColor, Color overlay)
        {
            if (overlay.A == 0) return baseColor;
            int a = overlay.A;
            return Color.FromArgb(
                (baseColor.R * (255 - a) + overlay.R * a) / 255,
                (baseColor.G * (255 - a) + overlay.G * a) / 255,
                (baseColor.B * (255 - a) + overlay.B * a) / 255);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && iconFont != null) iconFont.Dispose();
            base.Dispose(disposing);
        }
    }
}
