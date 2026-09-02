using System;
using System.Runtime.InteropServices;

namespace ETS2_Assist_GUI.AR
{
    /// <summary>
    /// Минимальный native DirectComposition binding для:
    /// D3D11 device -> IDXGIDevice -> DCompositionCreateDevice
    /// -> CreateTargetForHwnd -> CreateVisual -> SetContent
    /// -> SetRoot -> Commit.
    ///
    /// Используются реальные IID из dcomp.h.
    /// </summary>
    internal static class DirectComposition
    {
        private const string Dll = "dcomp.dll";

        private static readonly Guid IID_IDCompositionDevice =
            new("C37EA93A-E7AA-450D-B16F-9746CB0407F3");

        private static readonly Guid IID_IDCompositionTarget =
            new("EACDD04C-C9BE-4E17-88F4-D1B12B0E3D89");

        private static readonly Guid IID_IDCompositionVisual =
            new("4D93059D-097B-4651-9A60-F0F25116E2F3");

        [DllImport(Dll)]
        private static extern int DCompositionCreateDevice(
            IntPtr dxgiDevice,
            [In] ref Guid iid,
            out IntPtr dcompositionDevice);

        public static object CreateDevice(IntPtr dxgiDevicePtr)
        {
            if (dxgiDevicePtr == IntPtr.Zero)
                throw new ArgumentException("IDXGIDevice pointer is null.", nameof(dxgiDevicePtr));

            Guid iid = IID_IDCompositionDevice;

            int hr = DCompositionCreateDevice(
                dxgiDevicePtr,
                ref iid,
                out IntPtr devPtr);

            if (hr < 0)
                throw new InvalidOperationException(
                    $"DCompositionCreateDevice HRESULT=0x{hr:X8}");

            if (devPtr == IntPtr.Zero)
                throw new InvalidOperationException(
                    "DCompositionCreateDevice returned null device pointer.");

            try
            {
                return Marshal.GetObjectForIUnknown(devPtr);
            }
            finally
            {
                Marshal.Release(devPtr);
            }
        }

        public static object CreateTargetForHwnd(
            object device,
            IntPtr hwnd,
            bool topmost)
        {
            if (device is not IDCompositionDevice dev)
                throw new InvalidOperationException(
                    "Object does not implement IDCompositionDevice.");

            int hr = dev.CreateTargetForHwnd(
                hwnd,
                topmost,
                out var target);

            if (hr < 0)
                throw new InvalidOperationException(
                    $"CreateTargetForHwnd HRESULT=0x{hr:X8}");

            return target;
        }

        public static object CreateVisual(object device)
        {
            if (device is not IDCompositionDevice dev)
                throw new InvalidOperationException(
                    "Object does not implement IDCompositionDevice.");

            int hr = dev.CreateVisual(out var visual);

            if (hr < 0)
                throw new InvalidOperationException(
                    $"CreateVisual HRESULT=0x{hr:X8}");

            return visual;
        }

        public static void SetContent(object visual, IntPtr swapChainPtr)
        {
            if (visual is not IDCompositionVisual v)
                throw new InvalidOperationException(
                    "Object does not implement IDCompositionVisual.");

            if (swapChainPtr == IntPtr.Zero)
                throw new ArgumentException(
                    "SwapChain pointer is null.",
                    nameof(swapChainPtr));

            int hr = v.SetContent(swapChainPtr);

            if (hr < 0)
                throw new InvalidOperationException(
                    $"IDCompositionVisual.SetContent HRESULT=0x{hr:X8}");
        }

        public static void SetRoot(object target, object visual)
        {
            if (target is not IDCompositionTarget t)
                throw new InvalidOperationException(
                    "Object does not implement IDCompositionTarget.");

            if (visual is not IDCompositionVisual v)
                throw new InvalidOperationException(
                    "Object does not implement IDCompositionVisual.");

            int hr = t.SetRoot(v);

            if (hr < 0)
                throw new InvalidOperationException(
                    $"IDCompositionTarget.SetRoot HRESULT=0x{hr:X8}");
        }

        public static void Commit(object device)
        {
            if (device is not IDCompositionDevice dev)
                throw new InvalidOperationException(
                    "Object does not implement IDCompositionDevice.");

            int hr = dev.Commit();

            if (hr < 0)
                throw new InvalidOperationException(
                    $"IDCompositionDevice.Commit HRESULT=0x{hr:X8}");
        }

        // ============================================================
        // IDCompositionDevice
        // ============================================================

        [ComImport]
        [Guid("C37EA93A-E7AA-450D-B16F-9746CB0407F3")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDCompositionDevice
        {
            [PreserveSig]
            int Commit();

            [PreserveSig]
            int WaitForCommitCompletion();

            [PreserveSig]
            int GetFrameStatistics(IntPtr statistics);

            [PreserveSig]
            int CreateTargetForHwnd(
                IntPtr hwnd,
                [MarshalAs(UnmanagedType.Bool)] bool topmost,
                out IDCompositionTarget target);

            [PreserveSig]
            int CreateVisual(out IDCompositionVisual visual);
        }

        // ============================================================
        // IDCompositionTarget
        // ============================================================

        [ComImport]
        [Guid("EACDD04C-117E-4E17-88F4-D1B12B0E3D89")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDCompositionTarget
        {
            [PreserveSig]
            int SetRoot(IDCompositionVisual visual);
        }

        // ============================================================
        // IDCompositionVisual
        //
        // ВАЖНО:
        // Для COM interface порядок методов в vtable обязателен.
        // Нельзя оставлять произвольные "gap" методы.
        // ============================================================

        [ComImport]
        [Guid("4D93059D-097B-4651-9A60-F0F25116E2F3")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDCompositionVisual
        {
            // vtable 3
            [PreserveSig]
            int SetOffsetX(float offsetX);

            // vtable 4
            [PreserveSig]
            int SetOffsetX_Animation(IntPtr animation);

            // vtable 5
            [PreserveSig]
            int SetOffsetY(float offsetY);

            // vtable 6
            [PreserveSig]
            int SetOffsetY_Animation(IntPtr animation);

            // vtable 7
            [PreserveSig]
            int SetTransform(IntPtr matrix);

            // vtable 8
            [PreserveSig]
            int SetTransform_Animation(IntPtr transform);

            // vtable 9
            [PreserveSig]
            int SetTransformParent(IDCompositionVisual visual);

            // vtable 10
            [PreserveSig]
            int SetEffect(IntPtr effect);

            // vtable 11
            [PreserveSig]
            int SetBitmapInterpolationMode(int mode);

            // vtable 12
            [PreserveSig]
            int SetBorderMode(int mode);

            // vtable 13
            [PreserveSig]
            int SetClip(IntPtr clip);

            // vtable 14
            [PreserveSig]
            int SetClip_Animation(IntPtr clip);

            // vtable 15
            [PreserveSig]
            int SetContent(IntPtr content);

            // vtable 16
            [PreserveSig]
            int AddVisual(
                IDCompositionVisual visual,
                [MarshalAs(UnmanagedType.Bool)] bool insertAbove,
                IDCompositionVisual referenceVisual);

            // vtable 17
            [PreserveSig]
            int RemoveVisual(IDCompositionVisual visual);

            // vtable 18
            [PreserveSig]
            int RemoveAllVisuals();

            // vtable 19
            [PreserveSig]
            int SetCompositeMode(int mode);
        }
    }
}