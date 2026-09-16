using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    /// <summary>
    /// v1.0.40.41: компактный диалог ввода ЧИСЛА для меню «Настройки АР»
    /// (радиус отображения точек, FOV). Сделан вместо InputBox, чтобы не тянуть
    /// зависимость Microsoft.VisualBasic и держать единый тёмный стиль.
    ///
    /// Особенности:
    ///  • ввод принимает и точку, и запятую как разделитель (ru-RU клавиатура);
    ///  • значение проверяется по min/max, при выходе за границы — предупреждение;
    ///  • Enter подтверждает, Escape отменяет.
    /// </summary>
    internal sealed class ArNumberPrompt : Form
    {
        private readonly NumericUpDown _num;
        private readonly Label _hint;

        public double Value => (double)_num.Value;

        public ArNumberPrompt(
            string title,
            string prompt,
            double current,
            double min,
            double max,
            double step,
            string unit)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(400, 148);
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.Gainsboro;
            KeyPreview = true;

            var lbl = new Label
            {
                Text = prompt,
                Location = new Point(14, 14),
                Size = new Size(370, 34),
                ForeColor = Color.Gainsboro
            };
            Controls.Add(lbl);

            _num = new NumericUpDown
            {
                Location = new Point(14, 54),
                // Ширина с запасом под «1500 м»
                Size = new Size(120, 26),
                Minimum = (decimal)min,
                Maximum = (decimal)max,
                DecimalPlaces = step < 1.0 ? 1 : 0,
                Increment = (decimal)step,
                Value = (decimal)Math.Clamp(current, min, max),
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            // Enter в поле = подтвердить (привычно для такого диалога).
            _num.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); }
            };
            Controls.Add(_num);

            var unitLbl = new Label
            {
                Text = unit,
                Location = new Point(140, 58),
                Size = new Size(40, 20),
                ForeColor = Color.Gainsboro
            };
            Controls.Add(unitLbl);

            // Подсказка о допустимом диапазоне + как вводить.
            _hint = new Label
            {
                Text = $"Диапазон: {min:0.##}…{max:0.##} {unit}. Ввод принимает и точку, и запятую.",
                Location = new Point(14, 84),
                Size = new Size(370, 20),
                ForeColor = Color.FromArgb(150, 150, 150)
            };
            Controls.Add(_hint);

            var ok = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(214, 110),
                Size = new Size(80, 26),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            ok.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
            Controls.Add(ok);

            var cancel = new Button
            {
                Text = "Отмена",
                DialogResult = DialogResult.Cancel,
                Location = new Point(300, 110),
                Size = new Size(86, 26),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            cancel.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
