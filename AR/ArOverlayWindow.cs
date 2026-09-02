using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Numerics;

namespace ETS2_Assist_GUI.AR
{
    /// <summary>
    /// AR v2 — native Direct3D11 + DirectComposition overlay.
    ///
    /// Окно намеренно принимает mouse input для perspective calibration.
    /// Калибровочные точки можно перетаскивать мышью.
    /// </summary>
    public sealed class ArOverlayWindow : IDisposable
    {
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

        private const uint WS_POPUP = 0x80000000;

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        private const int SW_SHOW = 5;

        private const uint WM_MOUSEMOVE = 0x0200;
        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;

        private const float CalibrationHitRadiusPx = 18f;

        private static readonly IntPtr HWND_TOPMOST = new(-1);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(
            IntPtr hWnd,
            int nCmdShow);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProcW(
            IntPtr hWnd,
            uint msg,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx(
            ref WNDCLASSEX lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            int exStyle,
            string className,
            string? windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(
            IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetCapture(
            IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [StructLayout(
            LayoutKind.Sequential,
            CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public int cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        private IntPtr _handle;
        private readonly ArRenderer _renderer = new();

        private Thread? _renderThread;

        public bool IsRunning { get; private set; }

        public void ShowOnScreen(Screen screen)
        {
            if (IsRunning)
                return;

            ArBridge.ResetPerspectiveWarp();

            Rectangle b = screen.Bounds;

            _handle = CreateOverlayWindow(b);

            IsRunning = true;

            _renderer.Initialize(
                _handle,
                b.Width,
                b.Height);

            _renderThread = new Thread(RenderLoop)
            {
                IsBackground = true,
                Name = "ARv2.RenderThread"
            };

            _renderThread.Start();
        }

        public void Stop()
        {
            IsRunning = false;

            try
            {
                _renderThread?.Join(1500);
            }
            catch
            {
            }

            _renderer.Dispose();

            if (_handle != IntPtr.Zero)
            {
                try
                {
                    DestroyWindow(_handle);
                }
                catch
                {
                }

                _handle = IntPtr.Zero;
            }

            ArBridge.EndPerspectiveDrag();
        }

        private IntPtr CreateOverlayWindow(Rectangle b)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc =
                    Marshal.GetFunctionPointerForDelegate<NativeMethods.WndProc>(
                        NativeMethods.DefaultProc),
                hInstance =
                    Marshal.GetHINSTANCE(
                        typeof(ArOverlayWindow).Module),
                lpszClassName = "ETS2Assist_ARv2"
            };

            RegisterClassEx(ref wc);

            IntPtr hwnd = CreateWindowEx(
                WS_EX_NOREDIRECTIONBITMAP |
                WS_EX_TRANSPARENT |
                WS_EX_TOPMOST |
                WS_EX_NOACTIVATE |
                WS_EX_TOOLWINDOW,

                "ETS2Assist_ARv2",
                "ETS2 AR v2.0 (D3D)",

                WS_POPUP,

                b.X,
                b.Y,
                b.Width,
                b.Height,

                IntPtr.Zero,
                IntPtr.Zero,
                wc.hInstance,
                IntPtr.Zero);

            SetWindowPos(
                hwnd,
                HWND_TOPMOST,
                0,
                0,
                0,
                0,
                SWP_NOSIZE |
                SWP_NOMOVE |
                SWP_NOACTIVATE);

            ShowWindow(hwnd, SW_SHOW);

            return hwnd;
        }

        private void RenderLoop()
        {
            while (IsRunning)
            {
                _renderer.WaitForNextFrame();
                _renderer.RenderFrame();
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

    internal static class NativeMethods
    {
        public delegate IntPtr WndProc(
            IntPtr hWnd,
            uint msg,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr SetCapture(
            IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr DefWindowProcW(
            IntPtr hWnd,
            uint msg,
            IntPtr wParam,
            IntPtr lParam);

        public static readonly WndProc DefaultProc =
            (h, m, w, l) =>
            {
                // Перспективные управляющие точки получают мышь напрямую.
                // HTTRANSPARENT НЕ используется.

                if (m == 0x0201) // WM_LBUTTONDOWN
                {
                    Vector2FromLParam(l, out int x, out int y);

                    if (ArBridge.TryBeginPerspectiveDrag(
                            new System.Numerics.Vector2(x, y),
                            12f))
                    {
                        SetCapture(h);
                        return IntPtr.Zero;
                    }
                }

                if (m == 0x0200) // WM_MOUSEMOVE
                {
                    if (ArBridge.IsPerspectiveDragging)
                    {
                        Vector2FromLParam(
                            l,
                            out int x,
                            out int y);

                        ArBridge.UpdatePerspectiveDrag(
                            new System.Numerics.Vector2(x, y));

                        return IntPtr.Zero;
                    }
                }

                if (m == 0x0202) // WM_LBUTTONUP
                {
                    if (ArBridge.IsPerspectiveDragging)
                    {
                        ArBridge.EndPerspectiveDrag();
                        ReleaseCapture();
                        return IntPtr.Zero;
                    }
                }

                return DefWindowProcW(
                    h,
                    m,
                    w,
                    l);
            };

        private static void Vector2FromLParam(
            IntPtr lParam,
            out int x,
            out int y)
        {
            long value = lParam.ToInt64();

            x = unchecked((short)(
                value & 0xFFFF));

            y = unchecked((short)(
                (value >> 16) & 0xFFFF));
        }

        /// <summary>
        /// Сохраняется для совместимости со старой архитектурой.
        /// </summary>
        [DllImport("kernel32.dll")]
        public static extern uint WaitForSingleObject(
            IntPtr hHandle,
            uint dwMilliseconds);
    }
}