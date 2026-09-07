using System;
using System.Drawing;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    public partial class MainForm
    {
        // Field initializer registers the hook before InitializeComponents(); the Load
        // event itself fires only after all controls have been created.
        private readonly bool _mapEditor2LoadHook = RegisterMapEditor2LoadHook();
        private Button? _btnMapEditor2;

        private bool RegisterMapEditor2LoadHook()
        {
            Load += MainForm_MapEditor2Load;
            return true;
        }

        private void MainForm_MapEditor2Load(object? sender, EventArgs e)
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
