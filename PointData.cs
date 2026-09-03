using System;
using System.Collections.Generic;

namespace ETS2_Assist_GUI
{
    // Режим отображения поля в панели редактирования точки.
    public enum PointFieldMode
    {
        Editable,  // обычное редактируемое поле
        ReadOnly,  // показываем, но не даём менять (устаревшее, но нужное для совместимости)
        Hidden     // не показываем вовсе (OFF — окончательно устаревшее)
    }

    // Описание одного редактируемого поля точки. Используется панелью
    // редактирования для генерации контролов в нужном порядке/группах.
    public sealed class PointField
    {
        public string Key;            // имя свойства в PointData
        public string Label;         // подпись на русском
        public bool Required;        // обязательное (блокирует сохранение, если пусто)
        public PointFieldMode Mode = PointFieldMode.Editable;
        public string Group = "Основное";
        public Type ValueType = typeof(string); // string / double / int / bool
    }

    // Единая модель данных точки (цели/пользовательской точки) на карте.
    // Все свойства сохраняются в overrides (формат совместим с custom_targets.json,
    // плюс расширения: category, enabled, ...). НИКОГДА не удаляй свойства —
    // для обратной совместимости; устаревшие помечай Mode=Hidden/ReadOnly в Fields.
    public sealed class PointData
    {
        // --- Идентификация ---
        public string GameName = "";      // системный id (обязательно)
        public string RealName = "";      // отображаемое имя (обязательно)
        public string Category = "Пользовательское";
        public bool Enabled = true;       // статус Включена/Отключена
        public string Description = "";   // многострочное описание (textarea)

        // --- Координаты (обязательны) ---
        public double X, Y, Z;

        // --- Внешний вид ---
        public string Color = "default"; // #rrggbb или named
        public string Icon = "default";

        // --- Триггер / зона ---
        public double TriggerRadius = 200;   // радиус триггера, м
        public int CooldownMinutes = 0;       // кулдаун, мин (0 = нет)
        public int Hidden = 0;                // 0/1 — невидима на карте, но триггер активен
        public int DeleteOnComplete = 0;      // 0 оставить / 1 удалить / 2 пересоздать

        // --- Диалог / действие ---
        public string DialogId = "";          // enterDialog
        public string Action = "";
        public string Caption = "";
        public int EnterReward = 0;           // награда при входе, р
        public int AfterReward = 0;           // награда после, р
        public int EnterXp = 0;
        public int AfterXp = 0;

        // --- Служебные (не редактируются в панели) ---
        public bool IsRandom = false;
        public string QuestType = "";

        // --- Только в редакторе (НЕ сохраняются в overrides) ---
        public bool IsNew;        // создана в редакторе, ещё не записана в файл
        public bool IsOverride;   // переопределяет статическую точку
        public string SourceFile = ""; // файл overrides, которому принадлежит
        public DateTime CooldownUntil = DateTime.MinValue; // для отображения кулдауна в редакторе

        // --- Тип точки (только в редакторе, НЕ сохраняется в overrides) ---
        public bool IsCity;   // точка — город (из localized_cities)
        public bool IsPoi;    // точка — POI (из оверлеев)
        public bool IsSdo;    // точка — SDO (Static Data Objects, выгрузка редактора игры)

        // Поверхностная копия (все поля — значимые/строковые) для отмены/сравнения в редакторе.
        public PointData Clone() => (PointData)MemberwiseClone();

        // Список полей для панели редактирования (порядок = порядок в UI).
        // Устаревшие свойства помечаем Mode=Hidden/ReadOnly, но НЕ удаляем.
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
