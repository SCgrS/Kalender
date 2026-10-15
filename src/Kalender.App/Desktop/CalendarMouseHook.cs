using System.Drawing;
using System.Windows.Threading;
using Kalender.Calendar;
using Kalender.Core;
using Kalender.Interop;

namespace Kalender.Desktop;

/// <summary>
/// Takvimin üzerindeki fare tıklamalarını yakalar: sağ tıkta Windows'un
/// masaüstü menüsü yerine Kalender'in menüsü açılır, ay düğmelerine sol
/// tıkta önceki ya da sonraki aya geçilir ya da bu aya dönülür.
///
/// Neden alt düzey kanca gerekiyor? Takvim duvar kağıdının bir parçası olarak
/// çiziliyor; ortada bizim bir penceremiz yok, dolayısıyla fare olayları hiçbir
/// zaman bize ulaşmıyor. Tıklamayı biz karşılayacaksak olayı kabuğa varmadan
/// almamız gerekiyor. Yakalanan tıklama kabuğa hiç gitmez: ay düğmesine basınca
/// masaüstünde simge seçimi ya da seçim kutusu başlamaz. Takvimin üstünde bir
/// masaüstü simgesi varsa karar verici işlevler (bkz. AppHost) isabet
/// vermez; tıklama simgeye gider.
///
/// Maliyeti nasıl sıfıra yakın tutuyoruz? Kanca sürekli takılı durmuyor:
/// <see cref="HoverWatcher"/> zaten 100 ms'de bir imlecin nerede olduğuna
/// bakıyor; imleç takvim bloğunun üzerine geldiğinde kancayı takıyor, bloktan
/// çıkınca söküyor. Yani masaüstünün geri kalanında ve başka pencerelerde
/// çalışırken sistemin fare girdisine hiç dokunmuyoruz. Kanca takılıyken de
/// yapılan iş yalnızca "hangi tuş?" ve "hangi dikdörtgenin içinde?"
/// sorularından ibaret.
///
/// Windows, geri çağırması süreyi aşan alt düzey kancayı haber vermeden söker
/// (Windows 7'den beri). Bunu <see cref="Sokulmus"/> ile anlıyoruz: imleç
/// oynadığı hâlde kanca hiç çağrılmadıysa kanca yeniden takılır (bkz. AppHost).
/// Sağ basışın kararı süreye yaklaşınca kanca imleç oynamasa da yenilenir
/// (<see cref="KararSiniri"/>).
/// </summary>
internal sealed class CalendarMouseHook : IDisposable
{
    /// <summary>
    /// Sağ basış kararının süre sınırı (ms). Windows alt düzey kancayı
    /// LowLevelHooksTimeout kadar bekler (kayıt defterinde yoksa 300 ms); süre
    /// aşılınca basışı kancayı beklemeden kabuğa verir, kancayı da haber vermeden
    /// sökebilir. Sınır bu sürenin biraz altında.
    /// </summary>
    private const long KararSiniri = 250;

    /// <summary>
    /// Bırakışın en çok ne kadar bekleneceği (ms). Tuş basılıyken güvenli
    /// masaüstü (kullanıcı hesabı denetimi, kilit ekranı) açılırsa kanca
    /// bırakışı hiç görmez. Bırakış hep bekleniyor sanılsaydı duvar kâğıdı
    /// çizimi ve bu aya dönüş ertelenip durur, kanca takvimin dışında da takılı
    /// kalırdı. Bu kadar uzun basılı tutulan tuş tıklama sayılmaz: süre dolunca
    /// bırakış kaçmış sayılır (bkz. <see cref="BirakisBekleniyor"/>).
    /// </summary>
    private const long BirakisBeklemeSiniri = 10_000;

    private readonly Dispatcher _dispatcher;
    private readonly Func<Point, bool> _isOverCalendar;
    private readonly Func<Point, NavButton> _navAt;

    // Temsilci alan olarak tutuluyor: yalnız yerel değişkende dursaydı çöp
    // toplayıcı onu toplar ve kanca çağrıldığında süreç çökerdi.
    private readonly NativeMethods.HookProc _proc;

    private IntPtr _hook;
    private bool _swallowingUp;

    /// <summary>Ay düğmesine basıldı ve iş görüldü; bırakma da kabuktan saklanmalı.</summary>
    private bool _swallowingLeftUp;

    /// <summary>Son sağ basışın noktası; basış yutulduysa menü bu noktada (bu günde) açılır.</summary>
    private Point _rightDownPoint;

    /// <summary>Bırakışı beklenen son basışın anı (<see cref="Environment.TickCount64"/>).</summary>
    private long _yutulanBasisAni;

    /// <summary>Kancanın son çağrıldığı an (<see cref="Environment.TickCount64"/>).</summary>
    private long _lastCallAt;

    /// <summary>Kancanın takıldığı an (<see cref="Environment.TickCount64"/>).</summary>
    private long _armedAt;

    private bool _disposed;

    /// <param name="dispatcher">Olayların iletileceği arayüz iş parçacığı.</param>
    /// <param name="isOverCalendar">Nokta masaüstünde takvim bloğunun üzerinde mi?</param>
    /// <param name="navAt">Nokta masaüstünde hangi ay düğmesinin üzerinde?</param>
    public CalendarMouseHook(Dispatcher dispatcher, Func<Point, bool> isOverCalendar, Func<Point, NavButton> navAt)
    {
        _dispatcher = dispatcher;
        _isOverCalendar = isOverCalendar;
        _navAt = navAt;
        _proc = OnMouse;
    }

