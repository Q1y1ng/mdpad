using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MdPad
{
    /// <summary>
    /// 极简 Markdown → HTML 渲染器（无第三方依赖）。
    /// 覆盖：标题 / 粗体 / 斜体 / 删除线 / 行内代码 / 围栏代码块 / 引用 / 有序无序列表 /
    ///       任务列表 / 表格 / 分割线 / 链接 / 图片 / 裸链接自动识别 / 少量内联 HTML 白名单。
    /// </summary>
    internal static class MarkdownRenderer
    {
        private static readonly Regex ReAtx = new Regex(@"^[ ]{0,3}(#{1,6})[ \t]+(.*?)[ \t]*#*[ \t]*$", RegexOptions.Compiled);
        private static readonly Regex ReSetext = new Regex(@"^[ ]{0,3}(=+|-+)[ \t]*$", RegexOptions.Compiled);
        private static readonly Regex ReHr = new Regex(@"^[ ]{0,3}((\*[ \t]*){3,}|(-[ \t]*){3,}|(_[ \t]*){3,})$", RegexOptions.Compiled);
        private static readonly Regex ReFence = new Regex(@"^[ ]{0,3}(`{3,}|~{3,})[ \t]*([^\s`~]*)", RegexOptions.Compiled);
        private static readonly Regex ReUl = new Regex(@"^([ \t]*)([-*+])[ \t]+(.*)$", RegexOptions.Compiled);
        private static readonly Regex ReOl = new Regex(@"^([ \t]*)(\d{1,9})[.)][ \t]+(.*)$", RegexOptions.Compiled);
        private static readonly Regex ReTask = new Regex(@"^\[([ xX])\][ \t]*(.*)$", RegexOptions.Compiled);
        private static readonly Regex ReBareUrl = new Regex(@"https?://[^\s<>\u0001\u0002]+", RegexOptions.Compiled);

        private static readonly string[] HtmlWhitelist =
        {
            "br", "hr", "b", "strong", "i", "em", "u", "s", "del", "code", "kbd", "mark", "sub", "sup", "small", "span"
        };

        // ---------------------------------------------------------------- 公开入口

        public static string RenderPage(string markdown, string baseDir, bool hardBreak, bool dark, int fontPercent)
        {
            return WrapPage(RenderBody(markdown, baseDir, hardBreak), dark, fontPercent);
        }

        public static string RenderBody(string markdown, string baseDir, bool hardBreak)
        {
            if (markdown == null) markdown = string.Empty;
            string[] lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            StringBuilder sb = new StringBuilder();
            List<string> para = new List<string>();
            int i = 0;

            while (i < lines.Length)
            {
                string line = lines[i];

                // 围栏代码块
                Match fm = ReFence.Match(line);
                if (fm.Success)
                {
                    FlushParagraph(sb, para, baseDir, hardBreak);
                    string fence = fm.Groups[1].Value.Substring(0, 1);
                    int need = fm.Groups[1].Value.Length;
                    string lang = fm.Groups[2].Value;
                    i++;
                    StringBuilder code = new StringBuilder();
                    while (i < lines.Length)
                    {
                        Match cm = Regex.Match(lines[i], @"^[ ]{0,3}(" + Regex.Escape(fence) + @"{" + need + @",})[ \t]*$");
                        if (cm.Success) { i++; break; }
                        if (code.Length > 0) code.Append('\n');
                        code.Append(lines[i]);
                        i++;
                    }
                    sb.Append("<pre><code");
                    if (lang.Length > 0)
                        sb.Append(" class=\"language-").Append(Escape(lang)).Append('"');
                    sb.Append('>').Append(Escape(code.ToString())).Append("</code></pre>\n");
                    continue;
                }

                // 空行
                if (line.Trim().Length == 0)
                {
                    FlushParagraph(sb, para, baseDir, hardBreak);
                    i++;
                    continue;
                }

                // 分割线
                if (ReHr.IsMatch(line) && !IsListLine(line))
                {
                    FlushParagraph(sb, para, baseDir, hardBreak);
                    sb.Append("<hr />\n");
                    i++;
                    continue;
                }

                // ATX 标题
                Match hm = ReAtx.Match(line);
                if (hm.Success)
                {
                    FlushParagraph(sb, para, baseDir, hardBreak);
                    int lv = hm.Groups[1].Value.Length;
                    sb.Append("<h").Append(lv).Append('>')
                      .Append(Inline(hm.Groups[2].Value, baseDir)).Append("</h").Append(lv).Append(">\n");
                    i++;
                    continue;
                }

                // Setext 标题（下一行是 === 或 ---）
                if (i + 1 < lines.Length && ReSetext.IsMatch(lines[i + 1]) && line.Trim().Length > 0
                    && !IsListLine(line) && !ReSetext.IsMatch(line))
                {
                    FlushParagraph(sb, para, baseDir, hardBreak);
                    int lv2 = lines[i + 1].Trim().StartsWith("=") ? 1 : 2;
                    sb.Append("<h").Append(lv2).Append('>').Append(Inline(line.Trim(), baseDir))
                      .Append("</h").Append(lv2).Append(">\n");
                    i += 2;
                    continue;
                }

                // 引用
                if (line.TrimStart().StartsWith(">"))
                {
                    FlushParagraph(sb, para, baseDir, hardBreak);
                    StringBuilder quote = new StringBuilder();
                    while (i < lines.Length && (lines[i].TrimStart().StartsWith(">") || lines[i].Trim().Length == 0))
                    {
                        if (lines[i].Trim().Length == 0) break;
                        string q = lines[i].TrimStart();
                        q = q.Substring(1);
                        if (q.StartsWith(" ")) q = q.Substring(1);
                        quote.Append(q).Append('\n');
                        i++;
                    }
                    sb.Append("<blockquote>\n").Append(RenderBody(quote.ToString(), baseDir, hardBreak)).Append("</blockquote>\n");
                    continue;
                }

                // 表格
                if (line.IndexOf('|') >= 0 && i + 1 < lines.Length && IsTableSeparator(lines[i + 1]))
                {
                    FlushParagraph(sb, para, baseDir, hardBreak);
                    RenderTable(sb, lines, ref i, baseDir);
                    continue;
                }

                // 列表
                if (IsListLine(line))
                {
                    FlushParagraph(sb, para, baseDir, hardBreak);
                    RenderList(sb, lines, ref i, baseDir, hardBreak);
                    continue;
                }

                para.Add(line);
                i++;
            }

            FlushParagraph(sb, para, baseDir, hardBreak);
            return sb.ToString();
        }

        // ---------------------------------------------------------------- 块级处理

        private static void FlushParagraph(StringBuilder sb, List<string> para, string baseDir, bool hardBreak)
        {
            if (para.Count == 0) return;
            StringBuilder sb2 = new StringBuilder();
            for (int k = 0; k < para.Count; k++)
            {
                string t = para[k];
                bool br = hardBreak || t.EndsWith("  ") || t.EndsWith("\\");
                if (k > 0) sb2.Append(br ? "<br />\n" : "\n");
                sb2.Append(Inline(t.Trim(), baseDir));
            }
            sb.Append("<p>").Append(sb2.ToString()).Append("</p>\n");
            para.Clear();
        }

        private static bool IsListLine(string line)
        {
            if (ReHr.IsMatch(line)) return false;
            return ReUl.IsMatch(line) || ReOl.IsMatch(line);
        }

        private static int IndentOf(string line)
        {
            int n = 0;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == ' ') n++;
                else if (line[i] == '\t') n += 4;
                else break;
            }
            return n;
        }

        private static void RenderList(StringBuilder sb, string[] lines, ref int i, string baseDir, bool hardBreak)
        {
            int baseIndent = IndentOf(lines[i]);
            bool ordered = ReOl.IsMatch(lines[i]);
            sb.Append(ordered ? "<ol>\n" : "<ul>\n");

            while (i < lines.Length)
            {
                string line = lines[i];
                if (line.Trim().Length == 0)
                {
                    if (i + 1 < lines.Length && IsListLine(lines[i + 1]) && IndentOf(lines[i + 1]) >= baseIndent)
                    { i++; continue; }
                    break;
                }
                if (!IsListLine(line)) break;
                int ind = IndentOf(line);
                if (ind < baseIndent) break;
                if (ind >= baseIndent + 2) { RenderList(sb, lines, ref i, baseDir, hardBreak); continue; }

                Match m = ordered ? ReOl.Match(line) : ReUl.Match(line);
                string content = m.Groups[3].Value;
                bool task = false, done = false;
                Match tm = ReTask.Match(content);
                if (tm.Success)
                {
                    task = true;
                    done = tm.Groups[1].Value == "x" || tm.Groups[1].Value == "X";
                    content = tm.Groups[2].Value;
                }

                sb.Append("<li>");
                if (task)
                {
                    sb.Append("<input type=\"checkbox\" disabled=\"disabled\"");
                    if (done) sb.Append(" checked=\"checked\"");
                    sb.Append(" /> ");
                }
                sb.Append(Inline(content, baseDir));
                i++;

                // 该条目的后续行 / 子列表
                while (i < lines.Length)
                {
                    string nxt = lines[i];
                    if (nxt.Trim().Length == 0) break;
                    if (IsListLine(nxt) && IndentOf(nxt) >= baseIndent + 2) { RenderList(sb, lines, ref i, baseDir, hardBreak); continue; }
                    if (!IsListLine(nxt) && IndentOf(nxt) >= baseIndent + 2)
                    {
                        sb.Append(hardBreak ? "<br />\n" : " ").Append(Inline(nxt.Trim(), baseDir));
                        i++;
                        continue;
                    }
                    break;
                }
                sb.Append("</li>\n");
            }

            sb.Append(ordered ? "</ol>\n" : "</ul>\n");
        }

        private static bool IsTableSeparator(string line)
        {
            string s = line.Trim();
            if (s.Length < 3 || s.IndexOf('-') < 0 || s.IndexOf('|') < 0) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '|' && c != '-' && c != ':' && c != ' ' && c != '\t') return false;
            }
            return true;
        }

        private static void RenderTable(StringBuilder sb, string[] lines, ref int i, string baseDir)
        {
            string[] head = SplitRow(lines[i]);
            string[] sep = SplitRow(lines[i + 1]);
            i += 2;

            List<string> align = new List<string>();
            for (int c = 0; c < head.Length; c++)
            {
                string cell = c < sep.Length ? sep[c].Trim() : "";
                bool l = cell.StartsWith(":");
                bool r = cell.EndsWith(":");
                align.Add(l && r ? "center" : (r ? "right" : (l ? "left" : "")));
            }

            sb.Append("<table>\n<thead>\n<tr>");
            for (int c = 0; c < head.Length; c++)
                sb.Append("<th").Append(AlignAttr(align, c)).Append('>').Append(Inline(head[c].Trim(), baseDir)).Append("</th>");
            sb.Append("</tr>\n</thead>\n<tbody>\n");

            while (i < lines.Length && lines[i].Trim().Length > 0 && lines[i].IndexOf('|') >= 0)
            {
                string[] row = SplitRow(lines[i]);
                sb.Append("<tr>");
                for (int c = 0; c < head.Length; c++)
                {
                    string v = c < row.Length ? row[c].Trim() : "";
                    sb.Append("<td").Append(AlignAttr(align, c)).Append('>').Append(Inline(v, baseDir)).Append("</td>");
                }
                sb.Append("</tr>\n");
                i++;
            }
            sb.Append("</tbody>\n</table>\n");
        }

        private static string AlignAttr(List<string> align, int c)
        {
            if (c >= align.Count || align[c].Length == 0) return "";
            return " style=\"text-align:" + align[c] + "\"";
        }

        private static string[] SplitRow(string line)
        {
            string s = line.Trim();
            if (s.StartsWith("|")) s = s.Substring(1);
            if (s.EndsWith("|") && !s.EndsWith("\\|")) s = s.Substring(0, s.Length - 1);
            s = s.Replace("\\|", "\u0003");
            string[] parts = s.Split('|');
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Replace("\u0003", "|");
            return parts;
        }

        // ---------------------------------------------------------------- 行内处理

        private static string Inline(string s, string baseDir)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            StringBuilder sb = new StringBuilder();
            int i = 0;
            while (i < s.Length)
            {
                if (s[i] == '`')
                {
                    int run = 1;
                    while (i + run < s.Length && s[i + run] == '`') run++;
                    string ticks = new string('`', run);
                    int close = s.IndexOf(ticks, i + run, StringComparison.Ordinal);
                    if (close > 0)
                    {
                        string code = s.Substring(i + run, close - i - run);
                        sb.Append("<code>").Append(Escape(code.Trim())).Append("</code>");
                        i = close + run;
                        continue;
                    }
                }
                int next = s.IndexOf('`', i);
                string chunk = next < 0 ? s.Substring(i) : s.Substring(i, next - i);
                sb.Append(InlineNoCode(chunk, baseDir));
                i = next < 0 ? s.Length : next;
            }
            return sb.ToString();
        }

        private static string InlineNoCode(string t, string baseDir)
        {
            t = Escape(t);
            t = RestoreWhitelistedHtml(t);

            List<string> saved = new List<string>();
            // 图片 → 占位
            t = Regex.Replace(t, @"!\[([^\]]*)\]\([ \t]*([^)\s]+)(?:[ \t]+&quot;([^&]*)&quot;)?[ \t]*\)", delegate(Match m)
            {
                string alt = m.Groups[1].Value;
                string src = ResolveUrl(m.Groups[2].Value, baseDir, false);
                string title = m.Groups[3].Success ? " title=\"" + m.Groups[3].Value + "\"" : "";
                return Stash(saved, "<img src=\"" + src + "\" alt=\"" + alt + "\"" + title + " />");
            });
            // 链接 → 占位
            t = Regex.Replace(t, @"(?<!!)\[([^\]]*)\]\([ \t]*([^)\s]+)(?:[ \t]+&quot;([^&]*)&quot;)?[ \t]*\)", delegate(Match m)
            {
                string text = m.Groups[1].Value;
                string href = ResolveUrl(m.Groups[2].Value, baseDir, true);
                string title = m.Groups[3].Success ? " title=\"" + m.Groups[3].Value + "\"" : "";
                return Stash(saved, "<a href=\"" + href + "\"" + title + ">" + text + "</a>");
            });
            // 裸链接自动识别
            t = ReBareUrl.Replace(t, delegate(Match m)
            {
                string url = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', '\u3002', '\uFF0C', '\u3001', '\uFF1B', '\uFF09', ')');
                string tail = m.Value.Substring(url.Length);
                return Stash(saved, "<a href=\"" + url + "\">" + url + "</a>") + tail;
            });
            // 粗体 / 斜体 / 删除线
            t = Regex.Replace(t, @"\*\*(?=\S)(.+?)(?<=\S)\*\*", "<strong>$1</strong>");
            t = Regex.Replace(t, @"(?<![\w])__(?=\S)(.+?)(?<=\S)__(?![\w])", "<strong>$1</strong>");
            t = Regex.Replace(t, @"~~(?=\S)(.+?)(?<=\S)~~", "<del>$1</del>");
            t = Regex.Replace(t, @"(?<![\w*])\*(?=\S)([^*]+?)(?<=\S)\*(?![\w*])", "<em>$1</em>");
            t = Regex.Replace(t, @"(?<![\w_])_(?=\S)([^_]+?)(?<=\S)_(?![\w_])", "<em>$1</em>");
            // 行内 HTML 白名单里的 <br/> 之后要能换行
            t = t.Replace("<br/>", "<br />").Replace("<br>", "<br />");

            for (int k = 0; k < saved.Count; k++)
                t = t.Replace(Token(k), saved[k]);
            return t;
        }

        private static string Stash(List<string> bag, string html)
        {
            bag.Add(html);
            return Token(bag.Count - 1);
        }

        private static string Token(int i)
        {
            return "\u0001" + i.ToString() + "\u0002";
        }

        private static string RestoreWhitelistedHtml(string escaped)
        {
            for (int i = 0; i < HtmlWhitelist.Length; i++)
            {
                string tag = HtmlWhitelist[i];
                escaped = Regex.Replace(escaped, "&lt;" + tag + "&gt;", "<" + tag + ">", RegexOptions.IgnoreCase);
                escaped = Regex.Replace(escaped, "&lt;" + tag + "[ ]*/&gt;", "<" + tag + " />", RegexOptions.IgnoreCase);
                escaped = Regex.Replace(escaped, "&lt;/" + tag + "&gt;", "</" + tag + ">", RegexOptions.IgnoreCase);
            }
            return escaped;
        }

        // ---------------------------------------------------------------- 工具

        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            StringBuilder sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        public static string HtmlUnescape(string s)
        {
            return s.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"")
                    .Replace("&#39;", "'").Replace("&amp;", "&");
        }

        private static string ResolveUrl(string raw, string baseDir, bool isLink)
        {
            if (Regex.IsMatch(raw, @"^(?i)(https?|ftp|mailto|data|file):")) return raw;
            if (raw.StartsWith("#")) return raw;
            if (string.IsNullOrEmpty(baseDir)) return raw;
            try
            {
                string p = HtmlUnescape(raw);
                if (p.StartsWith("/")) p = p.Substring(1);
                p = Uri.UnescapeDataString(p);
                p = p.Replace('/', Path.DirectorySeparatorChar);
                string full = Path.GetFullPath(Path.Combine(baseDir, p));
                if (!File.Exists(full) && !Directory.Exists(full)) return raw;
                return new Uri(full).AbsoluteUri;
            }
            catch
            {
                return raw;
            }
        }

        /// <summary>预览正文字号（px）—— 与 CSS 里的基数保持一致，字号改动走页内 JS 用这个值</summary>
        public static double BaseFontPx(int fontPercent)
        {
            if (fontPercent < 60) fontPercent = 60;
            if (fontPercent > 220) fontPercent = 220;
            return 14.0 * fontPercent / 100.0;
        }

        /// <summary>
        /// 只跟配色有关的 CSS，单独成块（&lt;style id="md-theme"&gt;）：
        /// 换主题时用页内 JS 换掉这一块即可，不必整页重载 —— 大文档下这是唯一不卡的做法。
        /// </summary>
        public static string ThemeCss(bool dark)
        {
            return dark
                ? "body{background:#202020;color:#e8e8e8;}a{color:#6cb6ff;}code{background:#2b2b2b;border-color:#3a3a3a;}"
                  + "pre{background:#272727;border-color:#3a3a3a;}th,td{border-color:#3d3d3d;}th{background:#2b2b2b;}"
                  + "blockquote{color:#c0c0c0;border-left-color:#4a4a4a;}hr{border-top-color:#3d3d3d;}input[type=checkbox]{filter:invert(0.9);}"
                  + "h1{border-bottom-color:#3a3a3a;}h2{border-bottom-color:#333;}h5,h6{color:#aaa;}"
                  + ".welcome h1{color:#fff;}.welcome .muted{color:#a0a0a0;}.kbd{background:#2d2d2d;border-color:#444;color:#e8e8e8;}"
                : "body{background:#ffffff;color:#1b1b1b;}a{color:#0067c0;}code{background:#f5f5f5;border-color:#e8e8e8;}"
                  + "pre{background:#fafafa;border-color:#ebebeb;}th,td{border-color:#e3e3e3;}th{background:#f7f7f7;}"
                  + "blockquote{color:#5a5a5a;border-left-color:#e0e0e0;}hr{border-top-color:#e8e8e8;}"
                  + "h1{border-bottom-color:#eee;}h2{border-bottom-color:#f0f0f0;}h5,h6{color:#666;}"
                  + ".welcome h1{color:#111;}.welcome .muted{color:#767676;}.kbd{background:#fff;border-color:#dcdcdc;color:#1b1b1b;}";
        }

        public static string WrapPage(string body, bool dark, int fontPercent)
        {
            string fs = BaseFontPx(fontPercent).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

            StringBuilder sb = new StringBuilder();
            sb.Append("<!DOCTYPE html>\r\n<html><head><meta charset=\"utf-8\" />");
            sb.Append("<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\" />");
            sb.Append("<style id=\"md-base\">\r\n");
            sb.Append("html,body{margin:0;padding:0;} body{font-family:\"Segoe UI Variable Text\",\"Segoe UI\",\"Microsoft YaHei UI\",\"Noto Sans SC\",sans-serif;font-size:").Append(fs).Append("px;line-height:1.75;padding:22px 30px 90px 30px;}");
            sb.Append("#doc{max-width:900px;margin:0 auto;}");
            sb.Append("h1,h2,h3,h4,h5,h6{line-height:1.35;margin:1.2em 0 .6em;font-weight:600;}");
            sb.Append("h1{font-size:1.7em;border-bottom:1px solid;padding-bottom:.25em;}");
            sb.Append("h2{font-size:1.42em;border-bottom:1px solid;padding-bottom:.2em;}");
            sb.Append("h3{font-size:1.2em;} h4{font-size:1.06em;} h5,h6{font-size:1em;}");
            sb.Append("p{margin:.65em 0;}");
            sb.Append("code{font-family:\"Cascadia Mono\",Consolas,\"Courier New\",monospace;font-size:.92em;padding:1px 5px;border:1px solid;border-radius:4px;}");
            sb.Append("pre{font-family:\"Cascadia Mono\",Consolas,\"Courier New\",monospace;font-size:.9em;line-height:1.6;padding:12px 14px;border:1px solid;border-radius:8px;overflow-x:auto;}");
            sb.Append("pre code{background:none;border:none;padding:0;}");
            sb.Append("blockquote{margin:.9em 0;padding:.2em 0 .2em 14px;border-left:4px solid;}");
            sb.Append("ul,ol{padding-left:1.9em;margin:.6em 0;} li{margin:.22em 0;}");
            sb.Append("table{border-collapse:collapse;margin:.9em 0;font-size:.96em;}");
            sb.Append("th,td{border:1px solid;padding:6px 11px;} th{font-weight:600;}");
            sb.Append("img{max-width:100%;} hr{border:none;border-top:1px solid;margin:1.6em 0;}");
            sb.Append("a{text-decoration:none;} a:hover{text-decoration:underline;}");
            sb.Append("input[type=checkbox]{vertical-align:-1px;margin-right:4px;}");
            sb.Append(".welcome{padding-top:12px;}");
            sb.Append(".welcome h1{font-size:2.1em;font-weight:600;margin:0 0 4px 0;border:none;}");
            sb.Append(".welcome .muted{font-size:.94em;margin:0 0 26px 0;}");
            sb.Append(".welcome table{border:none;}");
            sb.Append(".welcome td{border:none;padding:5px 16px 5px 0;font-size:.94em;}");
            sb.Append(".kbd{display:inline-block;font-family:\"Segoe UI Variable Text\",\"Segoe UI\",sans-serif;font-size:.86em;line-height:1.5;padding:0 7px;border:1px solid;border-radius:5px;margin-right:2px;}");
            sb.Append("\r\n</style>\r\n<style id=\"md-theme\">");
            sb.Append(ThemeCss(dark));
            sb.Append("</style>\r\n<script type=\"text/javascript\">\r\n");
            sb.Append("function mdGetScroll(){return (document.documentElement.scrollTop||document.body.scrollTop||0);}\r\n");
            sb.Append("function mdSetContent(h,y){var d=document.getElementById('doc');if(!d)return;d.innerHTML=h;window.scrollTo(0,y||0);}\r\n");
            sb.Append("function mdScrollTo(y){window.scrollTo(0,y||0);}\r\n");
            // 字号 / 主题都只改 CSS，不重载页面（大文档重载一次要好几秒）
            sb.Append("function mdSetFontSize(px){document.body.style.fontSize=px+'px';}\r\n");
            sb.Append("function mdSetTheme(css){var s=document.getElementById('md-theme');if(s)s.textContent=css;}\r\n");
            sb.Append("</script>\r\n</head>\r\n<body><div id=\"doc\">").Append(body).Append("</div></body></html>");
            return sb.ToString();
        }
    }
}
