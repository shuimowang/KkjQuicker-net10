using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brush = System.Drawing.Brush;
using Color = System.Drawing.Color;
using DashStyle = System.Drawing.Drawing2D.DashStyle;
using FontStyleDrawing = System.Drawing.FontStyle;
using Pen = System.Drawing.Pen;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace KkjQuicker.Utilities.Imaging
{
    /// <summary>
    /// 文本贴图渲染参数。所有尺寸单位均为 DIP(设备无关像素),最终由渲染管线按 DPI 换算为物理像素。
    /// </summary>
    public sealed class TextStickerOptions
    {
        public TextStickerOptions()
        {
            FontFamily = "Microsoft YaHei UI";
            FontSize = 14;
            FontWeight = FontWeights.Normal;
            FontStyle = FontStyles.Normal;
            TextAlignment = TextAlignment.Left;
            LetterSpacingScale = 0.96;
            Padding = new Thickness(14, 10, 14, 10);
            CornerRadius = new CornerRadius(10);

            Foreground = System.Windows.Media.Brushes.White;
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(215, 16, 16, 24));
            BorderThickness = 1;
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255));

            Dpi = 96;
            DropShadow = false;
            ShadowRadius = 14;
            ShadowOffsetX = 0;
            ShadowOffsetY = 6;
            ShadowColor = System.Windows.Media.Color.FromArgb(110, 0, 0, 0);

            RedrawCount = 2;
            RenderScale = 2.0;
            MaxTextWidthDip = 0;
            LineHeightMultiplier = 1.2;
        }

        // ── 字体 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 字体族名称。
        /// 推荐值:"Microsoft YaHei UI"(中文)、"Segoe UI"(纯英文场景)。
        /// 指定不存在的字体时 GDI+ 会静默回退到系统默认字体。
        /// </summary>
        public string FontFamily { get; set; }

        /// <summary>
        /// 字号,单位 DIP。推荐范围 12–18;默认 14 在 100% DPI 下约等于 10.5pt。
        /// </summary>
        public double FontSize { get; set; }

        /// <summary>
        /// 字重。<see cref="FontWeights.SemiBold"/> 及以上会映射为 GDI+ Bold。
        /// 推荐值:<see cref="FontWeights.Normal"/> 或 <see cref="FontWeights.Medium"/>;
        /// 需要加粗时用 <see cref="FontWeights.Bold"/>,不建议超过 Bold(GDI+ 无更细粒度支持)。
        /// </summary>
        public FontWeight FontWeight { get; set; }

        /// <summary>
        /// 字形。<see cref="FontStyles.Italic"/> 和 <see cref="FontStyles.Oblique"/> 均映射为 GDI+ Italic。
        /// 推荐值:<see cref="FontStyles.Normal"/>。
        /// </summary>
        public System.Windows.FontStyle FontStyle { get; set; }

        // ── 排版 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 文本对齐方式。<see cref="TextAlignment.Justify"/> 与 Left 行为相同(逐字符布局,不支持撑满对齐)。
        /// 推荐值:<see cref="TextAlignment.Left"/>。
        /// </summary>
        public TextAlignment TextAlignment { get; set; }

        /// <summary>
        /// 字间距缩放系数,作用于每个字形的水平步进(advance)。
        /// 1.0 = 原始步进;小于 1.0 收紧,大于 1.0 放宽。推荐范围 0.92–1.05;默认 0.96 视觉上略紧凑。
        /// </summary>
        public double LetterSpacingScale { get; set; }

        /// <summary>
        /// 行高倍数,作用于字体基准行高(font.GetHeight)。
        /// 推荐值:单行 1.0–1.2,多行 1.3–1.4(过小行间无间隙,过大显稀疏);默认 1.2。
        /// </summary>
        public double LineHeightMultiplier { get; set; }

        /// <summary>
        /// 文本内容区最大宽度(DIP)。超出此宽度自动换行。
        /// 0 表示不限制宽度(内部回退为当前屏幕工作区宽度)。
        /// 贴屏场景推荐设为工作区宽度的 35%–55%,例如 1920×1080 下约 380–550。
        /// </summary>
        public double MaxTextWidthDip { get; set; }

        // ── 背景框 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 文字到背景框四条边的内边距(DIP)。顺序:左、上、右、下。
        /// 推荐值:<c>new Thickness(14, 10, 14, 10)</c>;
        /// 左右可适当大于上下以留出视觉呼吸感。
        /// </summary>
        public Thickness Padding { get; set; }

        /// <summary>
        /// 背景框圆角半径(DIP)。仅使用 <see cref="CornerRadius.TopLeft"/> 作为四角统一值,其余方向忽略。
        /// 推荐范围:6–14;默认 10,与常见系统 Toast 风格一致。
        /// </summary>
        public CornerRadius CornerRadius { get; set; }

        /// <summary>
        /// 文字颜色。推荐值:<see cref="System.Windows.Media.Brushes.White"/>;
        /// 深色背景下白色可读性最佳,无需考虑壁纸色彩干扰。
        /// 仅支持 <see cref="SolidColorBrush"/>,其他类型回退到白色。
        /// </summary>
        public System.Windows.Media.Brush Foreground { get; set; }

        /// <summary>
        /// 背景框填充色。推荐使用带 Alpha 的深色,Alpha 建议范围 180–230(半透明保留桌面层次感)。
        /// 默认 <c>rgba(16,16,24,215)</c>,近黑色微带蓝调。
        /// 仅支持 <see cref="SolidColorBrush"/>,其他类型回退到默认色。
        /// </summary>
        public System.Windows.Media.Brush Background { get; set; }

        /// <summary>
        /// 边框宽度(DIP)。0 表示无边框。
        /// 推荐值:0.5–1.5;默认 1,配合低不透明度浅色边框可增加玻璃质感。
        /// 注意:绘制时实际 Pen 宽度为此值的 2 倍(路径描边居中,一半在框外会被裁剪)。
        /// </summary>
        public double BorderThickness { get; set; }

        /// <summary>
        /// 边框颜色。推荐使用低 Alpha 白色,使边缘在深色背景上若隐若现。
        /// 默认 <c>rgba(255,255,255,40)</c>,Alpha 过高会使边框喧宾夺主。
        /// 仅支持 <see cref="SolidColorBrush"/>,其他类型回退到默认色。
        /// </summary>
        public System.Windows.Media.Brush BorderBrush { get; set; }

        // ── 阴影 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 是否启用投影。贴屏场景强烈建议开启:背景色不可控,阴影是在浅色桌面上保持可读性的唯一手段。
        /// </summary>
        public bool DropShadow { get; set; }

        /// <summary>
        /// 阴影模糊半径(DIP)。值越大扩散越广,但渲染开销随之增加。
        /// 推荐范围:8–20;默认 14。超过 20 时视觉收益递减。
        /// </summary>
        public double ShadowRadius { get; set; }

        /// <summary>
        /// 阴影水平偏移(DIP)。正值向右,负值向左。
        /// 推荐值:0(垂直居中投影更自然,无方向感)。
        /// </summary>
        public double ShadowOffsetX { get; set; }

        /// <summary>
        /// 阴影垂直偏移(DIP)。正值向下,负值向上。
        /// 推荐值:4–8;默认 6,模拟光源在上方的自然阴影。
        /// </summary>
        public double ShadowOffsetY { get; set; }

        /// <summary>
        /// 阴影颜色,含透明度。Alpha 建议范围 80–130(过深显突兀,过浅起不到隔离作用)。
        /// 推荐值:<c>Color.FromArgb(100, 0, 0, 0)</c>,纯黑半透明。
        /// </summary>
        public System.Windows.Media.Color ShadowColor { get; set; }

        // ── 渲染质量 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 基准 DPI。仅在 <see cref="TextStickerRenderService.Render"/> 的 <c>relativeTo</c> 参数为 null 时生效;
        /// 传入有效 Visual 后此值被忽略,以屏幕实际 DPI 为准。
        /// 推荐做法:始终传 <c>relativeTo</c>,无需手动设置此值。
        /// </summary>
        public double Dpi { get; set; }

        /// <summary>
        /// 内部渲染分辨率倍数。最终位图像素数 = 逻辑尺寸 × DPI缩放 × RenderScale。
        /// 推荐值:2.0(在 100% 和 150% DPI 下均可获得清晰文字,4K 屏可考虑 1.5 以减小内存占用)。
        /// 设为 1.0 可提升性能,但 100% DPI 屏上文字边缘会出现锯齿。
        /// </summary>
        public double RenderScale { get; set; }

        /// <summary>
        /// 每个字形的重复绘制次数,用于在 GDI+ 下模拟亚像素级加粗以提升文字视觉密度。
        /// 推荐值:2(轻微增强,几乎无性能影响);设为 1 等同于关闭;超过 3 收益极小但耗时线性增长。
        /// </summary>
        public int RedrawCount { get; set; }
    }

    public static class TextStickerRenderService
    {
        #region 内部模型

        private sealed class GlyphLayout
        {
            public string Text;
            public RectangleF Rect;
            public int LineIndex;
        }

        private sealed class LayoutResult
        {
            public readonly List<GlyphLayout> Glyphs = new List<GlyphLayout>();
            public RectangleF BackgroundRect;
            public int BitmapWidth;
            public int BitmapHeight;
        }

        #endregion

        #region 标点查表

        private static readonly HashSet<char> _hangChars = new HashSet<char>(
            ("~～·`｀！!@＠#＃￥$＄%％…^＾&＆*＊)）—＿-－+＋=＝}｝】]］|｜、\\；;：:" +
             "\u201D\u2019,，》>＞。.?？0０1１2２3３4４5５6６7７8８9９×÷　 ").ToCharArray());

        private static readonly HashSet<char> _wrapBeforeChars = new HashSet<char>(
            ("(（【[［{｛\u201C\"＂\u2018\u2019＇《<＜").ToCharArray());

        #endregion

        #region 公开入口

        /// <summary>
        /// 将文本渲染为贴图 <see cref="BitmapSource"/>。
        /// </summary>
        /// <param name="text">要渲染的文本,不能为 null。支持 \n 换行。</param>
        /// <param name="opt">渲染参数。为 null 时使用默认参数。</param>
        /// <param name="relativeTo">
        /// 用于解析目标屏幕 DPI 与工作区的 Visual。推荐传入最终显示贴图的宿主窗口;
        /// 为 null 时回退到 <see cref="TextStickerOptions.Dpi"/>,再回退到鼠标所在屏的系统 DPI。
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> 为 null 时抛出。</exception>
        public static BitmapSource Render(string text, TextStickerOptions opt = null, Visual relativeTo = null)
        {
            if (text == null) throw new ArgumentNullException("text");

            using (Bitmap bitmap = RenderGdiBitmap(text, opt ?? new TextStickerOptions(), relativeTo))
            {
                return ImageHelper.ToBitmapSource(bitmap);
            }
        }

        /// <summary>
        /// 将文本渲染为贴图并编码为 PNG 字节数组。
        /// </summary>
        /// <param name="text">要渲染的文本,不能为 null。支持 \n 换行。</param>
        /// <param name="opt">渲染参数。为 null 时使用默认参数。</param>
        /// <param name="relativeTo">
        /// 用于解析目标屏幕 DPI 与工作区的 Visual。推荐传入最终显示贴图的宿主窗口;
        /// 为 null 时回退到 <see cref="TextStickerOptions.Dpi"/>,再回退到鼠标所在屏的系统 DPI。
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> 为 null 时抛出。</exception>
        public static byte[] RenderToPng(string text, TextStickerOptions opt = null, Visual relativeTo = null)
        {
            if (text == null) throw new ArgumentNullException("text");

            using (Bitmap bitmap = RenderGdiBitmap(text, opt ?? new TextStickerOptions(), relativeTo))
            using (MemoryStream ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// 根据图像逻辑尺寸与工作区计算一个"贴屏不超出 <paramref name="maxRatio"/> 占比"的安全缩放系数。
        /// 结果恒在 (0, 1] 范围内,不会放大。
        /// </summary>
        /// <param name="imgWidthDip">图像逻辑宽度(DIP)。</param>
        /// <param name="imgHeightDip">图像逻辑高度(DIP)。</param>
        /// <param name="workAreaDip">工作区(DIP)。</param>
        /// <param name="maxRatio">最大占工作区的比例,必须大于 0;非正值按不缩放处理,返回 1.0。</param>
        public static double CalculateSafeScale(
            double imgWidthDip, double imgHeightDip, Rect workAreaDip, double maxRatio = 0.6)
        {
            if (imgWidthDip <= 0 || imgHeightDip <= 0 || workAreaDip.IsEmpty)
                return 1.0;

            // Fix #3: maxRatio 非正值会使 scale<=0 被 scale<1.0 条件放行返回,先拦截
            if (maxRatio <= 0)
                return 1.0;

            double scale = Math.Min(
                workAreaDip.Width * maxRatio / imgWidthDip,
                workAreaDip.Height * maxRatio / imgHeightDip);

            return scale < 1.0 ? scale : 1.0;
        }

        #endregion

        #region 主流程

        // Fix #4: 透传 relativeTo,使 MeasureLayout 能取到正确屏幕的 work area
        private static Bitmap RenderGdiBitmap(string text, TextStickerOptions options, Visual relativeTo)
        {
            string normalized = NormalizeText(text);
            double resolvedDpi = ResolveDpi(relativeTo, options);
            float pxScale = (float)(resolvedDpi / 96d * (options.RenderScale > 0 ? options.RenderScale : 1d));
            float outputDpi = (float)(resolvedDpi * (options.RenderScale > 0 ? options.RenderScale : 1d));

            using (Font font = CreateFont(options, pxScale))
            using (Bitmap measureBitmap = new Bitmap(1, 1, PixelFormat.Format32bppPArgb))
            using (Graphics measureG = Graphics.FromImage(measureBitmap))
            {
                ApplyGraphicsQuality(measureG);
                LayoutResult layout = MeasureLayout(normalized, measureG, font, options, pxScale, relativeTo);

                Bitmap bitmap = new Bitmap(
                    Math.Max(1, layout.BitmapWidth),
                    Math.Max(1, layout.BitmapHeight),
                    PixelFormat.Format32bppPArgb);

                try
                {
                    bitmap.SetResolution(outputDpi, outputDpi);

                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        ApplyGraphicsQuality(g);
                        g.Clear(Color.Transparent);
                        DrawShadow(g, layout.BackgroundRect, options, pxScale);
                        DrawBackground(g, layout.BackgroundRect, options, pxScale);
                        DrawGlyphs(g, layout.Glyphs, font, options);
                    }

                    return bitmap;
                }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }
            }
        }

        // Fix #1: 使用字体基准行高统一每行步进,避免按字符高度换行带来的行距参差
        // Fix #2: 最后一行底部只计字符本身高度,不叠加 (lineHeightScale-1) 的行后间距
        // Fix #4: 降级路径取 work area 时传入 relativeTo,使 work area 与 resolvedDpi 取自同一屏
        private static LayoutResult MeasureLayout(
            string text, Graphics g, Font font, TextStickerOptions options, float pxScale, Visual relativeTo)
        {
            LayoutResult result = new LayoutResult();
            List<float> lineWidths = new List<float>();

            float maxWidthPx = ToPx((float)options.MaxTextWidthDip, pxScale);
            if (maxWidthPx <= 0)
                maxWidthPx = ToPx((float)DpiHelper.GetScreenWorkAreaDip(relativeTo).Width, pxScale);

            float paddingLeft = ToPx((float)options.Padding.Left, pxScale);
            float paddingTop = ToPx((float)options.Padding.Top, pxScale);
            float paddingRight = ToPx((float)options.Padding.Right, pxScale);
            float paddingBottom = ToPx((float)options.Padding.Bottom, pxScale);

            float borderWidth = ToPx((float)options.BorderThickness, pxScale);
            float outerMargin = Math.Max(1f, Math.Max(borderWidth + 1f, GetShadowMargin(options, pxScale)));

            float startX = outerMargin + paddingLeft;
            float startY = outerMargin + paddingTop;
            float x = startX;
            float y = startY;
            int lineIndex = 0;

            float lineHeightScale = options.LineHeightMultiplier > 0 ? (float)options.LineHeightMultiplier : 1f;
            float letterSpacing = options.LetterSpacingScale > 0 ? (float)options.LetterSpacingScale : 1f;

            float lineHeight = font.GetHeight(g);
            float maxBottom = startY + lineHeight;

            foreach (char c in text)
            {
                if (c == '\r') continue;

                SizeF charSize = MeasureGlyphSize(g, c, font);

                if (ShouldWrap(c, x - outerMargin, charSize.Width, maxWidthPx, paddingRight, letterSpacing))
                {
                    lineWidths.Add(Math.Max(0f, x - startX));
                    x = startX;
                    y += lineHeight * lineHeightScale;
                    lineIndex++;
                }

                if (c == '\n') continue;

                result.Glyphs.Add(new GlyphLayout
                {
                    Text = c.ToString(),
                    Rect = new RectangleF(x, y, charSize.Width, charSize.Height),
                    LineIndex = lineIndex
                });

                x += Math.Max(0f, charSize.Width * letterSpacing);
                maxBottom = Math.Max(maxBottom, y + lineHeight);
            }

            if (result.Glyphs.Count == 0)
            {
                lineWidths.Add(1f);
                // maxBottom 已初始化为 startY + lineHeight,无需再赋值
            }
            else
            {
                lineWidths.Add(Math.Max(0f, x - startX));
            }

            float contentWidth = ApplyTextAlignment(result.Glyphs, lineWidths, options.TextAlignment);
            if (contentWidth <= 0f) contentWidth = 1f;

            float boxWidth = contentWidth + paddingLeft + paddingRight + 2f;
            float boxHeight = (maxBottom - outerMargin) + paddingBottom;

            result.BackgroundRect = new RectangleF(
                outerMargin, outerMargin,
                Math.Max(1f, (float)Math.Ceiling(boxWidth)),
                Math.Max(1f, (float)Math.Ceiling(boxHeight)));

            result.BitmapWidth = (int)Math.Ceiling(result.BackgroundRect.Right + outerMargin);
            result.BitmapHeight = (int)Math.Ceiling(result.BackgroundRect.Bottom + outerMargin);

            return result;
        }

        #endregion

        #region 绘制部件

        private static void DrawGlyphs(Graphics g, IList<GlyphLayout> glyphs, Font font, TextStickerOptions options)
        {
            Color foreColor = ToDrawingColor(options.Foreground, Color.White);
            int redraw = Math.Max(1, options.RedrawCount);

            using (Brush brush = new SolidBrush(foreColor))
            using (StringFormat format = CreateStringFormat())
            {
                foreach (GlyphLayout glyph in glyphs)
                {
                    for (int j = 0; j < redraw; j++)
                        g.DrawString(glyph.Text, font, brush, glyph.Rect, format);
                }
            }
        }

        private static void DrawBackground(Graphics g, RectangleF rect, TextStickerOptions options, float pxScale)
        {
            float radius = ToPx((float)options.CornerRadius.TopLeft, pxScale);
            float borderWidth = ToPx((float)options.BorderThickness, pxScale);
            Color bgColor = ToDrawingColor(options.Background, Color.FromArgb(215, 16, 16, 24));
            Color borderColor = ToDrawingColor(options.BorderBrush, Color.FromArgb(40, 255, 255, 255));

            using (GraphicsPath path = CreateRoundRectPath(rect, radius))
            using (Brush bgBrush = new SolidBrush(bgColor))
            {
                g.FillPath(bgBrush, path);

                if (borderWidth > 0.01f)
                {
                    using (Pen pen = new Pen(borderColor, borderWidth * 2f) { DashStyle = DashStyle.Solid })
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }
        }

        private static void DrawShadow(Graphics g, RectangleF rect, TextStickerOptions options, float pxScale)
        {
            if (!options.DropShadow || options.ShadowColor.A <= 0) return;

            float dx = ToPx((float)options.ShadowOffsetX, pxScale);
            float dy = ToPx((float)options.ShadowOffsetY, pxScale);
            float blur = ToPx((float)options.ShadowRadius, pxScale);
            float radius = ToPx((float)options.CornerRadius.TopLeft, pxScale);

            Color shadowColor = Color.FromArgb(
                options.ShadowColor.A, options.ShadowColor.R,
                options.ShadowColor.G, options.ShadowColor.B);

            RectangleF shadowRect = new RectangleF(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
            int passes = Math.Max(1, Math.Min(6, (int)Math.Ceiling(blur / 6f)));
            int alpha = Math.Max(1, shadowColor.A / (passes + 1));

            for (int i = passes; i >= 1; i--)
            {
                float inflate = i * 1.2f;
                RectangleF passRect = shadowRect;
                passRect.Inflate(inflate, inflate);

                using (GraphicsPath path = CreateRoundRectPath(passRect, radius + inflate))
                using (Brush brush = new SolidBrush(Color.FromArgb(alpha, shadowColor)))
                {
                    g.FillPath(brush, path);
                }
            }
        }

        #endregion

        #region 排版换行 / 对齐

        private static bool ShouldWrap(
            char c, float currentX, float charWidth, float maxWidth, float paddingRight, float letterSpacing)
        {
            if (c == '\n') return true;

            if (currentX + paddingRight > maxWidth && !_hangChars.Contains(c))
                return true;

            if (currentX + charWidth * letterSpacing + paddingRight >= maxWidth && _wrapBeforeChars.Contains(c))
                return true;

            return false;
        }

        private static float ApplyTextAlignment(
            IList<GlyphLayout> glyphs, IList<float> lineWidths, TextAlignment alignment)
        {
            float contentWidth = 0f;
            foreach (float w in lineWidths)
                if (w > contentWidth) contentWidth = w;

            if (alignment == TextAlignment.Left || alignment == TextAlignment.Justify)
                return contentWidth;

            foreach (GlyphLayout glyph in glyphs)
            {
                float lineWidth = (glyph.LineIndex >= 0 && glyph.LineIndex < lineWidths.Count)
                    ? lineWidths[glyph.LineIndex]
                    : 0f;

                float offset = alignment == TextAlignment.Center
                    ? (contentWidth - lineWidth) / 2f
                    : contentWidth - lineWidth;  // Right

                if (Math.Abs(offset) > 0.01f)
                {
                    glyph.Rect = new RectangleF(
                        glyph.Rect.X + offset, glyph.Rect.Y,
                        glyph.Rect.Width, glyph.Rect.Height);
                }
            }

            return contentWidth;
        }

        #endregion

        #region 基础辅助

        private static StringFormat CreateStringFormat()
        {
            StringFormat format = new StringFormat();
            format.Trimming = StringTrimming.Word;
            format.FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.MeasureTrailingSpaces;
            format.Alignment = StringAlignment.Near;
            format.LineAlignment = StringAlignment.Near;
            format.HotkeyPrefix = HotkeyPrefix.None;
            return format;
        }

        private static SizeF MeasureGlyphSize(Graphics g, char c, Font font)
        {
            if (c == '\n' || c == '\r')
                return new SizeF(0f, font.GetHeight(g));

            string text = c.ToString();

            if (c == ' ' || c == '　')
            {
                using (StringFormat format = CreateStringFormat())
                    return g.MeasureString(text, font, PointF.Empty, format);
            }

            using (StringFormat format = new StringFormat())
            {
                format.FormatFlags = StringFormatFlags.MeasureTrailingSpaces;
                format.Alignment = StringAlignment.Near;
                format.LineAlignment = StringAlignment.Near;
                format.HotkeyPrefix = HotkeyPrefix.None;
                format.SetMeasurableCharacterRanges(new[] { new CharacterRange(0, 1) });

                Region[] regions = null;
                try
                {
                    regions = g.MeasureCharacterRanges(text, font, new RectangleF(0f, 0f, 2048f, 2048f), format);
                    if (regions.Length > 0 && regions[0] != null)
                    {
                        RectangleF rect = regions[0].GetBounds(g);
                        if (rect.Width > 0.01f && rect.Height > 0.01f)
                            return rect.Size;
                    }
                }
                finally
                {
                    if (regions != null)
                        foreach (Region r in regions) r?.Dispose();
                }
            }

            using (StringFormat fallback = CreateStringFormat())
                return g.MeasureString(text, font, PointF.Empty, fallback);
        }

        private static Font CreateFont(TextStickerOptions options, float pxScale)
        {
            FontStyleDrawing style = FontStyleDrawing.Regular;
            if (options.FontStyle == FontStyles.Italic || options.FontStyle == FontStyles.Oblique)
                style |= FontStyleDrawing.Italic;
            if (options.FontWeight >= FontWeights.SemiBold)
                style |= FontStyleDrawing.Bold;

            float fontSizePx = Math.Max(1f, ToPx((float)options.FontSize, pxScale));
            string family = string.IsNullOrWhiteSpace(options.FontFamily)
                ? "Microsoft YaHei UI"
                : options.FontFamily;

            return new Font(family, fontSizePx, style, GraphicsUnit.Pixel);
        }

        private static float GetShadowMargin(TextStickerOptions options, float pxScale)
        {
            if (!options.DropShadow) return 0f;

            float blur = ToPx((float)options.ShadowRadius, pxScale);
            float dx = Math.Abs(ToPx((float)options.ShadowOffsetX, pxScale));
            float dy = Math.Abs(ToPx((float)options.ShadowOffsetY, pxScale));
            return blur + Math.Max(dx, dy) + 2f;
        }

        private static void ApplyGraphicsQuality(Graphics g)
        {
            g.PageUnit = GraphicsUnit.Pixel;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        }

        private static GraphicsPath CreateRoundRectPath(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();

            if (radius <= 0.5f)
            {
                path.AddRectangle(rect);
                path.CloseFigure();
                return path;
            }

            float diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }

        private static float ToPx(float dip, float pxScale)
            => dip <= 0f ? 0f : dip * pxScale;

        // relativeTo 非 null 时通过 DpiHelper.GetDpi 获取完整回退链(PresentationSource → HWND → SystemDpi);
        // 为 null 时优先取 options.Dpi,该值无效时再回退到真实系统 DPI
        private static double ResolveDpi(Visual relativeTo, TextStickerOptions options)
        {
            if (relativeTo != null)
            {
                try { return DpiHelper.GetDpi(relativeTo).PixelsPerInchX; }
                catch { }
            }

            if (options.Dpi > 0)
                return options.Dpi;

            return DpiHelper.SystemDpi.PixelsPerInchX;
        }

        private static string NormalizeText(string text)
            => string.IsNullOrEmpty(text) ? string.Empty : text.Replace("\r\n", "\n").Replace('\r', '\n');

        private static Color ToDrawingColor(System.Windows.Media.Brush brush, Color fallback)
        {
            SolidColorBrush solid = brush as SolidColorBrush;
            if (solid == null) return fallback;
            System.Windows.Media.Color c = solid.Color;
            return Color.FromArgb(c.A, c.R, c.G, c.B);
        }

        #endregion
    }
}