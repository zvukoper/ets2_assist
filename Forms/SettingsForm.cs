 
using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace ETS2_Assist_GUI.Forms
{
    /// <summary>
    /// Форма настроек приложения.
    /// Отвечает за отображение и сохранение параметров:
    /// - Режим записи треков
    /// - Длительность записи
    /// - Автосохранение
    /// - Формат файлов
    /// - Суффикс и описание
    /// - Хоткеи
    /// </summary>
    public partial class SettingsForm : Form
    {
        private readonly Storage.SettingsManager _settingsManager;

        // Элементы управления
        private ComboBox cmbRecordMode;
        private NumericUpDown nudDuration;
        private CheckBox chkAutoSave;
        private ComboBox cmbFormat;
        private TextBox txtSuffix;
        private TextBox txtDescription;
        private Button btnSave;
        private Button btnCancel;

        public SettingsForm()
        {
            _settingsManager = new Storage.SettingsManager();
            InitializeComponent();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            this.Text = "Настройки ETS2 Assist";
            this.Size = new Size(500, 500);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int y = 20;
            int labelWidth = 140;
            int controlWidth = 280;
            int step = 35;

            // Режим записи
            Label lblRecordMode = new Label { Text = "Режим записи:", Location = new Point(20, y), Size = new Size(labelWidth, 25) };
            cmbRecordMode = new ComboBox { Location = new Point(160, y), Size = new Size(controlWidth, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbRecordMode.Items.AddRange(new object[] { "Выкл", "Авто", "Вручную", "Только шлейф" });
            cmbRecordMode.SelectedIndex = 0;
            this.Controls.Add(lblRecordMode);
            this.Controls.Add(cmbRecordMode);
            y += step;

            // Длительность записи
            Label lblDuration = new Label { Text = "Длительность записи (мин):", Location = new Point(20, y), Size = new Size(labelWidth, 25) };
            nudDuration = new NumericUpDown { Location = new Point(160, y), Size = new Size(controlWidth, 25), Minimum = 0, Maximum = 1440, Value = 60, ThousandsSeparator = false };
            this.Controls.Add(lblDuration);
            this.Controls.Add(nudDuration);
            y += step;

            // Автосохранение
            chkAutoSave = new CheckBox { Text = "Автосохранение при достижении лимита", Location = new Point(20, y), Size = new Size(controlWidth + 50, 25) };
            this.Controls.Add(chkAutoSave);
            y += step;

            // Формат записи
            Label lblFormat = new Label { Text = "Формат записи:", Location = new Point(20, y), Size = new Size(labelWidth, 25) };
            cmbFormat = new ComboBox { Location = new Point(160, y), Size = new Size(controlWidth, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbFormat.Items.AddRange(new object[] { "1 файл (всё в HTML)", "2 файла (HTML + JSON трек)", "3 файла (HTML + JSON трек + JSON карта)" });
            cmbFormat.SelectedIndex = 0;
            this.Controls.Add(lblFormat);
            this.Controls.Add(cmbFormat);
            y += step;

            // Суффикс
            Label lblSuffix = new Label { Text = "Суффикс названия:", Location = new Point(20, y), Size = new Size(labelWidth, 25) };
            txtSuffix = new TextBox { Location = new Point(160, y), Size = new Size(controlWidth, 25) };
            this.Controls.Add(lblSuffix);
            this.Controls.Add(txtSuffix);
            y += step;

            // Описание
            Label lblDescription = new Label { Text = "Описание по умолчанию:", Location = new Point(20, y), Size = new Size(labelWidth, 25) };
            txtDescription = new TextBox { Location = new Point(160, y), Size = new Size(controlWidth, 25) };
            this.Controls.Add(lblDescription);
            this.Controls.Add(txtDescription);
            y += step;

            // Кнопки
            btnSave = new Button { Text = "Сохранить", Location = new Point(100, y + 20), Size = new Size(120, 30), DialogResult = DialogResult.OK };
            btnCancel = new Button { Text = "Отмена", Location = new Point(240, y + 20), Size = new Size(120, 30), DialogResult = DialogResult.Cancel };
            this.Controls.Add(btnSave);
            this.Controls.Add(btnCancel);

            btnSave.Click += BtnSave_Click;
        }

        private void LoadSettings()
        {
            var settings = _settingsManager.Load();
            cmbRecordMode.SelectedIndex = settings.RecordMode;
            nudDuration.Value = settings.RecordDurationMinutes;
            chkAutoSave.Checked = settings.AutoSaveEnabled;
            cmbFormat.SelectedIndex = settings.FormatMode - 1; // 1..3 -> 0..2
            txtSuffix.Text = settings.DefaultSuffix ?? "";
            txtDescription.Text = settings.DefaultDescription ?? "";
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            var settings = new Models.Settings
            {
                RecordMode = cmbRecordMode.SelectedIndex,
                RecordDurationMinutes = (int)nudDuration.Value,
                AutoSaveEnabled = chkAutoSave.Checked,
                FormatMode = cmbFormat.SelectedIndex + 1,
                DefaultSuffix = txtSuffix.Text.Trim(),
                DefaultDescription = txtDescription.Text.Trim()
            };
            _settingsManager.Save(settings);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}