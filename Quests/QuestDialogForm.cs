using System;
using System.Drawing;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    internal sealed class QuestDialogForm : Form
    {
        private readonly Label _messageLabel;
        private readonly Button _primaryButton;
        private readonly Button _secondaryButton;
        private readonly bool _isSuccess;

        public QuestDialogForm(string title, string message, bool isSuccess)
        {
            _isSuccess = isSuccess;

            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = true;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            ClientSize = new Size(isSuccess ? 430 : 500, isSuccess ? 170 : 190);

            _messageLabel = new Label
            {
                AutoSize = false,
                Text = message,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 18, 20, 10)
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(12, 8, 12, 10),
                WrapContents = false
            };

            if (isSuccess)
            {
                _primaryButton = new Button
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    AutoSize = true,
                    MinimumSize = new Size(90, 30)
                };
                _secondaryButton = new Button { Visible = false };
                AcceptButton = _primaryButton;
                CancelButton = _primaryButton;
                buttons.Controls.Add(_primaryButton);
            }
            else
            {
                _primaryButton = new Button
                {
                    Text = "Да",
                    DialogResult = DialogResult.Yes,
                    AutoSize = true,
                    MinimumSize = new Size(90, 30)
                };
                _secondaryButton = new Button
                {
                    Text = "Нет",
                    DialogResult = DialogResult.No,
                    AutoSize = true,
                    MinimumSize = new Size(90, 30)
                };

                AcceptButton = _primaryButton;
                CancelButton = _secondaryButton;
                buttons.Controls.Add(_secondaryButton);
                buttons.Controls.Add(_primaryButton);
            }

            Controls.Add(_messageLabel);
            Controls.Add(buttons);

            Shown += (_, _) =>
            {
                TopMost = true;
                _primaryButton.Focus();
            };
        }
    }
}
