using System;
using System.Drawing;
using System.Windows.Forms;

namespace MdPad
{
    /// <summary>
    /// 带滚动通知的 TextBox —— WinForms 的 TextBox 没有 Scroll 事件，
    /// 只能拦窗口消息（滚动条 / 滚轮 / 翻页键 / 尺寸变化）来通知行号槽重绘。
    /// </summary>
    internal sealed class EditorBox : TextBox
    {
        public event EventHandler Scrolled;

        private const int WM_VSCROLL = 0x0115;
        private const int WM_HSCROLL = 0x0114;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SIZE = 0x0005;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            bool notify = false;
            if (m.Msg == WM_VSCROLL || m.Msg == WM_HSCROLL || m.Msg == WM_MOUSEWHEEL || m.Msg == WM_SIZE)
            {
                notify = true;
            }
            else if (m.Msg == WM_KEYDOWN)
            {
                int k = m.WParam.ToInt32();
                if (k == 33 || k == 34 || k == 35 || k == 36 || k == 38 || k == 40) notify = true;   // PgUp/PgDn/End/Home/↑/↓
            }
            if (notify && Scrolled != null) Scrolled(this, EventArgs.Empty);
        }
    }

    /// <summary>行号槽：跟随编辑区滚动，按逻辑行编号（自动换行折出来的行不另编号）</summary>
    internal sealed class GutterPanel : Control
    {
        private TextBox source;
        private Color gutterBack = Color.White;
        private Color numberFg = Color.Gray;
        private Color borderFg = Color.Gainsboro;
        private Font numberFont;

        public GutterPanel(TextBox textSource)
        {
            source = textSource;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Width = 46;
        }

        /// <summary>编辑框被重建时重新绑定（句柄丢失后的兜底路径）</summary>
        public void SetSource(TextBox textSource)
        {
            source = textSource;
            Invalidate();
        }

        public void SetTheme(Color back, Color number, Color border, Font font)
        {
            gutterBack = back;
            numberFg = number;
            borderFg = border;
            if (numberFont != null) numberFont.Dispose();
            numberFont = font;
            Width = MeasureWidth();
            Invalidate();
        }

        private int MeasureWidth()
        {
            // 注意：这里绝不能碰 source 的句柄相关 API（GetLineFromCharIndex/TextLength），
            // 否则会在窗体句柄创建之前强制 CreateHandle —— 实测会抛「创建窗口句柄时出错」。
            Font f = numberFont != null ? numberFont : Font;
            int w = TextRenderer.MeasureText("0000", f).Width + 18;
            return Math.Max(34, Math.Min(96, w));
        }

        private void EnsureWidth(int lines)
        {
            int digits = Math.Max(3, lines.ToString().Length);
            Font f = numberFont != null ? numberFont : Font;
            int w = TextRenderer.MeasureText(new string('0', digits), f).Width + 18;
            w = Math.Max(34, Math.Min(120, w));
            if (w != Width) Width = w;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                using (SolidBrush bg = new SolidBrush(gutterBack))
                    e.Graphics.FillRectangle(bg, ClientRectangle);
                using (Pen p = new Pen(borderFg))
                    e.Graphics.DrawLine(p, Width - 1, 0, Width - 1, Height);

                if (source == null || !source.IsHandleCreated || source.TextLength == 0) return;

                Font f = numberFont != null ? numberFont : Font;
                int total;
                int first = 0;
                try
                {
                    total = source.GetLineFromCharIndex(source.TextLength);
                    int topChar = source.GetCharIndexFromPosition(new Point(1, 1));
                    first = Math.Max(0, source.GetLineFromCharIndex(topChar));
                }
                catch { return; }

                int visibleH = source.ClientSize.Height;
                for (int i = first; i <= total; i++)
                {
                    try
                    {
                        int ci = source.GetFirstCharIndexFromLine(i);
                        if (ci < 0) break;
                        Point pos = source.GetPositionFromCharIndex(ci);
                        if (pos.Y > visibleH) break;
                        if (pos.Y < -40) continue;

                        string s = (i + 1).ToString();
                        Size sz = TextRenderer.MeasureText(e.Graphics, s, f);
                        TextRenderer.DrawText(e.Graphics, s, f,
                            new Point(Width - 8 - sz.Width, pos.Y), numberFg,
                            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    }
                    catch (Exception ex)
                    {
                        MdPad.MainForm.LogSafe("[gutter-paint line " + i + "] " + ex.ToString());
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                MdPad.MainForm.LogSafe("[gutter-paint] " + ex.ToString());
            }
        }
    }
}
