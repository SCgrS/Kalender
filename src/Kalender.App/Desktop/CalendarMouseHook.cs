using System.Drawing;
using System.Windows.Threading;
using Kalender.Core;
using Kalender.Interop;

namespace Kalender.Desktop;

/// <summary>
/// Takvimin üzerindeki fare tıklamalarını yakalar: sağ tıkta Windows'un
/// masaüstü menüsü yerine Kalender'in menüsü açılır, ay düğmelerine sol
/// tıkta önceki ya da sonraki aya geçilir.
///
/// Neden alt düzey kanca gerekiyor? Takvim duvar kağıdının bir parçası olarak
/// çiziliyor; ortada bizim bir penceremiz yok, dolayısıyla fare olayları hiçbir
/// zaman bize ulaşmıyor. Tıklamayı biz karşılayacaksak olayı kabuğa varmadan
/// almamız gerekiyor. Yakalanan tıklama kabuğa hiç gitmez: ay düğmesine basınca
/// masaüstünde simge seçimi ya da seçim kutusu başlamaz.
///
/// Maliyeti nasıl sıfıra yakın tutuyoruz? Kanca sürekli takılı durmuyor:
/// <see cref="HoverWatcher"/> zaten 100 ms'de bir imlecin nerede olduğuna
/// bakıyor; imleç takvim bloğunun üzerine geldiğinde kancayı takıyor, bloktan
/// çıkınca söküyor. Yani masaüstünün geri kalanında ve başka pencerelerde
/// çalışırken sistemin fare girdisine hiç dokunmuyoruz. Kanca takılıyken de
/// yapılan iş yalnızca "hangi tuş?" ve "hangi dikdörtgenin içinde?"
/// sorularından ibaret.
/// </summary>
internal sealed class CalendarMouseHook : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Func<Point, bool> _isOverCalendar;
    private readonly Func<Point, int> _navAt;

    // Temsilci alan olarak tutuluyor: yalnız yerel değişkende dursaydı çöp
    // toplayıcı onu toplar ve kanca çağrıldığında süreç çökerdi.
    private readonly NativeMethods.HookProc _proc;

    private IntPtr _hook;
    private bool _swallowingUp;

    /// <summary>Sol tuş hangi ay düğmesinin üzerinde basıldı (-1, +1; 0 hiçbiri).</summary>
    private int _pressedNav;

    private bool _disposed;

    /// <param name="dispatcher">Olayların iletileceği arayüz iş parçacığı.</param>
    /// <param name="isOverCalendar">Nokta masaüstünde takvim bloğunun üzerinde mi?</param>
    /// <param name="navAt">Nokta masaüstünde hangi ay düğmesinin üzerinde (-1, +1; 0 hiçbiri)?</param>
    public CalendarMouseHook(Dispatcher dispatcher, Func<Point, bool> isOverCalendar, Func<Point, int> navAt)
    {
        _dispatcher = dispatcher;
        _isOverCalendar = isOverCalendar;
        _navAt = navAt;
        _proc = OnMouse;
    }

    /// <summary>Takvimin üzerinde sağ tıklandı (ekran koordinatı).</summary>
    public event Action<Point>? RightClicked;

    /// <summary>Ay düğmelerinden birine tıklandı: -1 önceki ay, +1 sonraki ay.</summary>
    public event Action<int>? NavClicked;

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
            Log.Debug("Fare kancası takılamadı; takvimde sağ tık menüsü ve ay düğmeleri çalışmayacak.");
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
        _pressedNav = 0;
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
                return OnRightButton(message, PointOf(lParam), nCode, wParam, lParam);
            }

            if (message is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_LBUTTONUP)
            {
                return OnLeftButton(message, PointOf(lParam), nCode, wParam, lParam);
            }
        }
        catch (Exception ex)
        {
            // Kancanın içinde asla istisna sızdırmıyoruz; sistemin fare
            // girdisini bozmaktansa tıklamadan vazgeçeriz.
            Log.Debug("Fare kancasında hata: " + ex.Message);
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private IntPtr OnRightButton(uint message, Point point, int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (message == NativeMethods.WM_RBUTTONDOWN)
        {
            // Basma anında karar veriyoruz; bırakma anında imleç kaymış
            // olsa bile aynı kararı uyguluyoruz ki kabuğa yarım bir
            // olay çifti gitmesin.
            _swallowingUp = _isOverCalendar(point);
            return _swallowingUp ? new IntPtr(1) : NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if (!_swallowingUp)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        _swallowingUp = false;

        // Menü kancanın içinde açılamaz: kanca çağrısı hızlı
        // dönmeli, yoksa Windows kancayı devre dışı bırakır.
        _dispatcher.BeginInvoke(() => RightClicked?.Invoke(point));
        return new IntPtr(1);
    }

    /// <summary>
    /// Ay düğmesine sol tıklama. Düğme sıradan bir düğme gibi davranır: basılan
    /// düğmenin üzerinde bırakılırsa ay değişir, basılıyken dışarı kayılırsa
    /// vazgeçilmiş sayılır. İki yarı da kabuktan saklanır; masaüstüne yarım bir
    /// olay çifti gitmez.
    /// </summary>
    private IntPtr OnLeftButton(uint message, Point point, int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (message == NativeMethods.WM_LBUTTONDOWN)
        {
            _pressedNav = _navAt(point);
            return _pressedNav != 0 ? new IntPtr(1) : NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if (_pressedNav == 0)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        int yon = _pressedNav;
        _pressedNav = 0;

        if (_navAt(point) == yon)
        {
            // Yeni ayın çizimi kancanın içinde yapılamaz; arayüz iş parçacığına bırakılır.
            _dispatcher.BeginInvoke(() => NavClicked?.Invoke(yon));
        }

        return new IntPtr(1);
    }

    private static Point PointOf(IntPtr lParam)
    {
        var data = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
        return new Point(data.pt.X, data.pt.Y);
    }

    public void Dispose()
    {
        _disposed = true;
        Disarm();
    }
}
