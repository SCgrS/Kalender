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
/// </summary>
public partial class PlacementOverlayWindow : Window
{
    private readonly MonitorTarget _monitor;
    private readonly AppSettings _settings;
    private readonly CalendarDataSnapshot _data;
    private readonly DateOnly _today;
    private readonly DesktopSurfaceRenderer _renderer = new();

    private System.Drawing.PointF _origin;
    private System.Drawing.SizeF _blockSize;
    private int _scalePercent;

    private bool _dragging;
    private System.Windows.Point _grabOffset;

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

        SourceInitialized += OnSourceInitialized;
        DpiChanged += OnDpiChanged;
        Closed += (_, _) => _renderer.Dispose();
    }

    /// <summary>Kullanıcının belirlediği yerleşim. Vazgeçilirse null olur.</summary>
    internal MonitorPlacement? Result { get; private set; }

    private float TotalScale => (float)(_monitor.DpiScale * (_scalePercent / 100.0));

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Pencereyi tam olarak hedef monitörün üstüne, fiziksel piksel
        // cinsinden yerleştiriyoruz. WPF'in Left/Top değerleri çoklu DPI'da
        // güvenilir değil.
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            _monitor.Bounds.X,
            _monitor.Bounds.Y,
            _monitor.Bounds.Width,
            _monitor.Bounds.Height,
            NativeMethods.SWP_SHOWWINDOW);

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
            using Bitmap bitmap = _renderer.RenderCalendarBlock(TotalScale, _settings, _data, _today);
            CalendarImage.Source = WpfImaging.ToBitmapSource(bitmap);
            CalendarImage.Width = bitmap.Width;
            CalendarImage.Height = bitmap.Height;
            _blockSize = new System.Drawing.SizeF(bitmap.Width, bitmap.Height);
        }
        catch (Exception ex)
        {
            Log.Error("Yerleştirme ekranının takvimi çizilemedi.", ex);
        }
    }

    // -------------------------------------------------------------- etkileşim

    private void MoveTo(System.Drawing.PointF point, bool snap)
    {
        SnapEdge edge = SnapEdge.None;

        if (snap)
        {
            point = PlacementSnapper.Snap(point, _blockSize, _monitor.Bounds.Size, out edge);
        }
        else
        {
            float maxX = Math.Max(0, _monitor.Bounds.Width - _blockSize.Width);
            float maxY = Math.Max(0, _monitor.Bounds.Height - _blockSize.Height);
            point = new System.Drawing.PointF(
                Math.Clamp(point.X, 0, maxX),
                Math.Clamp(point.Y, 0, maxY));
        }

        _origin = point;

        Canvas.SetLeft(CalendarHost, point.X);
        Canvas.SetTop(CalendarHost, point.Y);

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