    /// <summary>
    /// Takvimin üzerinde sağ tıklandı. Nokta, tuşun basıldığı yer (ekran
    /// koordinatı): karar basışta verildi, imleç bırakmadan önce takvimin
    /// dışına kaymış olabilir.
    /// </summary>
    public event Action<Point>? RightClicked;

    /// <summary>Ay düğmelerinden birine basıldı.</summary>
    public event Action<NavButton>? NavClicked;

    /// <summary>
    /// Yutulan bir basışın bırakışı geldi. Bırakış beklenirken kanca
    /// sökülmediği için (<see cref="BirakisBekleniyor"/>) takılı kalıp
    /// kalmayacağına yeniden bakılmalı.
    /// </summary>
    public event Action? BirakisGeldi;

    public bool IsArmed => _hook != IntPtr.Zero;

    /// <summary>
    /// Kanca yuttuğu bir basışın bırakışını mı bekliyor? Bu sürede imleç
    /// takvimden çıksa da sökülmemeli: bırakış kabuğa giderse kabuk basışını
    /// görmediği bir bırakış alır, sağ tıkta menü de hiç açılmaz. Bırakış on
    /// saniyedir gelmediyse kaçmış sayılır (<see cref="BirakisBeklemeSiniri"/>).
    /// </summary>
    public bool BirakisBekleniyor => _hook != IntPtr.Zero && (_swallowingUp || _swallowingLeftUp)
        && Environment.TickCount64 - _yutulanBasisAni < BirakisBeklemeSiniri;

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
            return;
        }

        _armedAt = Environment.TickCount64;
    }

    /// <summary>
    /// Windows kancayı sessizce sökmüş mü? İmleç, <paramref name="oncekiOkuma"/>
    /// anında okunan yerinden bu yana oynamıştır (bkz. HoverWatcher). Takılı bir
    /// alt düzey kanca her hareketi imleç yer değiştirmeden önce görür; kanca o andan
    /// önce takılmış olduğu hâlde o andan beri hiç çağrılmadıysa Windows onu
    /// geri çağırması gecikti diye haber vermeden sökmüştür.
    /// </summary>
    /// <param name="oncekiOkuma">İmlecin bir önceki okunduğu an (<see cref="Environment.TickCount64"/>).</param>
    public bool Sokulmus(long oncekiOkuma)
        => _hook != IntPtr.Zero && _armedAt < oncekiOkuma && _lastCallAt < oncekiOkuma;

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
        _swallowingLeftUp = false;
    }

    /// <summary>
    /// Takılı kancayı söküp yeniden takar; sökülü kancaya dokunmaz (ör. ay
    /// çizilirken bilerek sökülü). Windows kancayı sökmediyse de zararı yok.
    /// </summary>
    private void Yenile()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        Disarm();
        Arm();
    }

    private IntPtr OnMouse(int nCode, IntPtr wParam, IntPtr lParam)
    {
        _lastCallAt = Environment.TickCount64;

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
            bool takvimde = _isOverCalendar(point);
            if (Environment.TickCount64 - _lastCallAt >= KararSiniri)
            {
                // Karar uzun sürdü (ör. Explorer meşgulken simge sorusu).
                // Windows basışı kabuğa vermiş, kancayı da sökmüş olabilir;
                // ölü kanca bırakışı hiç görmez, bırakış hep bekleniyor sanılır
                // ve kanca takvimden çıkınca da sökülmezdi. Basış kabuğa gitmiş
                // sayılır (bırakış da ona gider), kanca yenilenir. Sol basışta
                // gerek yok: ay değişirken kanca zaten sökülüp yeniden takılıyor.
                _swallowingUp = false;
                _dispatcher.BeginInvoke(() => Yenile());
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            _swallowingUp = takvimde;
            _rightDownPoint = point;
            _yutulanBasisAni = Environment.TickCount64;
            return _swallowingUp ? new IntPtr(1) : NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if (!_swallowingUp)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        _swallowingUp = false;

        // Menü kancanın içinde açılamaz: kanca çağrısı hızlı
        // dönmeli, yoksa Windows kancayı devre dışı bırakır.
        Point basilan = _rightDownPoint;
        _dispatcher.BeginInvoke(() => RightClicked?.Invoke(basilan));
        _dispatcher.BeginInvoke(() => BirakisGeldi?.Invoke());
        return new IntPtr(1);
    }

    /// <summary>
    /// Ay düğmesine sol tıklama. Ay, tuş bırakılınca değil basılınca değişir:
    /// basış ile bırakış arası (~0,1 sn) beklenmez, geçiş hızlı görünür. Basış
    /// da bırakış da kabuktan saklanır. Ay çizilirken kanca sökülüyse bırakış
    /// kabuğa gider; önceki basışı görmemiş masaüstü için bu zararsızdır.
    /// </summary>
    private IntPtr OnLeftButton(uint message, Point point, int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (message == NativeMethods.WM_LBUTTONDOWN)
        {
            NavButton dugme = _navAt(point);
            _swallowingLeftUp = dugme != NavButton.None;
            if (!_swallowingLeftUp)
            {
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            _yutulanBasisAni = Environment.TickCount64;

            // Yeni ayın çizimi kancanın içinde yapılamaz; arayüz iş parçacığına bırakılır.
            _dispatcher.BeginInvoke(() => NavClicked?.Invoke(dugme));
            return new IntPtr(1);
        }

        if (!_swallowingLeftUp)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        _swallowingLeftUp = false;
        _dispatcher.BeginInvoke(() => BirakisGeldi?.Invoke());
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
