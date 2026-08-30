using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    /// <summary>
    /// Статусная строка редактора карты (и веб-миникарты — по общему дизайну).
    /// Делится по ширине на две части:
    ///  ЛЕВАЯ — индикация состояний системы: окружности-индикаторы (светло-серый фон,
    ///    2px тёмно-серая обводка; при активности — заливка lime) + текст справа.
    ///    Выровнено по левому краю. Сейчас один индикатор: «данные транспорта».
    ///  ПРАВАЯ — выполняемые операции: вращающийся круглый индикатор загрузки, справа
    ///    от него — что выполняется. Когда операция завершена — вместо вращения
    ///    рисуется тёмно-зелёная галочка, текст последней операции остаётся
    ///    до начала новой. Активные операции: загрузка точек, обновление overrides и т.п.
    /// Фон строки тёмно-серый, шрифт приглушённо-белый с чёрной обводкой.
    /// Клик по строке (в редакторе) — открыть папку логов приложения.
    /// </summary>
    public sealed class EditorStatusBar : Control
    {
        // ==== Оформление ====
        private static readonly Color BgColor = Color.FromArgb(38, 42, 48);        // тёмно-серый фон
        private static readonly Color IdleCircle = Color.FromArgb(70, 78, 88);     // тускло-серая окружность (нет данных)
        private static readonly Color CircleBorder = Color.FromArgb(115, 122, 132); // 2px обводка
        private static readonly Color ActiveLime = Color.FromArgb(190, 255, 90);   // lime
        private static readonly Color CheckGreen = Color.FromArgb(30, 140, 60);    // тёмно-зелёная галочка
        private static readonly Color TextColor = Color.FromArgb(198, 205, 214);   // приглушённо белый
        private const float CircleD = 7f;   // диаметр окружности-индикатора
        private const float SpinR = 5.5f;   // радиус вращающегося индикатора

        private readonly Timer _anim = new() { Interval = 33 }; // ~30 fps вращение
        private float _angle;                 // угол вращения (градусы)
        private bool _busy;                   // идёт операция -> вращающийся индикатор
        private string _opText = "";          // текст операции (сохраняется и после завершения)

        // Состояния системы (левая часть): title -> активна?
        private readonly System.Collections.Generic.List<(string title, bool active)> _states = new();

        public EditorStatusBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 24; // визуально низкая строка (8px контент + отступы)
            Font = new Font("Segoe UI", 7.5f);
            Cursor = Cursors.Hand;
            BackColor = BgColor;
            _anim.Tick += (s, e) => { if (_busy && IsHandleCreated && !IsDisposed) { _angle = (_angle + 8f) % 360f; Invalidate(); } };
            _anim.Start();
            Click += (s, e) => OpenLogFolder();
        }

        // Индикация состояния (левая часть). active = закрасить lime, иначе пустая окружность
        // + текст «нет данных …».
        public void SetSystemState(string title, bool active)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                if (InvokeRequired) { BeginInvoke((Action)(() => SetSystemState(title, active))); return; }
                var existing = _states.FindIndex(s => s.title == title);
                if (existing >= 0) _states[existing] = (title, active);
                else _states.Add((title, active));
                Invalidate();
            }
            catch (ObjectDisposedException) { }
        }

        // Операция (правая часть): busy=true — крутится, текст — что выполняется;
        // busy=false — галочка, текст остаётся до следующей операции.
        public void SetOperation(string text, bool busy)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                if (InvokeRequired) { BeginInvoke((Action)(() => SetOperation(text, busy))); return; }
                _opText = text ?? "";
                _busy = busy;
                Invalidate();
            }
            catch (ObjectDisposedException) { }
        }

        private void OpenLogFolder()
        {
            try
            {
                string logDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                if (System.IO.Directory.Exists(logDir))
                    Process.Start("explorer.exe", logDir);
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // ВСЯ отрисовка в try/catch: любой сбой GDI+ не должен ронять приложение.
            try
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(BgColor);

                using var textBrush = new SolidBrush(TextColor);
                using var blackBrush = new SolidBrush(Color.Black);
                // КЛОН шрифта — исключает «Parameter is not valid» из MeasureString,
                // если базовый Font освобождается извне во время отрисовки (крэш из стека).
                using var font = (Font)Font.Clone();

                int cy = Height / 2;

                // ==== ЛЕВАЯ ЧАСТЬ: состояния системы ====
                float x = 6f;
                foreach (var (title, active) in _states.ToArray())
                {
                    // окружность-индикатор: светлосерый фон, 2px тёмносерая обводка;
                    // активное состояние — заливка lime.
                    var rect = new RectangleF(x, cy - CircleD / 2, CircleD, CircleD);
                    using (var fill = new SolidBrush(active ? ActiveLime : IdleCircle))
                        g.FillEllipse(fill, rect);
                    using (var pen = new Pen(CircleBorder, 2f))
                        g.DrawEllipse(pen, rect);
                    string label = active ? title : ("нет " + title);
                    x = DrawOutlined(g, label, x + CircleD + 5f, cy, font, textBrush, blackBrush) + 14f;
                }

                // ==== ПРАВАЯ ЧАСТЬ: текущая операция ====
                // Вращающийся индикатор при выполнении, иначе тёмно-зелёная галочка.
                string op = _opText;
                SizeF textSize = string.IsNullOrEmpty(op) ? SizeF.Empty : SafeMeasure(g, op, font);
                // Спиннер/галочка справа; не заезжаем за середину строки.
                float spinnerX = Math.Max(Width * 0.5f + 12f, Width - 10f - (textSize.Width + 5f) - SpinR);

                if (_busy)
                {
                    using var pen = new Pen(ActiveLime, 1.8f);
                    pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                    var rect = new RectangleF(spinnerX - SpinR, cy - SpinR, SpinR * 2, SpinR * 2);
                    g.DrawArc(pen, rect, _angle, 240f);
                }
                else
                {
                    // Тёмно-зелёная галочка в окружности.
                    var rect = new RectangleF(spinnerX - SpinR, cy - SpinR, SpinR * 2, SpinR * 2);
                    using var pen = new Pen(CheckGreen, 1.8f);
                    g.DrawEllipse(pen, rect);
                    float cx = spinnerX;
                    using var checkPen = new Pen(CheckGreen, 1.8f);
                    checkPen.StartCap = LineCap.Round; checkPen.EndCap = LineCap.Round;
                    g.DrawLine(checkPen, cx - 3f, cy + 0.4f, cx - 1f, cy + 2.8f);
                    g.DrawLine(checkPen, cx - 1f, cy + 2.8f, cx + 3.2f, cy - 2.6f);
                }

                if (!string.IsNullOrEmpty(op))
                    DrawOutlined(g, op, spinnerX + SpinR + 5f, cy, font, textBrush, blackBrush);

                // ==== Разделитель половин ====
                using var sepPen = new Pen(Color.FromArgb(60, 66, 74), 1f);
                g.DrawLine(sepPen, Width * 0.5f, 2f, Width * 0.5f, Height - 2f);
            }
            catch (Exception)
            {
                // Проглатываем ошибки рисования (форма закрывается / смена DPI и т.п.).
            }
        }

        // MeasureString, который не бросается: при ошибке — оценка по ширине текста.
        private static SizeF SafeMeasure(Graphics g, string text, Font font)
        {
            try { return g.MeasureString(text, font); }
            catch
            {
                float fh = font?.Size ?? 8f;
                return new SizeF(text.Length * fh * 0.55f, fh * 1.4f);
            }
        }

        // Текст с чёрной обводкой (4 направления). Возвращает X после текста.
        private static float DrawOutlined(Graphics g, string text, float x, float cy, Font font, Brush fg, Brush outline)
        {
            if (string.IsNullOrEmpty(text) || g == null) return x;
            var size = SafeMeasure(g, text, font);
            float y = cy - size.Height / 2f;
            foreach (var (dx, dy) in new[] { (-1f, 0f), (1f, 0f), (0f, -1f), (0f, 1f) })
                g.DrawString(text, font, outline, x + dx, y + dy);
            g.DrawString(text, font, fg, x, y);
            return x + size.Width;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _anim.Stop(); _anim.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}