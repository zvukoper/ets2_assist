using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ETS2_Assist_GUI.Quests
{
    internal sealed class QuestSettingsForm : Form
    {
        private readonly QuestSettings _settings;
        private readonly Action _apply;

        private CheckBox _enabled = null!;
        private CheckBox _debug = null!;
        private NumericUpDown _debugRadius = null!;
        private NumericUpDown _triggerRadius = null!;
        private NumericUpDown _nearDistance = null!;
        private NumericUpDown _farDistance = null!;
        private NumericUpDown _fadeStart = null!;
        private NumericUpDown _fadeEnd = null!;
        private NumericUpDown _maxSize = null!;
        private NumericUpDown _minSize = null!;
        private NumericUpDown _nearOutline = null!;
        private NumericUpDown _farOutline = null!;
        private NumericUpDown _notifyWidth = null!;
        private NumericUpDown _notifyHeight = null!;

        public QuestSettingsForm(QuestSettings settings, Action apply)
        {
            _settings = settings;
            _apply = apply;
            Text = "Квесты — настройки видимости";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Width = 530;
            Height = 650;
            BackColor = Color.FromArgb(24, 28, 35);
            ForeColor = Color.FromArgb(225, 231, 238);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14),
                ColumnCount = 2,
                RowCount = 0,
                AutoScroll = true,
                BackColor = BackColor
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            Controls.Add(root);

            _enabled = AddCheck(root, "Система квестов включена", _settings.Enabled);
            _debug = AddCheck(root, "Отладка: показывать все точки в радиусе", _settings.DebugShowAllPoints);
            _debugRadius = AddNumber(root, "Радиус отладки, м", _settings.DebugRadiusM, 1, 10000, 1);
            _triggerRadius = AddNumber(root, "Радиус триггера интерактива, м", _settings.TriggerRadiusM, 1, 500, 1);
            AddSeparator(root, "AR — обычная точка");
            _nearDistance = AddNumber(root, "Максимальный размер до, м", _settings.ArSizeMaxDistanceM, 1, 5000, 1);
            _farDistance = AddNumber(root, "Минимальный размер от, м", _settings.ArSizeMinDistanceM, 1, 10000, 1);
            _fadeStart = AddNumber(root, "Начало затухания, м", _settings.ArFadeStartDistanceM, 1, 10000, 1);
            _fadeEnd = AddNumber(root, "Полная прозрачность, м", _settings.ArFadeEndDistanceM, 1, 20000, 1);
            _maxSize = AddNumber(root, "Максимальный размер точки, px", _settings.ArMaxPointSizePx, 1, 100, 0.5m);
            _minSize = AddNumber(root, "Минимальный размер точки, px", _settings.ArMinPointSizePx, 1, 50, 0.5m);
            _nearOutline = AddNumber(root, "Обводка вблизи, px", _settings.ArNearOutlinePx, 0.5m, 10, 0.5m);
            _farOutline = AddNumber(root, "Обводка вдали, px", _settings.ArFarOutlinePx, 0.5m, 10, 0.5m);
            AddSeparator(root, "Уведомление");
            _notifyWidth = AddNumber(root, "Ширина, % экрана", _settings.NotificationWidthPercent, 5, 90, 0.1m);
            _notifyHeight = AddNumber(root, "Высота, % экрана", _settings.NotificationHeightPercent, 1, 20, 0.1m);

            var hint = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Text = "Текущие пороги AR из проекта: 10 м → максимальный размер, 500 м → минимальный размер, 1500 м → полное затухание. Для квестовых маркеров размеры по умолчанию 10×3 px.",
                ForeColor = Color.FromArgb(150, 165, 185),
                Padding = new Padding(0, 10, 0, 8)
            };
            root.Controls.Add(hint, 0, root.RowCount);
            root.SetColumnSpan(hint, 2);
            root.RowCount++;

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            var ok = new Button { Text = "Применить", AutoSize = true, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => { ReadValues(); _apply(); };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            root.Controls.Add(buttons, 0, root.RowCount);
            root.SetColumnSpan(buttons, 2);
            root.RowCount++;
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private CheckBox AddCheck(TableLayoutPanel root, string text, bool value)
        {
            var cb = new CheckBox { Text = text, Checked = value, AutoSize = true, ForeColor = ForeColor, Margin = new Padding(0, 4, 0, 4) };
            root.Controls.Add(cb, 0, root.RowCount);
            root.SetColumnSpan(cb, 2);
            root.RowCount++;
            return cb;
        }

        private NumericUpDown AddNumber(TableLayoutPanel root, string text, double value, decimal min, decimal max, decimal increment)
        {
            var label = new Label { Text = text, AutoSize = true, ForeColor = Color.FromArgb(190, 200, 212), Anchor = AnchorStyles.Left, Margin = new Padding(0, 5, 0, 5) };
            var num = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Increment = increment,
                DecimalPlaces = increment < 1 ? 1 : 0,
                Value = Clamp(value, (double)min, (double)max),
                Width = 120,
                Anchor = AnchorStyles.Right,
                BackColor = Color.FromArgb(32, 39, 48),
                ForeColor = Color.White,
                Margin = new Padding(0, 3, 0, 3)
            };
            root.Controls.Add(label, 0, root.RowCount);
            root.Controls.Add(num, 1, root.RowCount);
            root.RowCount++;
            return num;
        }

        private void AddSeparator(TableLayoutPanel root, string text)
        {
            var label = new Label { Text = text, AutoSize = true, ForeColor = Color.FromArgb(255, 204, 80), Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 10, 0, 4) };
            root.Controls.Add(label, 0, root.RowCount);
            root.SetColumnSpan(label, 2);
            root.RowCount++;
        }

        private void ReadValues()
        {
            _settings.Enabled = _enabled.Checked;
            _settings.DebugShowAllPoints = _debug.Checked;
            _settings.DebugRadiusM = (double)_debugRadius.Value;
            _settings.TriggerRadiusM = (double)_triggerRadius.Value;
            _settings.ArSizeMaxDistanceM = (double)_nearDistance.Value;
            _settings.ArSizeMinDistanceM = (double)_farDistance.Value;
            _settings.ArFadeStartDistanceM = (double)_fadeStart.Value;
            _settings.ArFadeEndDistanceM = (double)_fadeEnd.Value;
            _settings.ArMaxPointSizePx = (double)_maxSize.Value;
            _settings.ArMinPointSizePx = (double)_minSize.Value;
            _settings.ArNearOutlinePx = (double)_nearOutline.Value;
            _settings.ArFarOutlinePx = (double)_farOutline.Value;
            _settings.NotificationWidthPercent = (double)_notifyWidth.Value;
            _settings.NotificationHeightPercent = (double)_notifyHeight.Value;
        }

        private static decimal Clamp(double value, double min, double max) =>
            (decimal)Math.Max(min, Math.Min(max, value));
    }
}