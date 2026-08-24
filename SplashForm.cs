using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    public partial class SplashForm : Form
    {
        private PictureBox pictureBox;
        private Timer animationTimer;
        private int phase = 0;
        private const double Step = 0.05;
        private int stepCounter = 0;

        public SplashForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Normal;
            this.BackColor = Color.Black;
            this.TransparencyKey = Color.Black;
            this.TopMost = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            // Загружаем изображение
            string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "ets2a_logo.png");
            Image logoImage;
            if (File.Exists(logoPath))
            {
                logoImage = Image.FromFile(logoPath);
            }
            else
            {
                // Заглушка, если файла нет
                var bmp = new Bitmap(400, 200);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.DrawString("ETS2 Assist", new Font("Arial", 24, FontStyle.Bold), Brushes.White, new RectangleF(0, 0, 400, 200));
                }
                logoImage = bmp;
            }

            // Определяем размеры экрана
            var screen = Screen.PrimaryScreen.Bounds;
            int screenWidth = screen.Width;
            int screenHeight = screen.Height;

            // Вычисляем размер для отображения картинки с сохранением пропорций
            int imgWidth = logoImage.Width;
            int imgHeight = logoImage.Height;

            // Максимальный размер с отступами (например, 90% от экрана)
            int maxWidth = (int)(screenWidth * 0.9);
            int maxHeight = (int)(screenHeight * 0.9);

            double scale = 1.0;
            if (imgWidth > maxWidth || imgHeight > maxHeight)
            {
                double scaleX = (double)maxWidth / imgWidth;
                double scaleY = (double)maxHeight / imgHeight;
                scale = Math.Min(scaleX, scaleY);
            }

            int displayWidth = (int)(imgWidth * scale);
            int displayHeight = (int)(imgHeight * scale);

            // Форма занимает весь экран
            this.Size = new Size(screenWidth, screenHeight);
            this.Location = new Point(0, 0);

            // Создаём pictureBox и центрируем его
            pictureBox = new PictureBox();
            pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox.BackColor = Color.Transparent;
            pictureBox.Image = logoImage;
            pictureBox.Size = new Size(displayWidth, displayHeight);
            pictureBox.Location = new Point((screenWidth - displayWidth) / 2, (screenHeight - displayHeight) / 2);
            this.Controls.Add(pictureBox);

            // Улучшенный рендеринг
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);
            this.Paint += SplashForm_Paint;

            animationTimer = new Timer();
            animationTimer.Interval = 30;
            animationTimer.Tick += AnimationTick;
            animationTimer.Start();
        }

        private void SplashForm_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.CompositingMode = CompositingMode.SourceOver;
            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
        }

        private void AnimationTick(object sender, EventArgs e)
        {
            if (phase == 0)
            {
                stepCounter++;
                double newOpacity = stepCounter * Step;
                if (newOpacity >= 1.0)
                {
                    newOpacity = 1.0;
                    phase = 1;
                    stepCounter = 0;
                    animationTimer.Interval = 3000;
                }
                this.Opacity = newOpacity;
            }
            else if (phase == 1)
            {
                phase = 2;
                animationTimer.Interval = 30;
                stepCounter = 0;
            }
            else if (phase == 2)
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