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
        private Control? _mapEditor2ButtonParent;

        private void PositionMapEditor2Button()
        {
            if (_btnMapEditor2 == null || btnMapEditor == null || _mapEditor2ButtonParent == null) return;
            var parent = _mapEditor2ButtonParent;
            int desiredY = btnMapEditor.Bottom + 6;
            int maxY = Math.Max(0, parent.ClientSize.Height - _btnMapEditor2.Height - 4);
            int y = Math.Min(desiredY, maxY);
            if (desiredY > maxY)
            {
                int aboveY = btnMapEditor.Top - _btnMapEditor2.Height - 6;
                if (aboveY >= 0) y = aboveY;
            }
            _btnMapEditor2.Location = new Point(btnMapEditor.Left, y);
            _btnMapEditor2.Visible = true;
            _btnMapEditor2.BringToFront();
        }

        private void AddMapEditor2Button()
        {
            if (_btnMapEditor2 != null || btnMapEditor == null) return;

            _btnMapEditor2 = new Button
            {
                Name = "btnMapEditor2",
                Text = "Редактор карты 2",
                Size = btnMapEditor.Size,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(166, 166, 166),
                BackColor = Color.FromArgb(60, 60, 60),
                UseVisualStyleBackColor = false,
                Visible = true,
                TabStop = true
            };
            _btnMapEditor2.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
            _btnMapEditor2.Click += (_, _) => OpenMapEditor2();

            var parent = btnMapEditor.Parent ?? this;
            _mapEditor2ButtonParent = parent;
            parent.Controls.Add(_btnMapEditor2);
            parent.Resize += (_, _) => PositionMapEditor2Button();
            PositionMapEditor2Button();
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
