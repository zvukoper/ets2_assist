using System.Collections.Generic;
using System.Drawing;

namespace ETS2_Assist_GUI
{
    // Тип строки сайдбара.
    internal enum SidebarItemType
    {
        Category,
        Point
    }

    // Модель UI-строки сайдбара. Не использует TreeNode.
    internal sealed class SidebarItem
    {
        public SidebarItemType Type { get; init; }

        // Стабильный id (для категории — id категории; для точки — gameName / составной ключ).
        public string Id { get; init; } = "";

        // Отображаемый текст.
        public string Text { get; init; } = "";

        // Для точки — id категории, к которой она принадлежит.
        public string? CategoryId { get; init; }

        // Цвет категории (используется ТОЛЬКО для чекбокса).
        public Color CategoryColor { get; init; } = Color.LightGray;

        // Состояние раскрытия (только для категорий).
        public bool Expanded { get; set; }

        // Для точки — входит ли в мультивыбор (_selectedIds).
        public bool Checked { get; set; }

        // Для точки — является ли активной (загружена в панель).
        public bool Active { get; set; }

        // Для точки — есть ли несохранённые правки (оранжевый фон).
        public bool Dirty { get; init; }

        // Для категории — видимость категории на карте (_catVisible).
        public bool CategoryVisible { get; set; }

        // Произвольные данные (не используется для TreeNode.Tag).
        public object? Tag { get; init; }

        // Дочерние строки (точки категории).
        public List<SidebarItem> Children { get; } = new();
    }
}
