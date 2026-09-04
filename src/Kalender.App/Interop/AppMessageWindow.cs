using Kalender.Core;

namespace Kalender.Interop;

/// <summary>
/// Görünmez, üst düzey bir pencere. İki işi var:
/// 1) Sistem yayın mesajlarını almak (ekran değişimi, saat değişimi, uykudan
///    uyanma, Explorer yeniden başlaması). Bu mesajlar yalnızca üst düzey
///    pencerelere gider; masaüstü katmanındaki çocuk pencerelerimiz almaz.
/// 2) Tepsi simgesinin geri çağırmalarını karşılamak.
/// </summary>
internal sealed class AppMessageWindow : NativeWindow
{
    /// <summary>Tepsi simgesinin olaylarını bildirdiği mesaj.</summary>
    public const uint TrayCallbackMessage = NativeMethods.WM_APP + 1;

    private readonly uint _taskbarCreatedMessage;

    private AppMessageWindow()
    {
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
    }

    protected override string ClassName => "KalenderMesajPenceresi";

    /// <summary>Çözünürlük, monitör sayısı ya da yerleşim değişti.</summary>
    public event Action? DisplayChanged;

    /// <summary>Sistem saati veya saat dilimi değişti.</summary>
    public event Action? TimeChanged;

    /// <summary>Bilgisayar uykudan uyandı.</summary>
    public event Action? Resumed;

    /// <summary>Explorer yeniden başladı; masaüstü katmanı sıfırdan kurulmalı.</summary>
    public event Action? ShellRestarted;

    /// <summary>Tepsi simgesine tıklandı: (mesaj, ekran x, ekran y).</summary>
    public event Action<uint, int, int>? TrayEvent;

    public static AppMessageWindow Create()
    {
        var window = new AppMessageWindow();
        window.CreateHandle(
            exStyle: NativeMethods.WS_EX_TOOLWINDOW,
            style: NativeMethods.WS_POPUP,
            x: 0, y: 0, width: 0, height: 0,
            parent: IntPtr.Zero);
        return window;
    }

    protected override IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            Log.Info("Explorer yeniden başladı.");
            ShellRestarted?.Invoke();
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case TrayCallbackMessage:
                // NOTIFYICON_VERSION_4: lParam olayın türünü, wParam ekran konumunu taşır.
                TrayEvent?.Invoke(
                    (uint)(NativeMethods.LoWord(lParam) & 0xFFFF),
                    NativeMethods.LoWord(wParam),
                    NativeMethods.HiWord(wParam));
                return IntPtr.Zero;

            case NativeMethods.WM_DISPLAYCHANGE:
                DisplayChanged?.Invoke();
                return IntPtr.Zero;

            case NativeMethods.WM_SETTINGCHANGE:
                // Çalışma alanı değişimi de buradan gelir (görev çubuğu taşınması gibi).
                DisplayChanged?.Invoke();
                break;

            case NativeMethods.WM_TIMECHANGE:
                TimeChanged?.Invoke();
                return IntPtr.Zero;

            case NativeMethods.WM_POWERBROADCAST:
                int olay = (int)wParam;
                if (olay is NativeMethods.PBT_APMRESUMEAUTOMATIC or NativeMethods.PBT_APMRESUMESUSPEND)
                {
                    Resumed?.Invoke();
                }

                return new IntPtr(1);
        }

        return base.WndProc(hWnd, msg, wParam, lParam);
    }
}
