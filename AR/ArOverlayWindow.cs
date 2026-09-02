using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace ETS2_Assist_GUI.AR
{
    /// <summary>
    /// AR v2.0 — прозрачный нативный overlay (D3D11 + flip model + waitable swap chain).
    /// Окно: borderless / transparent / click-through / no-activate / без курсора
    /// (требование архитектуры). Рендер на отдельном потоке, кадр независимо от WS.
    /// Критерий v2.0: минимальный motion-to-photon, latest-state (без очереди поз).
    /// </summary>
    public sealed class ArOverlayWindow : IDisposable
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;

        // Показ/скрытие окна.
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            int exStyle, string className, string? windowName, uint style,
            int x, int y, int width, int height,
            IntPtr parent, IntPtr menu, IntPtr instance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
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

        private IntPtr _handle2;                    // (не используется; резерв стиля Win32-класса)
        private IntPtr _handle;
        private readonly ArRenderer _renderer = new();
        private Thread? _renderThread;

        public bool IsRunning { get; private set; }

        public void ShowOnScreen(Screen screen)
        {
            if (IsRunning) return;
            var b = screen.Bounds;
            _handle = CreateOverlayWindow(b);
            IsRunning = true;
            _renderer.Initialize(_handle, b.Width, b.Height);
            _renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "ARv2.RenderThread" };
            _renderThread.Start();
        }

        public void Stop()
        {
            IsRunning = false;
            try { _renderThread?.Join(1500); } catch { }
            _renderer.Dispose();
            if (_handle != IntPtr.Zero)
            {
                try { DestroyWindow(_handle); } catch { }
                _handle = IntPtr.Zero;
            }
        }

        private IntPtr CreateOverlayWindow(Rectangle b)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate<NativeMethods.WndProc>(NativeMethods.DefaultProc),
                hInstance = Marshal.GetHINSTANCE(typeof(ArOverlayWindow).Module),
                lpszClassName = "ETS2Assist_ARv2"
            };
            RegisterClassEx(ref wc);

            IntPtr hwnd = CreateWindowEx(
                WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW,
                "ETS2Assist_ARv2", "ETS2 AR v2.0 (D3D)",
                0x80000000u /*WS_POPUP*/,
                b.X, b.Y, b.Width, b.Height,
                IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

            // v100: DirectComposition-окно — БЕЗ COLORKEY/LAYERED. Прозрачность
            // каждого пикселя задаётся premultiplied alpha swap chain через DComp.
            SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
            ShowWindow(hwnd, 5 /*SW_SHOW*/);
            return hwnd;
        }

        private void RenderLoop()
        {
            // Каданс задаёт waitable swap chain. Никаких Sleep/Task.Delay (архитектура).
            while (IsRunning)
            {
                _renderer.WaitForNextFrame();
                _renderer.RenderFrame();
            }
        }

        public void Dispose() { Stop(); }
    }

    /// <summary>Win32-хелперы для overlay-окна.</summary>
    internal static class NativeMethods
    {
        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

private const uint WM_NCHITTEST = 0x0084;
private static readonly IntPtr HTTRANSPARENT = new(-1);

public static readonly WndProc DefaultProc = (h, m, w, l) =>
{
    if (m == WM_NCHITTEST)
        return HTTRANSPARENT;

    return DefWindowProcW(h, m, w, l);
};

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        /// <summary>Ожидание waitable-объекта swap chain.</summary>
        [DllImport("kernel32.dll")]
        public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
    }
}