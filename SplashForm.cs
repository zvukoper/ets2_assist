using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    public partial class SplashForm : Form
    {
        private PictureBox pictureBox;
        private Timer animationTimer;
        private int phase = 0; // 0 - fade-in, 1 - пауза, 2 - fade-out
        private const double Step = 0.05;
        private int stepCounter = 0;
        private int originalWidth, originalHeight;

        public SplashForm()
        {
            // Настройка формы – прозрачный фон, без перекрытия панели задач
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Normal;
            this.BackColor = Color.White;
            this.TransparencyKey = Color.White; // белый фон становится прозрачным
            this.TopMost = false; // не перекрываем панель задач
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Bounds = Screen.PrimaryScreen.Bounds; // занимаем весь экран, но не в полноэкранном режиме
            this.Opacity = 0; // начинаем с прозрачности

            pictureBox = new PictureBox();
            pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox.BackColor = Color.Transparent;

            string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "ets2a_logo.png");
            Image logoImage;
            if (File.Exists(logoPath))
            {
                logoImage = Image.FromFile(logoPath);
            }
            else
            {
                // Если файла нет – создаём заглушку
                originalWidth = 400;
                originalHeight = 200;
                var bmp = new Bitmap(originalWidth, originalHeight);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.DrawString("ETS2 Assist", new Font("Arial", 24, FontStyle.Bold), Brushes.White, new RectangleF(0, 0, originalWidth, originalHeight));
                }
                logoImage = bmp;
            }

            originalWidth = logoImage.Width;
            originalHeight = logoImage.Height;
            pictureBox.Image = logoImage;
            pictureBox.Size = new Size(originalWidth, originalHeight);
            pictureBox.Location = new Point((this.Width - originalWidth) / 2, (this.Height - originalHeight) / 2);
            this.Controls.Add(pictureBox);

            animationTimer = new Timer();
            animationTimer.Interval = 30; // ~33 fps
            animationTimer.Tick += AnimationTick;
            animationTimer.Start();
        }

        private void AnimationTick(object sender, EventArgs e)
        {
            if (phase == 0) // Fade-in
            {
                stepCounter++;
                double newOpacity = stepCounter * Step;
                if (newOpacity >= 1.0)
                {
                    newOpacity = 1.0;
                    phase = 1;
                    stepCounter = 0;
                    // Пауза 3 секунды – меняем интервал таймера для паузы
                    animationTimer.Interval = 3000;
                    // При следующем тике перейдём к fade-out
                }
                this.Opacity = newOpacity;
            }
            else if (phase == 1) // Пауза
            {
                phase = 2;
                animationTimer.Interval = 30;
                stepCounter = 0;
            }
            else if (phase == 2) // Fade-out
            {
                stepCounter++;
                double newOpacity = 1.0 - stepCounter * Step;
                if (newOpacity <= 0)
                {
                    newOpacity = 0;
                    animationTimer.Stop();
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                this.Opacity = newOpacity;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
        }
    }
}