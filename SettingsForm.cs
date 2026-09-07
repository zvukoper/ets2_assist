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
            this.Size = new Size(420, 350);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int y = 20;
            int labelWidth = 150;
            int controlWidth = 200;
            int left = 30;

            Label lblLang = new Label
            {
                Text = "Language:",
                Location = new Point(left, y),
                Size = new Size(labelWidth, 25)
            };
            languageCombo = new ComboBox
            {
                Location = new Point(left + labelWidth + 10, y),
                Size = new Size(controlWidth, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            y += 40;

            debugCheck = new CheckBox
            {
                Text = "Debug Mode (show script window)",
                Location = new Point(left, y),
                Size = new Size(controlWidth + labelWidth, 25)
            };
            y += 35;

            autoStartCheck = new CheckBox
            {
                Text = "Auto start system on launch",
                Location = new Point(left, y),
                Size = new Size(controlWidth + labelWidth, 25)
            };
            y += 35;

            startMinimizedCheck = new CheckBox
            {
                Text = "Start minimized to tray",
                Location = new Point(left, y),
                Size = new Size(controlWidth + labelWidth, 25)
            };
            y += 35;

            checkUpdatesCheck = new CheckBox
            {
                Text = "Check updates on start",
                Location = new Point(left, y),
                Size = new Size(controlWidth + labelWidth, 25)
            };
            y += 45;

            btnSave = new Button
            {
                Text = "Save",
                Location = new Point(130, y),
                Size = new Size(100, 30),
                DialogResult = DialogResult.OK
            };
            btnSave.Click += (s, e) => SaveSettings();

            btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(250, y),
                Size = new Size(100, 30),
                DialogResult = DialogResult.Cancel
            };

            this.Controls.AddRange(new Control[] {
                lblLang, languageCombo,
                debugCheck, autoStartCheck, startMinimizedCheck, checkUpdatesCheck,
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
        }

        private void SaveSettings()
        {
            string? selectedLanguage = languageCombo.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(selectedLanguage) && selectedLanguage != AppSettings.Language)
            {
                AppSettings.Language = selectedLanguage;
                lang.LoadLanguage(selectedLanguage);
            }
            AppSettings.DebugMode = debugCheck.Checked;
            AppSettings.AutoStartSystem = autoStartCheck.Checked;
            AppSettings.StartMinimized = startMinimizedCheck.Checked;
            AppSettings.CheckUpdatesOnStart = checkUpdatesCheck.Checked;
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
            Label lblLang = (Label)this.Controls[0];
            lblLang.Text = lang.Get("settings_language") ?? "Language:";
        }
    }
}