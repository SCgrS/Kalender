using System.Drawing;
using System.Windows.Threading;
using Kalender.Core;
using Kalender.Interop;

namespace Kalender.Desktop;

/// <summary>
/// Takvimin üzerindeki sağ tıklamayı yakalar ve Windows'un masaüstü menüsünün
/// açılmasını engeller; menüyü Kalender kendisi gösterir.
///
/// Neden alt düzey kanca gerekiyor? Takvim duvar kağıdının bir parçası olarak
/// çiziliyor; ortada bizim bir penceremiz yok, dolayısıyla fare olayları hiçbir
/// zaman bize ulaşmıyor. Menüyü biz açacaksak olayı kabuğa varmadan almamız gerekiyor.
///
/// Maliyeti nasıl sıfıra yakın tutuyoruz? Kanca sürekli takılı durmuyor:
/// <see cref="HoverWatcher"/> zaten 100 ms'de bir imlecin nerede olduğuna
/// bakıyor; imleç takvim bloğunun üzerine geldiğinde kancayı takıyor, bloktan
/// çıkınca söküyor. Yani masaüstünün geri kalanında ve başka pencerelerde
/// çalışırken sistemin fare girdisine hiç dokunmuyoruz. Kanca takılıyken de
/// yapılan iş yalnızca "sağ tuş mu?" ve "hangi dikdörtgenin içinde?"
/// sorularından ibaret.
/// </summary>
internal sealed class CalendarRightClickHook : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Func<Point, bool> _isOverCalendar;

    // Temsilci alan olarak tutuluyor: yalnız yerel değişkende dursaydı çöp
    // toplayıcı onu toplar ve kanca çağrıldığında süreç çökerdi.
    private readonly NativeMethods.HookProc _proc;

    private IntPtr _hook;
    private bool _swallowingUp;
    private bool _disposed;

    public CalendarRightClickHook(Dispatcher dispatcher, Func<Point, bool> isOverCalendar)
    {
        _dispatcher = dispatcher;
        _isOverCalendar = isOverCalendar;
        _proc = OnMouse;
    }

    /// <summary>Takvimin üzerinde sağ tıklandı (ekran koordinatı).</summary>
    public event Action<Point>? RightClicked;

    public bool IsArmed => _hook != IntPtr.Zero;

    /// <summary>Kancayı takar. Zaten takılıysa hiçbir şey yapmaz.</summary>
    public void Arm()
    {
        if (_disposed || _hook != IntPtr.Zero)
        {
            return;
        }

        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandle(null), 0);

        if (_hook == IntPtr.Zero)
        {
            Log.Debug("Fare kancası takılamadı; takvimde sağ tık menüsü açılmayacak.");
        }
    }

    /// <summary>Kancayı söker.</summary>
    public void Disarm()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _swallowingUp = false;
    }

    private IntPtr OnMouse(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // nCode < 0 iken kancanın olayı incelemesi yasak; doğrudan zincire ver.
        if (nCode < 0)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        try
        {
            uint message = (uint)wParam;

            if (message is NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_RBUTTONUP)
            {
                var data = System.Runtime.InteropServices.Marshal
                    .PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                var point = new Point(data.pt.X, data.pt.Y);

                if (message == NativeMethods.WM_RBUTTONDOWN)
                {
                    // Basma anında karar veriyoruz; bırakma anında imleç kaymış
                    // olsa bile aynı kararı uyguluyoruz ki kabuğa yarım bir
                    // olay çifti gitmesin.
                    _swallowingUp = _isOverCalendar(point);
                    return _swallowingUp ? new IntPtr(1) : NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
                }

                if (_swallowingUp)
                {
                    _swallowingUp = false;

                    // Menü kancanın içinde açılamaz: kanca çağrısı hızlı
                    // dönmeli, yoksa Windows kancayı devre dışı bırakır.
                    _dispatcher.BeginInvoke(() => RightClicked?.Invoke(point));
                    return new IntPtr(1);
                }
            }
        }
        catch (Exception ex)
        {
            // Kancanın içinde asla istisna sızdırmıyoruz; sistemin fare
            // girdisini bozmaktansa sağ tık menüsünden vazgeçeriz.
            Log.Debug("Fare kancasında hata: " + ex.Message);
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        _disposed = true;
        Disarm();
    }
}
