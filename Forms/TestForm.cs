 
using System;
using System.Drawing;
using System.Windows.Forms;

namespace ETS2_Assist_GUI.Forms
{
    /// <summary>
    /// Тестовое окно для отладки.
    /// Отображается по хоткею Shift+Ctrl+T (если настроено).
    /// Содержит кнопки для тестирования различных функций.
    /// Запоминает свои размер и положение.
    /// </summary>
    public partial class TestForm : Form
    {
        private Button btnTestSound;
        private Button btnTestTrail;
        private Button btnTestMarker;
        private RichTextBox txtLog;
        private static Point? _lastLocation;
        private static Size? _lastSize;

        public TestForm()
        {
            InitializeComponent();
            // Восстановление положения и размера
            if (_lastLocation.HasValue) this.Location = _lastLocation.Value;
            if (_lastSize.HasValue) this.Size = _lastSize.Value;
        }

        private void InitializeComponent()
        {
            this.Text = "Тестовое окно ETS2 Assist";
            this.Size = new Size(400, 300);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;

            btnTestSound = new Button { Text = "Play Sound", Location = new Point(20, 20), Size = new Size(120, 30) };
            btnTestTrail = new Button { Text = "Test Trail", Location = new Point(150, 20), Size = new Size(120, 30) };
            btnTestMarker = new Button { Text = "Add Marker", Location = new Point(280, 20), Size = new Size(120, 30) };
            txtLog = new RichTextBox { Location = new Point(20, 70), Size = new Size(350, 180), ReadOnly = true, BackColor = Color.Black, ForeColor = Color.LightGray, Font = new Font("Consolas", 9) };

            this.Controls.Add(btnTestSound);
            this.Controls.Add(btnTestTrail);
            this.Controls.Add(btnTestMarker);
            this.Controls.Add(txtLog);

            btnTestSound.Click += (s, e) => AppendLog("🔊 Sound played (simulated)");
            btnTestTrail.Click += (s, e) => AppendLog("📊 Test trail data generated");
            btnTestMarker.Click += (s, e) => AppendLog("📍 Marker added (simulated)");

            this.FormClosing += (s, e) => {
                _lastLocation = this.Location;
                _lastSize = this.Size;
            };
        }

        private void AppendLog(string msg)
        {
            if (txtLog.InvokeRequired) {
                txtLog.Invoke(new Action(() => AppendLog(msg)));
                return;
            }
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            txtLog.ScrollToCaret();
        }
    }
}