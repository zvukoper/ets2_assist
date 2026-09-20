using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    /// <summary>
    /// Единая палитра интерфейса приложения. Значения совпадают с цветовой схемой
    /// окна квестов (web_quests.html), чтобы интерфейсы выглядели одинаково.
    ///
    /// ВАЖНО: редактор карт имеет собственную палитру и здесь не затрагивается.
    /// </summary>
    internal static class UiPalette
    {
        /// <summary>Основной акцентный оранжевый.</summary>
        public static readonly Color Accent = FromHex(0xFAB003);
        /// <summary>Второстепенный оранжевый: подсветка активных кнопок.</summary>
        public static readonly Color AccentSecondary = FromHex(0xB4810C);
        /// <summary>Приглушённый (неактивный) белый.</summary>
        public static readonly Color MutedWhite = FromHex(0xCCCCCC);
        /// <summary>Приглушённый оранжевый.</summary>
        public static readonly Color MutedAccent = FromHex(0x8A6321);
        /// <summary>Второстепенный серый для текста.</summary>
        public static readonly Color TextMuted = FromHex(0xA6A6A6);
        /// <summary>Фон кликабельных меню (кнопок).</summary>
        public static readonly Color Panel = FromHex(0x262626);
        /// <summary>Фон при наведении.</summary>
        public static readonly Color Hover = FromHex(0x181F23);
        /// <summary>Фон выбранного элемента.</summary>
        public static readonly Color Selected = FromHex(0x4B5A66);
        /// <summary>Тёмный фон окон (совпадает с игровым).</summary>
        public static readonly Color WindowBackground = FromHex(0x2B2B2B);
        /// <summary>Фон полей ввода.</summary>
        public static readonly Color InputBackground = FromHex(0x1E1E1E);
        /// <summary>Основной цвет текста.</summary>
        public static readonly Color TextPrimary = FromHex(0xE8E8E8);

        /// <summary>Спеццвет: особый синий.</summary>
        public static readonly Color SpecialBlue = FromHex(0x12ABE5);
        /// <summary>Спеццвет: красный (резерв).</summary>
        public static readonly Color SpecialRed = FromHex(0xCF0C0C);
        /// <summary>Спеццвет: салатовый (резерв).</summary>
        public static readonly Color SpecialLime = FromHex(0x11FB06);
        /// <summary>Спеццвет: оранжевый из навигатора (резерв).</summary>
        public static readonly Color SpecialNavOrange = FromHex(0xFFC64C);
        /// <summary>Спеццвет: тёмно-зелёный (резерв).</summary>
        public static readonly Color SpecialDarkGreen = FromHex(0x008234);

        /// <summary>Радиус скругления кнопок и панелей, px.</summary>
        public const int CornerRadius = 6;

        private static Color FromHex(int rgb) =>
            Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        /// <summary>
        /// Скруглить контрол.
        /// ВНИМАНИЕ: отключено. Region подгоняется под текущий размер контрола, но
        /// при DPI-масштабировании Windows/пересчёте размеров он не пересчитывается
        /// и обрезает кнопку с текстом. Владелец контрола должен пересчитывать
        /// Region в OnResize, если скругление всё же понадобится.
        /// </summary>
        public static void ApplyRoundedRegion(Control c, int radius = CornerRadius)
        {
            if (c == null) return;
            c.Region?.Dispose();
            c.Region = null;
        }

        /// <summary>Построить закруглённый прямоугольник.</summary>
        public static GraphicsPath RoundedPath(Rectangle r, int radius)
        {
            int d = System.Math.Max(1, System.Math.Min(radius, System.Math.Min(r.Width, r.Height) / 2));
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d * 2, d * 2, 180, 90);
            path.AddArc(r.Right - d * 2 - 1, r.Y, d * 2, d * 2, 270, 90);
            path.AddArc(r.Right - d * 2 - 1, r.Bottom - d * 2 - 1, d * 2, d * 2, 0, 90);
            path.AddArc(r.X, r.Bottom - d * 2 - 1, d * 2, d * 2, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// Единый вид кнопки: плоский стиль и палитра приложения.
        /// </summary>
        public static void StyleButton(Button b, Color? background = null, Color? foreground = null)
        {
            if (b == null) return;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Panel;
            b.FlatAppearance.MouseOverBackColor = Hover;
            b.FlatAppearance.MouseDownBackColor = Selected;
            b.BackColor = background ?? Panel;
            b.ForeColor = foreground ?? TextPrimary;
            b.UseVisualStyleBackColor = false;
            b.AutoEllipsis = false;
        }

        /// <summary>
        /// Единая раскраска меню приложения: тёмный фон панели, светлый текст,
        /// приглушённая оранжевая подсветка наведения. Без этой настройки
        /// ToolStrip использует системную (ярко-голубую) подсветку.
        /// </summary>
        public static void StyleMenuStrip(ToolStrip strip)
        {
            if (strip == null) return;
            strip.BackColor = Panel;
            strip.ForeColor = TextPrimary;
            strip.Renderer = new ToolStripProfessionalRenderer(new MenuColors());
            foreach (ToolStripItem item in strip.Items) StyleMenuItem(item);
        }

        private static void StyleMenuItem(ToolStripItem item)
        {
            item.BackColor = Panel;
            item.ForeColor = TextPrimary;
            if (item is ToolStripMenuItem mi)
            {
                mi.DropDown.BackColor = InputBackground;
                mi.DropDown.ForeColor = TextPrimary;
                foreach (ToolStripItem sub in mi.DropDownItems) StyleMenuItem(sub);
            }
        }

        /// <summary>Цветовая таблица меню: убирает системный голубой hover.</summary>
        private sealed class MenuColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected => Hover;
            public override Color MenuItemSelectedGradientBegin => Hover;
            public override Color MenuItemSelectedGradientEnd => Hover;
            public override Color MenuItemPressedGradientBegin => Selected;
            public override Color MenuItemPressedGradientEnd => Selected;
            public override Color MenuItemBorder => Accent;
            public override Color MenuBorder => Panel;
            public override Color ToolStripDropDownBackground => InputBackground;
            public override Color ImageMarginGradientBegin => InputBackground;
            public override Color ImageMarginGradientMiddle => InputBackground;
            public override Color ImageMarginGradientEnd => InputBackground;
            public override Color SeparatorDark => Panel;
            public override Color SeparatorLight => Panel;
        }
    }
}
