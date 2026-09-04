using System.Runtime.InteropServices;
using Kalender.Core;

namespace Kalender.Interop;

/// <summary>
/// Ham bir Win32 penceresi için ince bir sarmalayıcı. WPF pencerelerinin
/// aksine burada bütün biçemleri ve çizimi kendimiz denetleriz; masaüstü
/// katmanı için buna ihtiyacımız var.
/// </summary>
internal abstract class NativeWindow : IDisposable
{
    // Pencere sınıfı süreç başına bir kez kaydedilir.
    private static readonly Dictionary<string, ushort> RegisteredClasses = new(StringComparer.Ordinal);

    // HWND -> örnek eşlemesi. Statik WndProc buradan doğru nesneyi bulur.
    private static readonly Dictionary<IntPtr, NativeWindow> Windows = new();

    // Delegenin çöp toplayıcı tarafından toplanmaması için statik alanda tutulur.
    private static readonly NativeMethods.WndProcDelegate SharedWndProc = StaticWndProc;

    [ThreadStatic] private static NativeWindow? _creating;

    private bool _disposed;

    public IntPtr Handle { get; private set; }

    protected abstract string ClassName { get; }

    /// <summary>Pencere sınıfının stil bayrakları (CS_*).</summary>
    protected virtual uint ClassStyle => 0;

    protected void CreateHandle(uint exStyle, uint style, int x, int y, int width, int height, IntPtr parent)
    {
        EnsureClassRegistered();

        _creating = this;
        try
        {
            Handle = NativeMethods.CreateWindowEx(
                exStyle,
                ClassName,
                null,
                style,
                x, y, width, height,
                parent,
                IntPtr.Zero,
                NativeMethods.GetModuleHandle(null),
                IntPtr.Zero);
        }
        finally
        {
            _creating = null;
        }

        if (Handle == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Pencere oluşturulamadı (sınıf: {ClassName}, hata: {err}).");
        }

        Windows[Handle] = this;
    }

    private void EnsureClassRegistered()
    {
        if (RegisteredClasses.ContainsKey(ClassName))
        {
            return;
        }

        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            style = ClassStyle,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(SharedWndProc),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = NativeMethods.GetModuleHandle(null),
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = ClassName,
            hIconSm = IntPtr.Zero,
        };

        ushort atom = NativeMethods.RegisterClassEx(ref wc);
        if (atom == 0)
        {
            int err = Marshal.GetLastWin32Error();
            // 1410 = ERROR_CLASS_ALREADY_EXISTS; önceki bir örnekten kalmış olabilir.
            if (err != 1410)
            {
                throw new InvalidOperationException($"Pencere sınıfı kaydedilemedi ({ClassName}, hata: {err}).");
            }
        }

        RegisteredClasses[ClassName] = atom;
    }

    private static IntPtr StaticWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        NativeWindow? target;

        if (!Windows.TryGetValue(hWnd, out target))
        {
            // CreateWindowEx henüz dönmediği için tanıtıcıyı bilmiyoruz.
            if (_creating is not null)
            {
                target = _creating;
                target.Handle = hWnd;
                Windows[hWnd] = target;
            }
        }

        if (target is null)
        {
            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        try
        {
            return target.WndProc(hWnd, msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            // Pencere yordamından dışarı istisna sızarsa süreç çöker; burada durdurup logluyoruz.
            Log.Error("Pencere yordamında beklenmeyen hata.", ex);
            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    protected virtual IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_DESTROY)
        {
            Windows.Remove(hWnd);
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (Handle != IntPtr.Zero)
        {
            Windows.Remove(Handle);
            if (NativeMethods.IsWindow(Handle))
            {
                NativeMethods.DestroyWindow(Handle);
            }

            Handle = IntPtr.Zero;
        }
    }

    ~NativeWindow()
    {
        Dispose(false);
    }
}
