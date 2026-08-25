using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    /// <summary>
    /// Splash without TransparencyKey/Opacity/full-screen surface.
    /// Uses a small WS_EX_LAYERED ARGB window and UpdateLayeredWindow so the PNG
    /// alpha channel is composited by DWM without color-key holes or taskbar artifacts.
    /// </summary>
    public sealed class SplashForm : Form
    {
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int ULW_ALPHA = 0x00000002;

        private readonly Bitmap _bitmap;
        private readonly Timer _timer;
        private int _phase;
        private int _ticks;
        private const int FadeTicks = 20;
        private const int HoldTicks = 100;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int CX, CY; public SIZE(int cx, int cy) { CX = cx; CY = cy; } }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(
            IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        public SplashForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ControlBox = false;
            Text = string.Empty;

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "ets2a_logo.png");
            _bitmap = LoadPArgbBitmap(path);

            ClientSize = _bitmap.Size;
            Size = _bitmap.Size;
            var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.AllScreens[0].WorkingArea;
            Location = new Point(
                area.Left + Math.Max(0, (area.Width - Width) / 2),
                area.Top + Math.Max(0, (area.Height - Height) / 2));

            _timer = new Timer { Interval = 30 };
            _timer.Tick += (_, _) => AnimationTick();
            _timer.Start();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyLayeredAlpha(0);
        }

        private static Bitmap LoadPArgbBitmap(string path)
        {
            if (File.Exists(path))
            {
                using var source = Image.FromFile(path);
                var clone = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
                using var g = Graphics.FromImage(clone);
                g.Clear(Color.Transparent);
                g.DrawImageUnscaled(source, 0, 0);
                return clone;
            }

            var fallback = new Bitmap(420, 180, PixelFormat.Format32bppPArgb);
            using var fallbackGraphics = Graphics.FromImage(fallback);
            fallbackGraphics.Clear(Color.Transparent);
            using var font = new Font("Segoe UI", 28, FontStyle.Bold);
            fallbackGraphics.DrawString("ETS2 Assist", font, Brushes.White, new PointF(20, 60));
            return fallback;
        }


        private void ApplyLayeredAlpha(byte alpha)
        {
            IntPtr screenDc = IntPtr.Zero;
            IntPtr memDc = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldObject = IntPtr.Zero;

            try
            {
                using var dib = new Bitmap(_bitmap.Width, _bitmap.Height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(dib))
                {
                    g.Clear(Color.Transparent);
                    g.DrawImageUnscaled(_bitmap, 0, 0);
                }

                hBitmap = dib.GetHbitmap(Color.FromArgb(0));
                screenDc = GetDC(IntPtr.Zero);
                memDc = CreateCompatibleDC(screenDc);
                oldObject = SelectObject(memDc, hBitmap);

                // UpdateLayeredWindow works in physical pixels. Use the actual bitmap size,
                // not WinForms Width/Height, which may be DPI-scaled logical units.
                var size = new SIZE(_bitmap.Width, _bitmap.Height);
                var source = new POINT(0, 0);
                var dest = new POINT(Left, Top);
                var blend = new BLENDFUNCTION
                {
                    BlendOp = 0,
                    BlendFlags = 0,
                    SourceConstantAlpha = alpha,
                    AlphaFormat = 1
                };

                if (!UpdateLayeredWindow(Handle, screenDc, ref dest, ref size, memDc,
                    ref source, 0, ref blend, ULW_ALPHA))
                {
                    // Do not throw during splash: application startup must continue.
                }
            }
            finally
            {
                if (oldObject != IntPtr.Zero && memDc != IntPtr.Zero) SelectObject(memDc, oldObject);
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                if (memDc != IntPtr.Zero) DeleteDC(memDc);
                if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private void AnimationTick()
        {
            _ticks++;
            switch (_phase)
            {
                case 0:
                    ApplyLayeredAlpha((byte)Math.Clamp((_ticks * 255) / FadeTicks, 0, 255));
                    if (_ticks >= FadeTicks) { _phase = 1; _ticks = 0; }
                    break;
                case 1:
                    ApplyLayeredAlpha(255);
                    if (_ticks >= HoldTicks) { _phase = 2; _ticks = 0; }
                    break;
                default:
                    ApplyLayeredAlpha((byte)Math.Clamp(255 - (_ticks * 255) / FadeTicks, 0, 255));
                    if (_ticks >= FadeTicks)
                    {
                        _timer.Stop();
                        DialogResult = DialogResult.OK;
                        Close();
                    }
                    break;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _bitmap.Dispose();
            }
            base.Dispose(disposing);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    }
}
