using System.Drawing;
using Kalender.Calendar;
using Kalender.Core;
using Kalender.Interop;
using Kalender.Rendering;

namespace Kalender.Desktop;

/// <summary>
/// Bir monitörün duvar kağıdını çizen pencere. Masaüstü katmanının
/// (WorkerW) çocuğu olarak yaşar.
///
/// Girdiyle ilgili üç önlem birlikte alınır:
/// WS_EX_NOACTIVATE odak almasını, WS_EX_TRANSPARENT fare vuruşlarını
/// yakalamasını, WM_NCHITTEST yanıtı da kalan bütün durumları engeller.
/// Böylece simgelere tıklama, sürükleyerek çoklu seçim ve masaüstünde
/// sağ tık tamamen Windows'a kalır.
/// </summary>
internal sealed class DesktopWindow : NativeWindow
{
    private DesktopSurface? _surface;

    protected override string ClassName => "KalenderMasaustuKatmani";

    public required string MonitorId { get; init; }

    /// <summary>Monitörün sanal masaüstü koordinatlarındaki alanı.</summary>
    public required Rectangle MonitorBounds { get; init; }

    /// <summary>Bu penceredeki takvim geometrisi; takvim çizilmediyse null.</summary>
    public CalendarLayout? Layout => _surface?.Layout;

    /// <summary>
    /// Pencere bizim isteğimiz dışında yok edildi. Bu, ana pencerenin (WorkerW)
    /// kabuk tarafından yıkıldığı anlamına gelir; katman yeniden kurulmalıdır.
    /// Duvar kağıdı değiştiğinde kabuk WorkerW'yi tam olarak böyle yeniler.
    /// </summary>
    public event Action? DestroyedByShell;

    public static DesktopWindow Create(string monitorId, Rectangle monitorBounds, IntPtr host, Point hostOrigin)
    {
        var window = new DesktopWindow
        {
            MonitorId = monitorId,
            MonitorBounds = monitorBounds,
        };

        window.CreateHandle(
            exStyle: NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW,
            style: NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_CLIPSIBLINGS,
            x: monitorBounds.X - hostOrigin.X,
            y: monitorBounds.Y - hostOrigin.Y,
            width: monitorBounds.Width,
            height: monitorBounds.Height,
            parent: host);

        return window;
    }

    /// <summary>Pencereyi ana pencerenin en altına iter (Progman yedek planında gerekir).</summary>
    public void SendToBack()
    {
        NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>Yeni görüntüyü yerleştirir ve pencereyi yeniden boyatır.</summary>
    public void SetSurface(DesktopSurface surface)
    {
        DesktopSurface? old = _surface;
        _surface = surface;
        old?.Dispose();

        NativeMethods.InvalidateRect(Handle, IntPtr.Zero, false);
    }

    /// <summary>Ekran koordinatındaki noktayı pencere koordinatına çevirir.</summary>
    public PointF ToLocal(Point screenPoint)
        => new(screenPoint.X - MonitorBounds.X, screenPoint.Y - MonitorBounds.Y);

    protected override IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_PAINT:
                OnPaint(hWnd);
                return IntPtr.Zero;

            case NativeMethods.WM_PRINT:
            case NativeMethods.WM_PRINTCLIENT:
                // Ekran görüntüsü alan araçlar (PrintWindow) bu yolu kullanır.
                DrawTo(wParam);
                return IntPtr.Zero;

            case NativeMethods.WM_ERASEBKGND:
                // Zemini kendimiz boyuyoruz; Windows silmeye kalkarsa titreme olur.
                return new IntPtr(1);

            case NativeMethods.WM_NCHITTEST:
                // Fare bu pencereye hiç değmesin.
                return new IntPtr(NativeMethods.HTTRANSPARENT);

            case NativeMethods.WM_MOUSEACTIVATE:
                return new IntPtr(NativeMethods.MA_NOACTIVATE);

            case NativeMethods.WM_DESTROY:
                if (!IsDisposing)
                {
                    Log.Info("Çizim penceresi kabuk tarafından yok edildi.");
                    DestroyedByShell?.Invoke();
                }

                break;
        }

        return base.WndProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>Görüntüyü verilen aygıt bağlamına kopyalar.</summary>
    private void DrawTo(IntPtr hdc)
    {
        try
        {
            Bitmap? image = _surface?.Image;
            if (hdc == IntPtr.Zero || image is null)
            {
                return;
            }

            using Graphics g = Graphics.FromHdc(hdc);
            g.DrawImageUnscaled(image, 0, 0);
        }
        catch (Exception ex)
        {
            Log.Error("Masaüstü katmanı kopyalanamadı.", ex);
        }
    }

    private void OnPaint(IntPtr hWnd)
    {
        IntPtr hdc = NativeMethods.BeginPaint(hWnd, out NativeMethods.PAINTSTRUCT ps);
        try
        {
            Bitmap? image = _surface?.Image;
            if (hdc != IntPtr.Zero && image is not null)
            {
                using Graphics g = Graphics.FromHdc(hdc);
                g.DrawImageUnscaled(image, 0, 0);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Masaüstü katmanı boyanamadı.", ex);
        }
        finally
        {
            NativeMethods.EndPaint(hWnd, ref ps);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _surface?.Dispose();
            _surface = null;
        }

        base.Dispose(disposing);
    }
}
