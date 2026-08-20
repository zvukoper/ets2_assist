using System;
using System.Drawing;
using System.Windows.Forms;

namespace ETS2_Assist_GUI.Forms
{
    /// <summary>
    /// Диалог для добавления пользовательской заметки на карту.
    /// Позволяет ввести название и описание.
    /// </summary>
    public partial class MarkerDialog : Form
    {
        private TextBox txtName;
        private TextBox txtDescription;
        private Button btnOk;
        private Button btnCancel;

        public string MarkerName { get; private set; } = "";
        public string MarkerDescription { get; private set; } = "";

        public MarkerDialog(string defaultName = "", string defaultDesc = "")
        {
            InitializeComponent();
            txtName.Text = defaultName;
            txtDescription.Text = defaultDesc;
        }

        private void InitializeComponent()
        {
            this.Text = "Добавить заметку";
            this.Size = new Size(400, 220);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int y = 20;
            int labelWidth = 80;
            int controlWidth = 280;

            Label lblName = new Label { Text = "Название:", Location = new Point(10, y), Size = new Size(labelWidth, 25) };
            txtName = new TextBox { Location = new Point(100, y), Size = new Size(controlWidth, 25) };
            this.Controls.Add(lblName);
            this.Controls.Add(txtName);
            y += 35;

            Label lblDesc = new Label { Text = "Описание:", Location = new Point(10, y), Size = new Size(labelWidth, 25) };
            txtDescription = new TextBox { Location = new Point(100, y), Size = new Size(controlWidth, 60), Multiline = true, ScrollBars = ScrollBars.Vertical };
            this.Controls.Add(lblDesc);
            this.Controls.Add(txtDescription);
            y += 70;

            btnOk = new Button { Text = "OK", Location = new Point(80, y + 10), Size = new Size(100, 30), DialogResult = DialogResult.OK };
            btnCancel = new Button { Text = "Отмена", Location = new Point(200, y + 10), Size = new Size(100, 30), DialogResult = DialogResult.Cancel };
            this.Controls.Add(btnOk);
            this.Controls.Add(btnCancel);

            btnOk.Click += (s, e) => {
                MarkerName = txtName.Text.Trim();
                MarkerDescription = txtDescription.Text.Trim();
            };
        }
    }
}