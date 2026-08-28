using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    internal sealed class QuestDialogForm : Form
    {
        private readonly RichTextBox _messageBox;
        private readonly Button _primaryButton;
        private readonly Button _secondaryButton;
        private readonly bool _isSuccess;

        public QuestDialogForm(string title, string message, bool isSuccess,
            string primaryText = "", string secondaryText = "")
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
            ClientSize = new Size(isSuccess ? 430 : 500, isSuccess ? 180 : 200);

            _messageBox = new RichTextBox
            {
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                BackColor = SystemColors.Control,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Regular, GraphicsUnit.Point),
                Text = message,
                DetectUrls = false,
                Margin = new Padding(20, 16, 20, 8)
            };
            ColorRewardLines(_messageBox, message);

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
                    Text = string.IsNullOrEmpty(primaryText) ? "OK" : primaryText,
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
                    Text = string.IsNullOrEmpty(primaryText) ? "Да" : primaryText,
                    DialogResult = DialogResult.Yes,
                    AutoSize = true,
                    MinimumSize = new Size(90, 30)
                };
                _secondaryButton = new Button
                {
                    Text = string.IsNullOrEmpty(secondaryText) ? "Нет" : secondaryText,
                    DialogResult = DialogResult.No,
                    AutoSize = true,
                    MinimumSize = new Size(90, 30)
                };

                AcceptButton = _primaryButton;
                CancelButton = _secondaryButton;
                buttons.Controls.Add(_secondaryButton);
                buttons.Controls.Add(_primaryButton);
            }

            Controls.Add(_messageBox);
            Controls.Add(buttons);

            Shown += (_, _) =>
            {
                TopMost = true;
                _primaryButton.Focus();
            };
        }

        // Подсвечивает строки с наградой жирным зелёным: +XXXXX руб. / +XXXXX опыта.
        private static void ColorRewardLines(RichTextBox box, string message)
        {
            try
            {
                var lines = message.Split('\n');
                for (int i = 0; i < lines.Length && i < box.Lines.Length; i++)
                {
                    if (IsRewardLine(lines[i]))
                    {
                        int start = box.GetFirstCharIndexFromLine(i);
                        int len = box.Lines[i].Length;
                        if (start < 0 || len <= 0) continue;
                        box.Select(start, len);
                        box.SelectionColor = Color.FromArgb(0, 150, 40);
                        box.SelectionFont = new Font(box.Font, FontStyle.Bold);
                    }
                }
                box.Select(0, 0);
            }
            catch { }
        }

        private static bool IsRewardLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            if (line.Contains("руб") || line.Contains("опыта") || line.Contains("опыт") || line.Contains("Награда"))
                return true;
            return Regex.IsMatch(line, @"[+\-]\s*\d");
        }
    }
}
