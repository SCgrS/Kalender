using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Kalender.Calendar;
using Kalender.Core;
using Kalender.Interop;
using Kalender.Rendering;
using Kalender.Settings;

namespace Kalender.Ui;

/// <summary>
/// Takvimi sürükleyerek yerleştirme ekranı.
///
/// Masaüstü katmanı hiçbir fare girdisi almadığı için takvim doğrudan
/// masaüstünde sürüklenemez. Bunun yerine ilgili monitörü kaplayan, aynı
/// zemini gösteren geçici bir pencere açılıyor; kullanıcı takvimi burada
/// yerleştirip kaydediyor.
///
/// Pencere hedef monitörün üstünde <b>yaratılıyor</b>, sonradan oraya
/// taşınmıyor. WPF, Left/Top verilirse pencereyi o noktadaki monitörün
/// DPI'ıyla kurar. Önceden pencere ana ekranda kurulup SetWindowPos ile
/// taşınıyordu; ölçeği farklı bir ekrana geçince Windows WM_DPICHANGED
/// gönderiyor, WPF de pencereyi kendi önerdiği dikdörtgene sonradan
/// (SWP_ASYNCWINDOWPOS) çekiyordu. Sonuç: %150'lik ikinci ekran için açılan
/// pencere ana ekranda, yarım boyda görünüyordu.
/// </summary>
public partial class PlacementOverlayWindow : Window
{
    /// <summary>Sınır düzeltmesinin en çok kaç kez yineleneceği; sonsuz döngüye karşı.</summary>
    private const int MaxBoundsFixes = 8;

    private readonly MonitorTarget _monitor;
    private readonly AppSettings _settings;
    private readonly CalendarDataSnapshot _data;
    private readonly DateOnly _today;
    private readonly DesktopSurfaceRenderer _renderer = new();

    private System.Drawing.PointF _origin;
    private System.Drawing.SizeF _blockSize;
    private int _scalePercent;

    /// <summary>Karartma görüntüsünün sol üst köşesinin takvim bloğuna göre yeri.</summary>
    private System.Drawing.PointF _karartmaKaymasi;

    private bool _dragging;
    private System.Windows.Point _grabOffset;
    private int _boundsFixes;

    internal PlacementOverlayWindow(MonitorTarget monitor, AppSettings settings,
        MonitorPlacement placement, CalendarDataSnapshot data, DateOnly today)
    {
        InitializeComponent();

        _monitor = monitor;
        _settings = settings;
        _data = data;
        _today = today;

        Result = placement.Clone();
        _scalePercent = Math.Clamp(Result.ScalePercent, 80, 150);

        ScaleSlider.Value = _scalePercent;
        ScaleText.Text = $"%{_scalePercent}";

        Stage.Width = monitor.Bounds.Width;
        Stage.Height = monitor.Bounds.Height;
        StageScale.ScaleX = 1.0 / monitor.DpiScale;
        StageScale.ScaleY = 1.0 / monitor.DpiScale;

        // Pencerenin tanıtıcısı yaratılırken WPF bu Left/Top noktasının hangi
        // monitöre düştüğüne bakıp o monitörün DPI'ını kullanıyor; değerler o
        // monitörün ölçeğine bölünmüş fiziksel pikseldir. Kesin dikdörtgen
        // hemen ardından SetWindowPos ile piksel cinsinden veriliyor.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = monitor.Bounds.X / monitor.DpiScale;
        Top = monitor.Bounds.Y / monitor.DpiScale;
        Width = monitor.Bounds.Width / monitor.DpiScale;
        Height = monitor.Bounds.Height / monitor.DpiScale;

        SourceInitialized += OnSourceInitialized;
        DpiChanged += OnDpiChanged;
        Loaded += (_, _) => EnsureBounds("yüklenince");
        ContentRendered += (_, _) => EnsureBounds("çizilince");
        LocationChanged += (_, _) => EnsureBounds("taşınınca");
        SizeChanged += (_, _) => EnsureBounds("boyutu değişince");
        Closed += (_, _) => _renderer.Dispose();
    }

