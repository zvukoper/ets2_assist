using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    // Диалог квеста с РАЗДЕЛЬНЫМИ контролами (просьба пользователя 30.08.2026):
    //   _messageLabel — обычный текст задания (шрифт/цвет по умолчанию);
    //   _rewardsTable — награды, по ОТДЕЛЬНОМУ Label на каждую строку,
    //       ЗЕЛЁНЫЙ ЖИРНЫЙ шрифт.
    // Награды передаются отдельным параметром rewards (каждая строка = свой контрол).
    internal sealed class QuestDialogForm : Form
    {
        private readonly Label _messageLabel;
        private readonly TableLayoutPanel _rewardsTable;
        private readonly Button _primaryButton;
        private readonly Button _secondaryButton;
        private readonly bool _isSuccess;

        private static readonly Color RewardColor = Color.FromArgb(0, 150, 40);
        private static readonly Font RewardFont =
            new Font("Segoe UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point);

        // Новый формат: message — обычный текст, rewards — строки наград (пустой
        // параметр — без блока наград). ЕДИНЫЙ конструктор: rewards — именованный.
        public QuestDialogForm(string title, string message, bool isSuccess,
            string primaryText = "", string secondaryText = "",
            IEnumerable<string>? rewards = null)
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

            // ---- ТЕКСТ ЗАДАНИЯ: ОТДЕЛЬНЫЙ КОНТРОЛ, обычный шрифт/цвет ----
            _messageLabel = new Label
            {
                Text = message,
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(20, 14, 20, 4),
                Font = new Font("Segoe UI", 10.5F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = SystemColors.ControlText
            };

            // ---- НАГРАДЫ: ОТДЕЛЬНЫЙ КОНТРОЛ, каждая строка = свой Label,
            //      ЗЕЛЁНЫЙ ЖИРНЫЙ (просьба пользователя) ----
            _rewardsTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoScroll = false,
                ColumnCount = 1,
                RowCount = 1,
                Padding = new Padding(20, 2, 20, 6),
                Margin = new Padding(0)
            };
            bool hasRewards = false;
            foreach (var r in rewards ?? (IEnumerable<string>)Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(r)) continue;
                hasRewards = true;
                var lbl = new Label
                {
                    Text = r,
                    AutoSize = true,
                    Font = (Font)RewardFont.Clone(),
                    ForeColor = RewardColor,
                    Margin = new Padding(0, 1, 0, 1)
                };
                _rewardsTable.Controls.Add(lbl, 0, _rewardsTable.RowCount - 1);
                _rewardsTable.RowCount++;
            }

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

            Controls.Add(buttons);
            if (hasRewards) Controls.Add(_rewardsTable);
            Controls.Add(_messageLabel);

            ClientSize = ComputeClientSize(message, hasRewards, isSuccess);

            Shown += (_, _) =>
            {
                TopMost = true;
                _primaryButton.Focus();
            };
        }

        // (params-конструктор убран: он конфликтовал с именованным rewards,
        //  перетягивая List<string> на позиционный secondaryText — CS1503.)

        // Подбирает высоту окна под фактический контент (текст + награды + кнопки).
        private Size ComputeClientSize(string message, bool hasRewards, bool isSuccess)
        {
            int width = isSuccess ? 430 : 500;
            try
            {
                using var g = CreateGraphics();
                int usable = width - 60;
                var textFont = _messageLabel.Font;
                int textLines = 1;
                foreach (var raw in (message ?? "").Split('\n'))
                {
                    var line = raw.TrimEnd('\r');
                    var size = g.MeasureString(string.IsNullOrEmpty(line) ? " " : line, textFont);
                    textLines += Math.Max(1, (int)Math.Ceiling(size.Width / usable));
                }
                int totalH = (int)(textLines * textFont.GetHeight(g)) + 40;

                if (hasRewards)
                {
                    int rewardLines = 0;
                    foreach (Control c in _rewardsTable.Controls)
                    {
                        if (c is Label l && !string.IsNullOrEmpty(l.Text))
                        {
                            var s = g.MeasureString(l.Text, RewardFont);
                            rewardLines += Math.Max(1, (int)Math.Ceiling(s.Width / usable));
                        }
                    }
                    totalH += (int)(rewardLines * RewardFont.GetHeight(g)) + 16;
                }

                totalH += 58 + 10;
                return new Size(width, Math.Max(isSuccess ? 160 : 180, totalH));
            }
            catch
            {
                return new Size(width, isSuccess ? 180 : 200);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Control c in _rewardsTable.Controls)
                {
                    if (c is Label l) l.Font?.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
