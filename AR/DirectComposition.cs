using System;
using System.Runtime.InteropServices;

namespace ETS2_Assist_GUI.AR
{
    /// <summary>
    /// DirectComposition (dcomp.dll) — минимальные COM-интерфейсы для связки
    /// D3D11 composition swap chain → IDCompositionVisual → target окна.
    /// Vortice не содержит DComp-биндингов (проверено рефлексией 3.8.3),
    /// пакет Vortice.DirectComposition недоступен офлайн — реализуем вручную.
    /// Схема (Microsoft): D3D11 device → IDXGIDevice → DCompositionCreateDevice
    /// → CreateTargetForHwnd → CreateVisual → SetContent(swapChain) →
    /// SetRoot(visual) → Commit.
    ///
    /// ВАЖНО (фикс E_NOINTERFACE 0x80004002): первый аргумент DCompositionCreateDevice
    /// — нативный IDXGIDevice*. НЕЛЬЗЯ передавать SharpGen-обёртку как object с
    /// [MarshalAs(IUnknown)] — маршаллер создаст CCW, реализующий только IUnknown,
    /// и DComp не сможет QueryInterface на IDXGIDevice. Передаём IntPtr (NativePointer).
    /// </summary>
    internal static class DirectComposition
    {
        private const string Dll = "dcomp.dll";

        [DllImport(Dll)]
        private static extern int DCompositionCreateDevice(
            IntPtr dxgiDevice,                       // IDXGIDevice* (нативный указатель)
            [MarshalAs(UnmanagedType.LPStruct)] Guid iid,
            out IntPtr compositionDevice);           // IDCompositionDevice*

        private static readonly Guid IID_IDCompositionDevice = new("C37EA93A-E7AA-450D-B16F-974D6A6A6E6E");
        private static readonly Guid IID_IDCompositionTarget = new("EACDD04C-C9BE-4AB4-A7E6-5F8990D434EE");
        private static readonly Guid IID_IDCompositionVisual = new("4D93059D-097B-43CC-8860-2FBF2A9E2C78");

        /// <summary>Создаёт IDCompositionDevice из нативного IDXGIDevice* (IntPtr).</summary>
        public static object CreateDevice(IntPtr dxgiDevicePtr)
        {
            int hr = DCompositionCreateDevice(dxgiDevicePtr, IID_IDCompositionDevice, out IntPtr devPtr);
            if (hr < 0) throw new InvalidOperationException($"DCompositionCreateDevice HRESULT=0x{hr:X8}");
            return Marshal.GetObjectForIUnknown(devPtr);   // RCW → IDCompositionDevice
        }

        /// <summary>IDCompositionDevice::CreateTargetForHwnd → IDCompositionTarget.</summary>
        public static object CreateTargetForHwnd(object device, IntPtr hwnd, bool topmost)
        {
            var dev = (IDCompositionDevice)device;
            int hr = dev.CreateTargetForHwnd(hwnd, topmost, out var target);
            if (hr < 0) throw new InvalidOperationException($"CreateTargetForHwnd HRESULT=0x{hr:X8}");
            return target;
        }

        /// <summary>IDCompositionDevice::CreateVisual → IDCompositionVisual.</summary>
        public static object CreateVisual(object device)
        {
            var dev = (IDCompositionDevice)device;
            int hr = dev.CreateVisual(out var visual);
            if (hr < 0) throw new InvalidOperationException($"CreateVisual HRESULT=0x{hr:X8}");
            return visual;
        }

        /// <summary>IDCompositionVisual::SetContent(swapChain). swapChain — нативный IUnknown*.</summary>
        public static void SetContent(object visual, IntPtr swapChainPtr)
        {
            ((IDCompositionVisual)visual).SetContent(swapChainPtr);
        }

        /// <summary>IDCompositionTarget::SetRoot(visual).</summary>
        public static void SetRoot(object target, object visual)
        {
            ((IDCompositionTarget)target).SetRoot((IDCompositionVisual)visual);
        }

        /// <summary>IDCompositionDevice::Commit().</summary>
        public static void Commit(object device)
        {
            ((IDCompositionDevice)device).Commit();
        }

        // ================= COM-интерфейсы (vtable-вызовы) =================
        // Каждый интерфейс наследует IUnknown (3 метода) + свои методы.

        [ComImport, Guid("C37EA93A-E7AA-450D-B16F-974D6A6A6E6E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDCompositionDevice
        {
            // IUnknown
            void _VtblGap0(); // QueryInterface
            void _VtblGap1(); // AddRef
            void _VtblGap2(); // Release
            // IDCompositionDevice
            [PreserveSig] int Commit();
            [PreserveSig] int WaitForCommitCompletion();
            [PreserveSig] int GetFrameStatistics(IntPtr statistics);
            [PreserveSig] int CreateTargetForHwnd(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool topmost, out IDCompositionTarget target);
            [PreserveSig] int CreateVisual(out IDCompositionVisual visual);
            // ... остальные методы не нужны
        }

        [ComImport, Guid("EACDD04C-C9BE-4AB4-A7E6-5F8990D434EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDCompositionTarget
        {
            void _VtblGap0();
            void _VtblGap1();
            void _VtblGap2();
            [PreserveSig] int SetRoot(IDCompositionVisual visual);
        }

        [ComImport, Guid("4D93059D-097B-43CC-8860-2FBF2A9E2C78"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDCompositionVisual
        {
            void _VtblGap0();
            void _VtblGap1();
            void _VtblGap2();
            [PreserveSig] int SetOffsetX(float x);
            [PreserveSig] int SetOffsetY(float y);
            [PreserveSig] int SetTransform(IntPtr matrix);
            [PreserveSig] int SetTransformParent(IDCompositionVisual visual);
            [PreserveSig] int SetEffect(IntPtr effect);
            [PreserveSig] int SetOverlayMode(int mode);
            [PreserveSig] int SetClip(IntPtr clip);
            [PreserveSig] int SetBorderMode(int mode);
            [PreserveSig] int SetAlphaMode(int mode);
            [PreserveSig] int SetBackFaceVisibility(int visibility);
            [PreserveSig] int SetBitmapInterpolationMode(int mode);
            [PreserveSig] int SetContent(object content);
            [PreserveSig] int SetContent(IntPtr content);
            [PreserveSig] int AddVisual(IDCompositionVisual visual, [MarshalAs(UnmanagedType.Bool)] bool insertAbove, IDCompositionVisual reference);
            [PreserveSig] int RemoveVisual(IDCompositionVisual visual);
            [PreserveSig] int RemoveAllVisuals();
            [PreserveSig] int SetCompositeMode(int mode);
        }
    }
}