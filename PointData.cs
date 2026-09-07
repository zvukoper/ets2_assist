using System;
using System.Collections.Generic;

namespace ETS2_Assist_GUI
{
    // Режим отображения поля в панели редактирования точки.
    public enum PointFieldMode
    {
        Editable,
        ReadOnly,
        Hidden
    }

    // Описание одного редактируемого поля точки.
    public sealed class PointField
    {
        public string Key = "";
        public string Label = "";
        public bool Required;
        public PointFieldMode Mode = PointFieldMode.Editable;
        public string Group = "Основное";
        public Type ValueType = typeof(string);
    }

    // Единая модель данных точки (цели/пользовательской точки) на карте.
    public sealed class PointData
    {
        public string GameName = "";
        public string RealName = "";
        public string Category = "Пользовательское";
        public bool Enabled = true;
        public string Description = "";

        public double X, Y, Z;

        public string Color = "default";
        public string Icon = "default";
        public float LabelStroke = 1f;

        public double TriggerRadius = 200;
        public int CooldownMinutes = 0;
        public int Hidden = 0;
        public int DeleteOnComplete = 0;

        public string DialogId = "";
        public string Action = "";
        public string Caption = "";
        public int EnterReward = 0;
        public int AfterReward = 0;
        public int EnterXp = 0;
        public int AfterXp = 0;

        public bool IsRandom = false;
        public string QuestType = "";

        public bool IsNew;
        public bool IsOverride;
        public string SourceFile = "";
        public DateTime CooldownUntil = DateTime.MinValue;

        public bool IsCity;
        public bool IsPoi;
        public bool IsSdo;

        public PointData Clone() => (PointData)MemberwiseClone();

        public static readonly PointField[] Fields = new[]
        {
            new PointField { Key="GameName", Label="Системное имя (id)", Required=true, Group="Основное", ValueType=typeof(string) },
            new PointField { Key="RealName", Label="Отображаемое имя", Required=true, Group="Основное", ValueType=typeof(string) },
            new PointField { Key="Description", Label="Описание", Group="Основное", ValueType=typeof(string) },
            new PointField { Key="Category", Label="Категория", Group="Основное", ValueType=typeof(string) },
            new PointField { Key="Enabled", Label="Статус: включена", Group="Основное", ValueType=typeof(bool) },
            new PointField { Key="X", Label="Координата X", Required=true, Group="Координаты", ValueType=typeof(double) },
            new PointField { Key="Y", Label="Координата Y", Required=true, Group="Координаты", ValueType=typeof(double) },
            new PointField { Key="Z", Label="Координата Z", Required=true, Group="Координаты", ValueType=typeof(double) },
            new PointField { Key="Color", Label="Цвет (#rrggbb)", Group="Внешний вид", ValueType=typeof(string) },
            new PointField { Key="Icon", Label="Иконка", Group="Внешний вид", ValueType=typeof(string) },
            new PointField { Key="LabelStroke", Label="Толщина обводки названия (px)", Group="Внешний вид", ValueType=typeof(float) },
            new PointField { Key="TriggerRadius", Label="Радиус триггера (м)", Group="Триггер", ValueType=typeof(double) },
            new PointField { Key="CooldownMinutes", Label="Кулдаун (мин, 0=нет)", Group="Триггер", ValueType=typeof(int) },
            new PointField { Key="Hidden", Label="Скрытая (1/0)", Group="Триггер", ValueType=typeof(int) },
            new PointField { Key="DeleteOnComplete", Label="Удалить при выполнении (0/1/2)", Group="Триггер", ValueType=typeof(int) },
            new PointField { Key="DialogId", Label="Диалог (id)", Group="Диалог", ValueType=typeof(string) },
            new PointField { Key="Action", Label="Действие", Group="Диалог", ValueType=typeof(string) },
            new PointField { Key="Caption", Label="Подпись", Group="Диалог", ValueType=typeof(string) },
            new PointField { Key="EnterReward", Label="Награда при входе (р)", Group="Диалог", ValueType=typeof(int) },
            new PointField { Key="AfterReward", Label="Награда после (р)", Group="Диалог", ValueType=typeof(int) },
            new PointField { Key="EnterXp", Label="Опыт при входе", Group="Диалог", ValueType=typeof(int) },
            new PointField { Key="AfterXp", Label="Опыт после", Group="Диалог", ValueType=typeof(int) },
        };
    }
}