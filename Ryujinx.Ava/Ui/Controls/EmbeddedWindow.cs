using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using PInvoke;
using Ryujinx.Ava.Ui.Helper;
using SPB.Graphics;
using SPB.Platform;
using SPB.Platform.GLX;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Ui.Controls
{
    public class EmbeddedWindow : NativeControlHost
    {
        private User32.WndProc _wndProcDelegate;
        private string _className;

        protected GLXWindow X11Window { get; set; }
        protected IntPtr WindowHandle { get; set; }
        protected IntPtr X11Display { get; set; }
        protected IntPtr NsView { get; set; }
        protected IntPtr MetalLayer { get; set; }

        private UpdateBoundsCallbackDelegate _updateBoundsCallback;

        public event EventHandler<IntPtr> WindowCreated;
        public event EventHandler<Size> SizeChanged;

        protected virtual void OnWindowDestroyed() { }
        protected virtual void OnWindowDestroying()
        {
            WindowHandle = IntPtr.Zero;
            X11Display = IntPtr.Zero;
        }

        public EmbeddedWindow()
        {
            var stateObserverable = this.GetObservable(BoundsProperty);

            stateObserverable.Subscribe(StateChanged);

            this.Initialized += NativeEmbeddedWindow_Initialized;
        }

        public virtual void OnWindowCreated() { }

        private void NativeEmbeddedWindow_Initialized(object sender, EventArgs e)
        {
            OnWindowCreated();

            Task.Run(() =>
            {
                WindowCreated?.Invoke(this, WindowHandle);
            });
        }

        private void StateChanged(Rect rect)
        {
            SizeChanged?.Invoke(this, rect.Size);
            _updateBoundsCallback?.Invoke(rect);
        }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            if (OperatingSystem.IsLinux())
            {
                return CreateLinux(parent);
            }
            else if (OperatingSystem.IsWindows())
            {
                return CreateWin32(parent);
            }
            else if (OperatingSystem.IsMacOS())
            {
                return CreateMacOs(parent);
            }

            return base.CreateNativeControlCore(parent);
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            OnWindowDestroying();

            if (OperatingSystem.IsLinux())
            {
                DestroyLinux();
            }
            else if (OperatingSystem.IsWindows())
            {
                DestroyWin32(control);
            }
            else if (OperatingSystem.IsMacOS())
            {
                DestroyMacOS();
            }
            else
            {
                base.DestroyNativeControlCore(control);
            }

            OnWindowDestroyed();
        }

        [SupportedOSPlatform("linux")]
        protected virtual IPlatformHandle CreateLinux(IPlatformHandle parent)
        {
            X11Window    = PlatformHelper.CreateOpenGLWindow(FramebufferFormat.Default, 0, 0, 100, 100) as GLXWindow;
            WindowHandle = X11Window.WindowHandle.RawHandle;
            X11Display   = X11Window.DisplayHandle.RawHandle;

            return new PlatformHandle(WindowHandle, "X11");
        }

        [SupportedOSPlatform("windows")]
        unsafe IPlatformHandle CreateWin32(IPlatformHandle parent)
        {
            _className = "NativeWindow-" + Guid.NewGuid();
            _wndProcDelegate = WndProc;
            var wndClassEx = new User32.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<User32.WNDCLASSEX>(),
                hInstance = Kernel32.GetModuleHandle(null),
                lpfnWndProc = _wndProcDelegate,
                style = User32.ClassStyles.CS_OWNDC,
                lpszClassName = (char *)Unsafe.AsPointer(ref _className),
                hCursor = User32.LoadCursor(IntPtr.Zero, (IntPtr)User32.Cursors.IDC_ARROW).DangerousGetHandle()
            };

            var atom = User32.RegisterClassEx(ref wndClassEx);

            var handle = User32.CreateWindowEx(
                User32.WindowStylesEx.WS_EX_LEFT,
                _className,
                "NativeWindow",
                User32.WindowStyles.WS_CHILD,
                0,
                0,
                640,
                480,
                parent.Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            WindowHandle = handle;

            return new PlatformHandle(WindowHandle, "HWND");
        }

        [SupportedOSPlatform("windows")]
        unsafe IntPtr WndProc(IntPtr hWnd, User32.WindowMessage windowMessage, void* wParam1, void* lParam1)
        {
            var point = new Point((long)lParam1 & 0xFFFF, ((long)lParam1 >> 16) & 0xFFFF);
            var root = VisualRoot as Window;
            bool isLeft = false;
            switch (windowMessage)
            {
                case User32.WindowMessage.WM_LBUTTONDOWN:
                case User32.WindowMessage.WM_RBUTTONDOWN:
                    isLeft = windowMessage == User32.WindowMessage.WM_LBUTTONDOWN;
                    this.RaiseEvent(new PointerPressedEventArgs(
                        this,
                        new Pointer(0, PointerType.Mouse, true),
                        root,
                        this.TranslatePoint(point, root).Value,
                        (ulong)Environment.TickCount64,
                        new PointerPointProperties(isLeft ? RawInputModifiers.LeftMouseButton : RawInputModifiers.RightMouseButton, isLeft ? PointerUpdateKind.LeftButtonPressed : PointerUpdateKind.RightButtonPressed),
                        KeyModifiers.None));
                    break;
                case User32.WindowMessage.WM_LBUTTONUP:
                case User32.WindowMessage.WM_RBUTTONUP:
                    isLeft = windowMessage == User32.WindowMessage.WM_LBUTTONUP;
                    this.RaiseEvent(new PointerReleasedEventArgs(
                        this,
                        new Pointer(0, PointerType.Mouse, true),
                        root,
                        this.TranslatePoint(point, root).Value,
                        (ulong)Environment.TickCount64,
                        new PointerPointProperties(isLeft ? RawInputModifiers.LeftMouseButton : RawInputModifiers.RightMouseButton, isLeft ? PointerUpdateKind.LeftButtonReleased : PointerUpdateKind.RightButtonReleased),
                        KeyModifiers.None,
                        isLeft ? MouseButton.Left : MouseButton.Right));
                    break;
                case User32.WindowMessage.WM_MOUSEMOVE:
                    this.RaiseEvent(new PointerEventArgs(
                        PointerMovedEvent,
                        this,
                        new Pointer(0, PointerType.Mouse, true),
                        root,
                        this.TranslatePoint(point, root).Value,
                        (ulong)Environment.TickCount64,
                        new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
                        KeyModifiers.None));
                    break;
            }
            return User32.DefWindowProc(hWnd, windowMessage, (IntPtr)wParam1, (IntPtr)lParam1);
        }

        [SupportedOSPlatform("macos")]
        IPlatformHandle CreateMacOs(IPlatformHandle parent)
        {
            MetalLayer = MetalHelper.GetMetalLayer(out IntPtr nsView, out _updateBoundsCallback);

            NsView = nsView;

            return new PlatformHandle(nsView, "NSView");
        }

        void DestroyLinux()
        {
            X11Window?.Dispose();
        }

        [SupportedOSPlatform("windows")]
        void DestroyWin32(IPlatformHandle handle)
        {
            User32.DestroyWindow(handle.Handle);
            User32.UnregisterClass(_className, Kernel32.GetModuleHandle(null));
        }

        [SupportedOSPlatform("macos")]
        void DestroyMacOS()
        {
            MetalHelper.DestroyMetalLayer(NsView, MetalLayer);
        }
    }
}