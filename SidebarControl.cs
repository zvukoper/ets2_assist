using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    // ============================================================
    // События сайдбара
    // ============================================================

    // Обычный клик по строке (точка/категория).
    internal sealed class SidebarItemEventArgs : EventArgs
    {
        public SidebarItem Item { get; }
        public SidebarItemEventArgs(SidebarItem item) { Item = item; }
    }

    // Изменение множественного выбора точек (чекбокс точки).
    internal sealed class SidebarSelectionChangedEventArgs : EventArgs
    {
        // id точки, по чекбоксу которой кликнули (null = программное изменение).
        public string? ClickedId { get; }
        public SidebarSelectionChangedEventArgs(string? clickedId) { ClickedId = clickedId; }
    }

    // Изменение видимости категории (чекбокс категории).
    internal sealed class SidebarCategoryVisibilityChangedEventArgs : EventArgs
    {
        public string CategoryId { get; }
        public bool Visible { get; }
        public SidebarCategoryVisibilityChangedEventArgs(string categoryId, bool visible)
        {
            CategoryId = categoryId;
            Visible = visible;
        }
    }

    // Правый клик (контекстное меню).
    internal sealed class SidebarContextMenuEventArgs : EventArgs
    {
        public SidebarItem Item { get; }
        public IReadOnlyCollection<string> SelectedIds { get; }
        public Point ClientPoint { get; }
        public SidebarContextMenuEventArgs(SidebarItem item, IReadOnlyCollection<string> selectedIds, Point clientPoint)
        {
            Item = item;
            SelectedIds = selectedIds;
            ClientPoint = clientPoint;
        }
    }

    // Изменение раскрытия категории.
    internal sealed class SidebarCategoryExpandedChangedEventArgs : EventArgs
    {
        public string CategoryId { get; }
        public bool Expanded { get; }
        public SidebarCategoryExpandedChangedEventArgs(string categoryId, bool expanded)
        {
            CategoryId = categoryId;
            Expanded = expanded;
        }
    }

    // ============================================================
    // SidebarControl — самостоятельный custom control сайдбара.
    // Полностью отвечает за категории, точки, expand/collapse,
    // checkbox, single/multi selection, hit testing, правый клик,
    // отрисовку строк и вертикальную прокрутку.
    // НЕ знает про MapEditorForm / PointData / _catVisible.
    // ============================================================
    internal sealed class SidebarControl : ScrollableControl
    {
        // v39.54: глобальная палитра — приглушённый белый (#A6A6A6) обычного текста;
        // супер-белый (#FFFFFF) — только для выделения.
        public static readonly Color MutedWhite = Color.FromArgb(166, 166, 166);
        public static readonly Color SuperWhite = Color.White;

        // --- Фиксированная геометрия ---
        private const int RowHeight = 23;
        private const int LeftPadding = 5;
        private const int IndentWidth = 18;
        private const int ExpandSize = 12;
        private const int CheckBoxSize = 16;
        private const int CheckBoxHitPadding = 3;
        private const int TextGap = 6;

        // --- Модель данных ---
        private readonly List<SidebarItem> _items = new();
        private readonly List<SidebarVisibleRow> _visibleRows = new();
        private readonly HashSet<string> _selectedIds = new();
        private string? _activeItemId;

        // --- Кэшированные GDI-объекты (не создаём на каждую строку) ---
        private readonly Font _font;
        private readonly Font _activeFont;
        private readonly StringFormat _textFormat;

        // --- Защита от случайного выбора точки, появившейся под курсором после expand ---
        private bool _consumeMouseUp;

        // --- События ---
        public event EventHandler<SidebarItemEventArgs>? ItemActivated;
        public event EventHandler<SidebarSelectionChangedEventArgs>? SelectionChanged;
        public event EventHandler<SidebarCategoryVisibilityChangedEventArgs>? CategoryVisibilityChanged;
        public event EventHandler<SidebarContextMenuEventArgs>? ContextMenuRequested;
        // Изменение раскрытия категории (для сохранения состояния).
        public event EventHandler<SidebarCategoryExpandedChangedEventArgs>? CategoryExpandedChanged;

        // --- Публичное состояние ---
        public string? ActiveItemId => _activeItemId;
        public IReadOnlyCollection<string> SelectedIds => _selectedIds;

        public SidebarControl()
        {
            DoubleBuffered = true;
            AutoScroll = true;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Color.FromArgb(20, 25, 35);
            ForeColor = MutedWhite;
            _font = new Font("Segoe UI", 9);
            _activeFont = new Font("Segoe UI", 9, FontStyle.Bold);
            _textFormat = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
        }

        // --- Строка видимого списка ---
        private sealed class SidebarVisibleRow
        {
            public SidebarItem Item { get; init; } = null!;
            public Rectangle RowBounds { get; init; }
            public Rectangle ExpandBounds { get; init; }
            public Rectangle CheckboxBounds { get; init; }
        }

        // --- Публичная модель данных ---
        public void SetItems(IEnumerable<SidebarItem> items)
        {
            _items.Clear();
            foreach (var it in items) _items.Add(it);
            RebuildVisibleRows();
            Invalidate();
        }

        // --- Установка множественного выбора (синхронизирует и item.Checked) ---
        public void SetSelectedIds(IEnumerable<string> ids)
        {
            _selectedIds.Clear();
            foreach (var id in ids) _selectedIds.Add(id);
            foreach (var it in _items)
                if (it.Type == SidebarItemType.Point)
                    it.Checked = _selectedIds.Contains(it.Id);
            Invalidate();
        }

        // --- Установка активной точки ---
        public void SetActiveId(string? id)
        {
            _activeItemId = id;
            foreach (var it in _items)
                if (it.Type == SidebarItemType.Point)
                    it.Active = it.Id == id;
            Invalidate();
        }

        // v39.57: АТОМАРНАЯ синхронизация состояния выделения из MapEditorForm.
        // Единая точка правды: чекбоксы = ids, single = activeId. Вызывается ПОСЛЕ
        // любого изменения выделения (карта/сайдбар/сброс) — устраняет рассинхрон.
        public void SetSelectionState(IReadOnlyCollection<string> ids, string? activeId)
        {
            _selectedIds.Clear();
            foreach (var id in ids) _selectedIds.Add(id);
            _activeItemId = activeId;
            foreach (var it in _items)
            {
                if (it.Type != SidebarItemType.Point) continue;
                it.Checked = _selectedIds.Contains(it.Id);
                it.Active = it.Id == activeId;
            }
            Invalidate();
        }

        // --- Сброс активной точки (при multi-selection >= 2) ---
        public void ClearActive()
        {
            _activeItemId = null;
            foreach (var it in _items)
                if (it.Type == SidebarItemType.Point)
                    it.Active = false;
            Invalidate();
        }

        // v39.56: раскрыть категорию точки и прокрутить список до неё.
        // Возвращает true, если точка найдена и стала видимой.
        public bool RevealItem(string pointId)
        {
            foreach (var cat in _items)
            {
                if (cat.Type != SidebarItemType.Category) continue;
                var pt = cat.Children.FirstOrDefault(p => p.Id == pointId);
                if (pt == null) continue;

                bool scrolled = false;
                if (!cat.Expanded)
                {
                    cat.Expanded = true;
                    CategoryExpandedChanged?.Invoke(this,
                        new SidebarCategoryExpandedChangedEventArgs(cat.Id, true));
                    RebuildVisibleRows();
                }
                // Индекс строки точки = категория + количество предыдущих детей (все раскрыты).
                int rowIndex = 0;
                foreach (var c in _items)
                {
                    if (c.Type != SidebarItemType.Category) continue;
                    if (c == cat) { rowIndex++; break; }
                    if (c.Expanded) rowIndex += c.Children.Count + 1;
                    else rowIndex += 1;
                }
                // Точка: (rowIndex - 1) — индекс строки категории; сама точка — категория + позиция.
                int catRowIndex = rowIndex - 1;
                int ptIndexInCat = cat.Children.IndexOf(pt);
                int ptRow = catRowIndex + 1 + ptIndexInCat;
                // Прокрутка: AutoScrollPosition задаётся отрицательными значениями.
                int rowY = ptRow * RowHeight;
                int visibleHeight = ClientSize.Height - RowHeight;
                int current = -AutoScrollPosition.Y;
                if (rowY < current || rowY > current + visibleHeight)
                {
                    scrolled = true;
                    AutoScrollPosition = new Point(0, Math.Max(0, rowY - visibleHeight / 2));
                }
                Invalidate();
                return true;
            }
            return false;
        }

        // --- Пересборка видимых строк (категории + раскрытые точки) ---
        private void RebuildVisibleRows()
        {
            _visibleRows.Clear();
            int rowIndex = 0;
            foreach (var cat in _items)
            {
                if (cat.Type != SidebarItemType.Category) continue;
                AddRow(cat, rowIndex++);
                if (cat.Expanded)
                {
                    foreach (var pt in cat.Children)
                        if (pt.Type == SidebarItemType.Point)
                            AddRow(pt, rowIndex++);
                }
            }
            AutoScrollMinSize = new Size(0, _visibleRows.Count * RowHeight);
        }

        private void AddRow(SidebarItem item, int rowIndex)
        {
            var rowBounds = new Rectangle(0, rowIndex * RowHeight, ClientSize.Width, RowHeight);
            var expandBounds = Rectangle.Empty;
            var cbBounds = GetCheckboxBounds(item, rowBounds);
            if (item.Type == SidebarItemType.Category)
            {
                int arrowX = LeftPadding;
                int arrowY = rowBounds.Y + (RowHeight - ExpandSize) / 2;
                expandBounds = new Rectangle(arrowX, arrowY, ExpandSize, ExpandSize);
            }
            _visibleRows.Add(new SidebarVisibleRow
            {
                Item = item,
                RowBounds = rowBounds,
                ExpandBounds = expandBounds,
                CheckboxBounds = cbBounds
            });
        }

        // Единая формула геометрии чекбокса (используется и при Draw, и при hit testing).
        private Rectangle GetCheckboxBounds(SidebarItem item, Rectangle rowBounds)
        {
            int cbX = item.Type == SidebarItemType.Category
                ? LeftPadding + ExpandSize + 4
                : LeftPadding + IndentWidth;
            int cbY = rowBounds.Y + (RowHeight - CheckBoxSize) / 2;
            return new Rectangle(cbX, cbY, CheckBoxSize, CheckBoxSize);
        }

        // --- Отрисовка ---
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            int scrollY = -AutoScrollPosition.Y;
            int firstRow = Math.Max(0, scrollY / RowHeight);
            int lastRow = Math.Min(_visibleRows.Count - 1, (scrollY + ClientSize.Height) / RowHeight);

            for (int i = firstRow; i <= lastRow; i++)
            {
                var row = _visibleRows[i];
                var bounds = row.RowBounds;
                bounds.Y -= scrollY; // в клиентские координаты
                DrawRow(g, row, bounds);
            }
        }

        private void DrawRow(Graphics g, SidebarVisibleRow row, Rectangle bounds)
        {
            var item = row.Item;
            bool isCat = item.Type == SidebarItemType.Category;

            // 1. Фон строки.
            // v39.54: обычный фон; выделенная точка — фон чуть светлее фона сайдбара
            // (одинаково для single-выделения и checkbox-мультивыбора).
            bool selected = !isCat && (item.Active || item.Checked);
            Color bg = Color.FromArgb(22, 27, 38);
            if (selected) bg = Color.FromArgb(34, 41, 55);
            else if (!isCat && item.Dirty) bg = Color.FromArgb(120, 60, 12);
            using (var bgBrush = new SolidBrush(bg))
                g.FillRectangle(bgBrush, bounds);

            // 2. Стрелка expand/collapse (только категории).
            if (isCat && !row.ExpandBounds.IsEmpty)
            {
                var eb = row.ExpandBounds;
                eb.Y = bounds.Y + (RowHeight - ExpandSize) / 2;
                using var arrowBrush = new SolidBrush(MutedWhite);
                string glyph = item.Expanded ? "▼" : "▶";
                g.DrawString(glyph, _font, arrowBrush, eb, _textFormat);
            }

            // 3. Чекбокс (цвет категории). v39.54: галочка — ЧЁРНАЯ.
            var cb = row.CheckboxBounds;
            cb.Y = bounds.Y + (RowHeight - CheckBoxSize) / 2;
            using (var cbBrush = new SolidBrush(item.CategoryColor))
                g.FillRectangle(cbBrush, cb);
            using (var cbPen = new Pen(Color.FromArgb(90, 100, 120), 1f))
                g.DrawRectangle(cbPen, cb);
            bool checkedState = isCat ? item.CategoryVisible : item.Checked;
            if (checkedState)
            {
                using var checkPen = new Pen(Color.Black, 2f);
                g.DrawLine(checkPen, cb.X + 3, cb.Y + 8, cb.X + 7, cb.Y + 12);
                g.DrawLine(checkPen, cb.X + 7, cb.Y + 12, cb.X + 13, cb.Y + 4);
            }

            // 4. Текст (обрезается по ширине контрола).
            int textX = cb.Right + TextGap;
            int textWidth = bounds.Right - textX - 4;
            if (textWidth > 0)
            {
                var textRect = new Rectangle(textX, bounds.Y, textWidth, bounds.Height);
                // v39.54: фон светлее — только под ТЕКСТОМ выделенных точек (супербелый жирный).
                if (selected)
                {
                    using var activeBg = new SolidBrush(Color.FromArgb(48, 58, 78));
                    g.FillRectangle(activeBg, textRect);
                }
                // Цвет текста: выделенные — супербелый жирный; грязные — оранжевый;
                // обычные — приглушённый белый.
                Color textColor = selected ? SuperWhite
                    : isCat ? MutedWhite
                    : (item.Dirty ? Color.Orange : MutedWhite);
                var font = selected ? _activeFont : _font;
                using var textBrush = new SolidBrush(textColor);
                g.DrawString(item.Text, font, textBrush, textRect, _textFormat);
            }
        }

        // --- Обработка мыши ---
        protected override void OnMouseDown(MouseEventArgs e)
        {
            var row = HitTestRow(e.Location);
            if (row == null) return;

            // 1. Правый клик — только контекстное меню, ничего не меняем.
            if (e.Button == MouseButtons.Right)
            {
                ContextMenuRequested?.Invoke(this, new SidebarContextMenuEventArgs(
                    row.Item, _selectedIds.ToList(), e.Location));
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            // 2. Категория + стрелка — expand/collapse.
            if (row.Item.Type == SidebarItemType.Category && ToClientRect(row.ExpandBounds).Contains(e.Location))
            {
                row.Item.Expanded = !row.Item.Expanded;
                _consumeMouseUp = true;
                CategoryExpandedChanged?.Invoke(this,
                    new SidebarCategoryExpandedChangedEventArgs(row.Item.Id, row.Item.Expanded));
                RebuildVisibleRows();
                Invalidate();
                return;
            }

            // 3. Чекбокс.
            if (ToClientRect(row.CheckboxBounds).Contains(e.Location))
            {
                ToggleCheckbox(row.Item);
                return;
            }

            // 4. Точка — активация.
            if (row.Item.Type == SidebarItemType.Point)
            {
                ActivatePoint(row.Item);
                return;
            }

            // 5. Категория — активация (без expand/collapse).
            if (row.Item.Type == SidebarItemType.Category)
            {
                ItemActivated?.Invoke(this, new SidebarItemEventArgs(row.Item));
            }
        }

        // Преобразует прямоугольник из координат virtual content в клиентские координаты
        // (с учётом вертикальной прокрутки). Не изменяет cached bounds.
        private Rectangle ToClientRect(Rectangle contentRect)
        {
            contentRect.Y += AutoScrollPosition.Y;
            return contentRect;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_consumeMouseUp)
            {
                _consumeMouseUp = false;
                return;
            }
            base.OnMouseUp(e);
        }

        // Определяет строку по клиентской координате (с учётом прокрутки).
        private SidebarVisibleRow? HitTestRow(Point clientPoint)
        {
            int contentY = clientPoint.Y - AutoScrollPosition.Y;
            int rowIndex = contentY / RowHeight;
            if (rowIndex < 0 || rowIndex >= _visibleRows.Count) return null;
            return _visibleRows[rowIndex];
        }

        // Переключение чекбокса: категория -> видимость (локально, уведомление);
        // точка -> мультивыбор (v39.58: состояние меняет ТОЛЬКО MapEditorForm через
        // SelectionChanged → SetSelectionState; локально ничего не правим).
        private void ToggleCheckbox(SidebarItem item)
        {
            if (item.Type == SidebarItemType.Category)
            {
                item.CategoryVisible = !item.CategoryVisible;
                CategoryVisibilityChanged?.Invoke(this,
                    new SidebarCategoryVisibilityChangedEventArgs(item.Id, item.CategoryVisible));
                Invalidate();
                return;
            }
            SelectionChanged?.Invoke(this, new SidebarSelectionChangedEventArgs(item.Id));
        }

        // Активация точки (клик по названию): v39.58 — SidebarControl НЕ меняет своё
        // состояние сам. Только шлёт ItemActivated; MapEditorForm устанавливает выделение
        // (сброс мультивыбора) и синхронизирует обратно через SetSelectionState.
        // Это устраняет рассинхрон: источник правды — ТОЛЬКО MapEditorForm.
        private void ActivatePoint(SidebarItem item)
        {
            ItemActivated?.Invoke(this, new SidebarItemEventArgs(item));
            // Защита: если обработчик не установил состояние (точка не в модели) —
            // снимаем подсветку (выделение не должно «прилипать» к неизвестной точке).
            if (_activeItemId == null)
                Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _font.Dispose();
                _activeFont.Dispose();
                _textFormat.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
