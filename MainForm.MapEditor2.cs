using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    public partial class MainForm
    {
        static MainForm()
        {
            Application.Idle += MainForm_ApplicationIdle;
        }

        private static void MainForm_ApplicationIdle(object? sender, EventArgs e)
        {
            var form = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
            if (form == null || form.IsDisposed || form.Disposing || form.btnMapEditor == null)
                return;

            Application.Idle -= MainForm_ApplicationIdle;
            form.AddMapEditor2Button();
        }

        private Button? _btnMapEditor2;

        private void AddMapEditor2Button()
        {
            if (_btnMapEditor2 != null || btnMapEditor == null)
                return;

            _btnMapEditor2 = new Button
            {
                Name = "btnMapEditor2",
                Text = "Редактор карты 2",
                Size = btnMapEditor.Size,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(166, 166, 166),
                BackColor = Color.FromArgb(60, 60, 60),
                UseVisualStyleBackColor = false
            };
            _btnMapEditor2.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
            _btnMapEditor2.Click += (_, _) => OpenMapEditor2();

            var parent = btnMapEditor.Parent ?? this;
            parent.Controls.Add(_btnMapEditor2);

            var desired = new Point(btnMapEditor.Left, btnMapEditor.Bottom + 6);
            var maxY = parent.ClientSize.Height - _btnMapEditor2.Height - 4;
            _btnMapEditor2.Location = new Point(desired.X, Math.Min(desired.Y, Math.Max(0, maxY)));
            _btnMapEditor2.BringToFront();
        }

        private void OpenMapEditor2()
        {
            try
            {
                using var form = new MapEditor2Form();
                form.ShowDialog(this);
            }
            catch (Exception ex)
            {
                AppendLog($"Map Editor 2 failed to open: {ex.Message}");
            }
        }
    }
}
