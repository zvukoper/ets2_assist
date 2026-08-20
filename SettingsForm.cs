using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;

namespace ETS2_Assist_GUI
{
    public class SettingsForm : Form
    {
        private ComboBox languageCombo = null!;
        private CheckBox debugCheck = null!;
        private CheckBox autoStartCheck = null!;
        private CheckBox startMinimizedCheck = null!;
        private CheckBox checkUpdatesCheck = null!;

        // Новые элементы для записи треков
        private GroupBox recordingGroup = null!;
        private ComboBox recordModeCombo = null!;
        private NumericUpDown recordDurationNud = null!;
        private CheckBox autoSaveCheck = null!;
        private ComboBox saveFormatCombo = null!;
        private TextBox defaultSuffixTxt = null!;
        private TextBox defaultDescTxt = null!;

        private Button btnSave = null!;
        private Button btnCancel = null!;

        private LanguageManager lang = null!;

        public SettingsForm()
        {
            lang = LanguageManager.Instance;
            InitializeComponent();
            LoadSettings();
            ApplyLanguage();
        }

        private void InitializeComponent()
        {
            this.Text = "Settings";
            this.Size = new Size(500, 550);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int y = 20;
            int labelWidth = 150;
            int controlWidth = 200;
            int left = 20;

            // Language
            Label lblLang = new Label { Text = "Language:", Location = new Point(left, y), Size = new Size(labelWidth, 25) };
            languageCombo = new ComboBox { Location = new Point(left + labelWidth + 10, y), Size = new Size(controlWidth, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            y += 40;

            // Debug
            debugCheck = new CheckBox { Text = "Debug Mode (show script window)", Location = new Point(left, y), Size = new Size(controlWidth + labelWidth, 25) };
            y += 35;
            autoStartCheck = new CheckBox { Text = "Auto start system on launch", Location = new Point(left, y), Size = new Size(controlWidth + labelWidth, 25) };
            y += 35;
            startMinimizedCheck = new CheckBox { Text = "Start minimized to tray", Location = new Point(left, y), Size = new Size(controlWidth + labelWidth, 25) };
            y += 35;
            checkUpdatesCheck = new CheckBox { Text = "Check updates on start", Location = new Point(left, y), Size = new Size(controlWidth + labelWidth, 25) };
            y += 45;

            // ===== Группа настроек записи =====
            recordingGroup = new GroupBox { Text = "Запись треков", Location = new Point(left, y), Size = new Size(440, 220) };
            int gy = 20;
            int gl = 10;

            Label lblRecordMode = new Label { Text = "Режим записи:", Location = new Point(gl, gy), Size = new Size(100, 25) };
            recordModeCombo = new ComboBox { Location = new Point(gl + 110, gy), Size = new Size(150, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            recordModeCombo.Items.AddRange(new object[] { "Off", "Auto", "Manual", "TrailOnly" });
            gy += 35;

            Label lblDuration = new Label { Text = "Длительность (мин):", Location = new Point(gl, gy), Size = new Size(100, 25) };
            recordDurationNud = new NumericUpDown { Location = new Point(gl + 110, gy), Size = new Size(80, 25), Minimum = 1, Maximum = 1440, Value = 60 };
            gy += 35;

            autoSaveCheck = new CheckBox { Text = "Автосохранение по окончании", Location = new Point(gl, gy), Size = new Size(200, 25) };
            gy += 35;

            Label lblFormat = new Label { Text = "Формат сохранения:", Location = new Point(gl, gy), Size = new Size(100, 25) };
            saveFormatCombo = new ComboBox { Location = new Point(gl + 110, gy), Size = new Size(150, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            saveFormatCombo.Items.AddRange(new object[] { "OneFile", "TwoFiles", "ThreeFiles" });
            gy += 35;

            Label lblSuffix = new Label { Text = "Суффикс названия:", Location = new Point(gl, gy), Size = new Size(100, 25) };
            defaultSuffixTxt = new TextBox { Location = new Point(gl + 110, gy), Size = new Size(200, 25) };
            gy += 35;

            Label lblDesc = new Label { Text = "Описание по умолчанию:", Location = new Point(gl, gy), Size = new Size(100, 25) };
            defaultDescTxt = new TextBox { Location = new Point(gl + 110, gy), Size = new Size(200, 25) };

            recordingGroup.Controls.AddRange(new Control[] {
                lblRecordMode, recordModeCombo,
                lblDuration, recordDurationNud,
                autoSaveCheck,
                lblFormat, saveFormatCombo,
                lblSuffix, defaultSuffixTxt,
                lblDesc, defaultDescTxt
            });

            y += recordingGroup.Height + 20;

            // Buttons
            btnSave = new Button { Text = "Save", Location = new Point(150, y), Size = new Size(100, 30), DialogResult = DialogResult.OK };
            btnSave.Click += (s, e) => SaveSettings();

            btnCancel = new Button { Text = "Cancel", Location = new Point(270, y), Size = new Size(100, 30), DialogResult = DialogResult.Cancel };

            this.Controls.AddRange(new Control[] {
                lblLang, languageCombo,
                debugCheck, autoStartCheck, startMinimizedCheck, checkUpdatesCheck,
                recordingGroup,
                btnSave, btnCancel
            });
        }

        private void LoadSettings()
        {
            string langDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "language");
            if (Directory.Exists(langDir))
            {
                var files = Directory.GetFiles(langDir, "*.csv");
                foreach (var file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    languageCombo.Items.Add(name);
                    if (name == AppSettings.Language)
                        languageCombo.SelectedItem = name;
                }
            }
            if (languageCombo.SelectedIndex == -1 && languageCombo.Items.Count > 0)
                languageCombo.SelectedIndex = 0;

            debugCheck.Checked = AppSettings.DebugMode;
            autoStartCheck.Checked = AppSettings.AutoStartSystem;
            startMinimizedCheck.Checked = AppSettings.StartMinimized;
            checkUpdatesCheck.Checked = AppSettings.CheckUpdatesOnStart;

            // Загрузка настроек записи
            recordModeCombo.SelectedItem = AppSettings.RecordMode;
            recordDurationNud.Value = AppSettings.RecordDurationMinutes;
            autoSaveCheck.Checked = AppSettings.AutoSaveEnabled;
            saveFormatCombo.SelectedItem = AppSettings.SaveFormat;
            defaultSuffixTxt.Text = AppSettings.DefaultSuffix;
            defaultDescTxt.Text = AppSettings.DefaultDescription;
        }

        private void SaveSettings()
        {
            if (languageCombo.SelectedItem != null && languageCombo.SelectedItem.ToString() != AppSettings.Language)
            {
                AppSettings.Language = languageCombo.SelectedItem.ToString();
                lang.LoadLanguage(AppSettings.Language);
            }
            AppSettings.DebugMode = debugCheck.Checked;
            AppSettings.AutoStartSystem = autoStartCheck.Checked;
            AppSettings.StartMinimized = startMinimizedCheck.Checked;
            AppSettings.CheckUpdatesOnStart = checkUpdatesCheck.Checked;

            AppSettings.RecordMode = recordModeCombo.SelectedItem?.ToString() ?? "Auto";
            AppSettings.RecordDurationMinutes = (int)recordDurationNud.Value;
            AppSettings.AutoSaveEnabled = autoSaveCheck.Checked;
            AppSettings.SaveFormat = saveFormatCombo.SelectedItem?.ToString() ?? "ThreeFiles";
            AppSettings.DefaultSuffix = defaultSuffixTxt.Text.Trim();
            AppSettings.DefaultDescription = defaultDescTxt.Text.Trim();

            AppSettings.Save();

            MessageBox.Show(
                lang.Get("settings_saved") ?? "Settings saved. Some changes may take effect after restart.",
                lang.Get("settings_title") ?? "Settings",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private void ApplyLanguage()
        {
            this.Text = lang.Get("settings_title") ?? "Settings";
            btnSave.Text = lang.Get("settings_save") ?? "Save";
            btnCancel.Text = lang.Get("settings_cancel") ?? "Cancel";
            debugCheck.Text = lang.Get("settings_debug") ?? "Debug Mode (show script window)";
            autoStartCheck.Text = lang.Get("settings_autostart") ?? "Auto start system on launch";
            startMinimizedCheck.Text = lang.Get("settings_startminimized") ?? "Start minimized to tray";
            checkUpdatesCheck.Text = lang.Get("settings_checkupdates") ?? "Check updates on start";
            recordingGroup.Text = "Запись треков";
            Label lblLang = (Label)this.Controls[0];
            lblLang.Text = lang.Get("settings_language") ?? "Language:";
            // Остальные метки уже заданы на русском, можно оставить.
        }
    }
}