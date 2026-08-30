using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ETS2_Assist_GUI
{
    // Наложение map_overrides на миникарту. РЕАЛИЗАЦИЯ ПЕРЕНЕСЕНА в
    // MainForm.OverridesPipeline.cs — единый конвейер: приложение собирает
    // effective-данные (статика городов/POI + delta-merge overrides по load_order
    // + цели из map_overrides\test_targets.json) и рассылает ГОТОВЫЙ пакет
    // map_overrides_data. Миникарта не считает merge сама — только принимает пакет,
    // заменяет списки точек и перерисовывает (dumb-receiver).
    //
    // Этот файл оставляет публичные точки входа:
    //   - event PointsOverridesChanged / NotifyPointsOverridesChanged (редактор карты);
    //   - SendPointsOverridesToMap() (совместимость: теперь пересылает полный пакет).
    public partial class MainForm
    {
        // Редактор вызывает после успешного сохранения/удаления/добавления точки.
        public static event Action? PointsOverridesChanged;
        internal static void NotifyPointsOverridesChanged() => PointsOverridesChanged?.Invoke();

        private System.Windows.Forms.Timer? _pointsOverridesDebounce;

        private void HookPointsOverridesChanged(bool subscribe)
        {
            if (subscribe) PointsOverridesChanged += OnPointsOverridesChanged;
            else PointsOverridesChanged -= OnPointsOverridesChanged;
        }

        private void OnPointsOverridesChanged()
        {
            // Debounce: серия сохранений (например, перетаскивание) — одна рассылка.
            // Отправку делает новый конвейер: команда map_overrides_data.
            if (_pointsOverridesDebounce == null)
            {
                _pointsOverridesDebounce = new System.Windows.Forms.Timer { Interval = 400 };
                _pointsOverridesDebounce.Tick += (s, e) => { _pointsOverridesDebounce!.Stop(); SendMapOverridesToMap("overrides-changed"); };
            }
            _pointsOverridesDebounce.Stop();
            _pointsOverridesDebounce.Start();
        }

        internal void SendPointsOverridesToMap()
        {
            // Переадресация на единый конвейер (MainForm.OverridesPipeline.cs).
            SendMapOverridesToMap("points_overrides");
        }
    }
}