    /// <summary>Kullanıcının belirlediği yerleşim. Vazgeçilirse null olur.</summary>
    internal MonitorPlacement? Result { get; private set; }

    private float TotalScale => CalendarScale.For(_monitor.Bounds.Size, _scalePercent);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Pencereyi tam olarak hedef monitörün üstüne, fiziksel piksel
        // cinsinden yerleştiriyoruz. WPF'in Left/Top değerleri çoklu DPI'da
        // güvenilir değil; gösterme işini WPF'e bırakıyoruz.
        ApplyBounds(NativeMethods.SWP_NOACTIVATE);
        UpdateStageScale();
        LogPlacement("kuruldu");

        DrawBackground();
        DrawCalendar();
        MoveTo(DesktopSurfaceRenderer.ResolveOrigin(_monitor.Bounds.Size, TotalScale, Result!), snap: false);

        Activate();
        Focus();
    }

    private void OnDpiChanged(object sender, DpiChangedEventArgs e)
    {
        StageScale.ScaleX = 1.0 / e.NewDpi.DpiScaleX;
        StageScale.ScaleY = 1.0 / e.NewDpi.DpiScaleY;

        // WPF bu olayın ardından pencereyi Windows'un önerdiği dikdörtgene
        // gecikmeli olarak taşır; o iş bitince kendi sınırlarımızı geri koyuyoruz.
        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            () => EnsureBounds("DPI değişince"));
    }

    // ------------------------------------------------------------- yerleşim

    /// <summary>Pencereyi hedef monitörün fiziksel dikdörtgenine oturtur.</summary>
    private void ApplyBounds(uint flags)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            _monitor.Bounds.X,
            _monitor.Bounds.Y,
            _monitor.Bounds.Width,
            _monitor.Bounds.Height,
            flags);
    }

    /// <summary>
    /// Pencere hedef monitörün dışına kaydıysa ya da boyutu bozulduysa geri
    /// koyar. Kendi SetWindowPos çağrımız da bu olayları doğurur; dikdörtgen
    /// zaten doğruysa hiçbir şey yapılmadığı için döngüye girilmez.
    /// </summary>
    private void EnsureBounds(string neden)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT r))
        {
            return;
        }

        System.Drawing.Rectangle b = _monitor.Bounds;
        if (r.Left == b.X && r.Top == b.Y && r.Right == b.Right && r.Bottom == b.Bottom)
        {
            return;
        }

        if (_boundsFixes >= MaxBoundsFixes)
        {
            Log.Warn($"Yerleştirme penceresi hedef ekrana oturtulamadı ({neden}): "
                + $"({r.Left},{r.Top})-({r.Right},{r.Bottom}), beklenen {b}.");
            return;
        }

        _boundsFixes++;
        Log.Debug($"Yerleştirme penceresi {neden} kaymıştı: ({r.Left},{r.Top})-({r.Right},{r.Bottom}); "
            + $"{b} uygulanıyor. Kaydıran:" + Environment.NewLine + KisaYigin());

        ApplyBounds(NativeMethods.SWP_NOACTIVATE);
        UpdateStageScale();
    }

    /// <summary>
    /// Sahne fiziksel piksel uzayında çalışır; pencerenin o anki DPI'ına göre
    /// küçültülür. Pencere hedef monitördeyse bu, monitörün kendi ölçeğidir.
    /// </summary>
    private void UpdateStageScale()
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        if (dpi.DpiScaleX <= 0 || dpi.DpiScaleY <= 0)
        {
            return;
        }

        StageScale.ScaleX = 1.0 / dpi.DpiScaleX;
        StageScale.ScaleY = 1.0 / dpi.DpiScaleY;
    }

    /// <summary>
    /// Pencereyi kimin kaydırdığını anlamak için çağrı yığınının bizden sonraki
    /// ilk satırları. Yalnızca ayrıntılı günlükte, düzeltme olduğunda yazılır.
    /// </summary>
    private static string KisaYigin()
    {
        // Mesaj döngüsünün her katmanda yinelenen sarmalayıcıları atlanıyor;
        // geriye pencereyi kimin, hangi mesajla taşıdığını gösteren satırlar kalıyor.
        string[] gurultu =
        {
            nameof(KisaYigin), nameof(EnsureBounds), "get_StackTrace", "ExceptionWrapper",
            "LegacyInvokeImpl", "HwndSubclass", "CallWindowProc", "DefWndProcWrapper",
            "HwndWrapper.WndProc", "PublicHooksFilterMessage",
        };

        string[] satirlar = Environment.StackTrace.Split('\n');
        return string.Join('\n', satirlar
            .Select(s => s.Trim())
            .Where(s => s.Length > 0 && !gurultu.Any(s.Contains))
            .Take(16)
            .Select(s => "    " + s));
    }

    private void LogPlacement(string asama)
    {
        if (Log.Level > LogLevel.Debug)
        {
            return;
        }

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT r);
        DpiScale dpi = VisualTreeHelper.GetDpi(this);

        Log.Debug($"Yerleştirme penceresi {asama}: hedef {_monitor}, pencere "
            + $"({r.Left},{r.Top})-({r.Right},{r.Bottom}), DPI %{dpi.DpiScaleX * 100:0}.");
    }

    // ------------------------------------------------------------------ çizim

    private void DrawBackground()
    {
        try
        {
            using Bitmap bitmap = _renderer.RenderBackgroundOnly(_monitor.Bounds.Size, _settings, Result!);
            BackgroundImage.Source = WpfImaging.ToBitmapSource(bitmap);
            BackgroundImage.Width = bitmap.Width;
            BackgroundImage.Height = bitmap.Height;
        }
        catch (Exception ex)
        {
            Log.Error("Yerleştirme ekranının zemini çizilemedi.", ex);
        }
    }

    private void DrawCalendar()
    {
        try
        {
            using Bitmap bitmap = _renderer.RenderCalendarBlock(TotalScale, _settings, Result!, _data, _today);
            CalendarImage.Source = WpfImaging.ToBitmapSource(bitmap);
            CalendarImage.Width = bitmap.Width;
            CalendarImage.Height = bitmap.Height;
            _blockSize = new System.Drawing.SizeF(bitmap.Width, bitmap.Height);
        }
        catch (Exception ex)
        {
            Log.Error("Yerleştirme ekranının takvimi çizilemedi.", ex);
        }

        DrawBackdrop();
    }

    /// <summary>
    /// Fotoğraf zemininde takvimin arkasındaki karartma, masaüstündekiyle aynı
    /// boyut ve renkte; takvimle birlikte taşınır. Önceden çizilmiyordu:
    /// kullanıcı karartmanın nereye düşeceğini görmeden yerleştiriyordu.
    /// </summary>
    private void DrawBackdrop()
    {
        try
        {
            using Bitmap? bitmap = _renderer.KarartmaYuzeyi(TotalScale, _settings, Result!, out _karartmaKaymasi);
            BackdropImage.Source = bitmap is null ? null : WpfImaging.ToBitmapSource(bitmap);
            BackdropImage.Width = bitmap?.Width ?? 0;
            BackdropImage.Height = bitmap?.Height ?? 0;
        }
        catch (Exception ex)
        {
            BackdropImage.Source = null;
            Log.Error("Yerleştirme ekranında takvimin arkasındaki karartma çizilemedi.", ex);
        }
    }

    // -------------------------------------------------------------- etkileşim

    private void MoveTo(System.Drawing.PointF point, bool snap)
    {
        SnapEdge edge = SnapEdge.None;

        // Masaüstündeki çizimle aynı sınır: takvim ekran kenarından kenar payı
        // kadar içeride durur. Önceden burada kenara dek gidiyordu; kaydedilen
        // yer masaüstünde ve ekran yeniden açılınca içeri kaymış görünüyordu.
        if (snap)
        {
            point = PlacementSnapper.Snap(point, _blockSize, _monitor.Bounds.Size, TotalScale, out edge);
        }
        else
        {
            point = DesktopSurfaceRenderer.KenarPayiIcinde(point, _blockSize, _monitor.Bounds.Size, TotalScale);
        }

        _origin = point;

        // Takvimin resmi çerçevenin (yapışma vurgusu) içinde duruyor; çerçeve
        // kalınlığı kadar geriye konuyor ki resim masaüstündeki çizimle aynı
        // pikselde dursun. Önceden resim kaydedilen yerden 2 piksel sağda ve
        // aşağıda görünüyordu.
        Canvas.SetLeft(CalendarHost, point.X - CalendarHost.BorderThickness.Left);
        Canvas.SetTop(CalendarHost, point.Y - CalendarHost.BorderThickness.Top);

        // Karartma masaüstündeki gibi takvimin ortasına gelsin.
        Canvas.SetLeft(BackdropImage, point.X + _karartmaKaymasi.X);
        Canvas.SetTop(BackdropImage, point.Y + _karartmaKaymasi.Y);

        UpdateGuides(edge);
    }

    private void UpdateGuides(SnapEdge edge)
    {
        bool showV = edge.HasFlag(SnapEdge.HorizontalCenter);
        bool showH = edge.HasFlag(SnapEdge.VerticalCenter);

        GuideV.Visibility = showV ? Visibility.Visible : Visibility.Collapsed;
        GuideH.Visibility = showH ? Visibility.Visible : Visibility.Collapsed;

        if (showV)
        {
            double x = _monitor.Bounds.Width / 2.0;
            GuideV.X1 = x;
            GuideV.X2 = x;
            GuideV.Y1 = 0;
            GuideV.Y2 = _monitor.Bounds.Height;
        }

        if (showH)
        {
            double y = _monitor.Bounds.Height / 2.0;
            GuideH.X1 = 0;
            GuideH.X2 = _monitor.Bounds.Width;
            GuideH.Y1 = y;
            GuideH.Y2 = y;
        }

        CalendarHost.BorderBrush = edge == SnapEdge.None
            ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF))
            : new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        System.Windows.Point p = e.GetPosition(Stage);
        if (!InsideBlock(p))
        {
            return;
        }

        _dragging = true;
        _grabOffset = new System.Windows.Point(p.X - _origin.X, p.Y - _origin.Y);
        CaptureMouse();
        Cursor = Cursors.SizeAll;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        System.Windows.Point p = e.GetPosition(Stage);

        if (!_dragging)
        {
            Cursor = InsideBlock(p) ? Cursors.SizeAll : Cursors.Arrow;
            return;
        }

        MoveTo(
            new System.Drawing.PointF((float)(p.X - _grabOffset.X), (float)(p.Y - _grabOffset.Y)),
            snap: true);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
        UpdateGuides(SnapEdge.None);
    }

    private bool InsideBlock(System.Windows.Point p)
        => p.X >= _origin.X && p.X <= _origin.X + _blockSize.Width
        && p.Y >= _origin.Y && p.Y <= _origin.Y + _blockSize.Height;

    private void OnScaleChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded && CalendarImage.Source is null)
        {
            return;
        }

        _scalePercent = (int)Math.Round(e.NewValue);
        ScaleText.Text = $"%{_scalePercent}";

        DrawCalendar();
        MoveTo(_origin, snap: false);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Cancel();
                break;
            case Key.Enter:
                Save();
                break;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e) => Save();

    private void OnCancel(object sender, RoutedEventArgs e) => Cancel();

    private void Save()
    {
        Result!.ScalePercent = _scalePercent;
        Result.XRatio = _origin.X / Math.Max(1.0, _monitor.Bounds.Width);
        Result.YRatio = _origin.Y / Math.Max(1.0, _monitor.Bounds.Height);

        DialogResult = true;
        Close();
    }

    private void Cancel()
    {
        Result = null;
        DialogResult = false;
        Close();
    }
}
