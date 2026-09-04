using System.Drawing;
using System.Runtime.InteropServices;
using Kalender.Core;

namespace Kalender.Interop;

/// <summary>Tek bir monitörün, uygulamanın ilgilendiği özellikleri.</summary>
internal sealed class MonitorTarget
{
    public required IntPtr Handle { get; init; }

    /// <summary>Windows'un verdiği aygıt adı, örn. \\.\DISPLAY1.</summary>
    public required string DeviceName { get; init; }

    /// <summary>
    /// Ayarlarda anahtar olarak kullanılan, monitör donanımına bağlı kimlik.
    /// Ekranların yeri değiştiğinde ayarların karışmaması için aygıt adı
    /// yerine bu kullanılır.
    /// </summary>
    public required string StableId { get; init; }

    /// <summary>Sanal masaüstü koordinatlarında monitörün alanı (fiziksel piksel).</summary>
    public required Rectangle Bounds { get; init; }

    /// <summary>Görev çubuğu dışarıda bırakılmış çalışma alanı.</summary>
    public required Rectangle WorkArea { get; init; }

    /// <summary>Etkin DPI (96 = %100).</summary>
    public required uint Dpi { get; init; }

    public required bool IsPrimary { get; init; }

    public double DpiScale => Dpi / 96.0;

    public override string ToString()
        => $"{DeviceName} {Bounds.Width}x{Bounds.Height} @{Bounds.X},{Bounds.Y} DPI {Dpi}{(IsPrimary ? " (ana)" : string.Empty)}";
}

/// <summary>Bağlı monitörleri ve sanal masaüstünün sınırlarını okur.</summary>
internal static class MonitorEnumerator
{
    public static IReadOnlyList<MonitorTarget> All()
    {
        var list = new List<MonitorTarget>();

        NativeMethods.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
        {
            try
            {
                var info = new NativeMethods.MONITORINFOEX
                {
                    cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                    szDevice = string.Empty,
                };

                if (!NativeMethods.GetMonitorInfo(hMonitor, ref info))
                {
                    return true;
                }

                uint dpi = 96;
                if (NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI,
                        out uint dpiX, out _) == 0 && dpiX > 0)
                {
                    dpi = dpiX;
                }

                list.Add(new MonitorTarget
                {
                    Handle = hMonitor,
                    DeviceName = info.szDevice,
                    StableId = ResolveStableId(info.szDevice),
                    Bounds = ToRectangle(info.rcMonitor),
                    WorkArea = ToRectangle(info.rcWork),
                    Dpi = dpi,
                    IsPrimary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
                });
            }
            catch (Exception ex)
            {
                Log.Warn("Monitör bilgisi okunamadı.", ex);
            }

            return true;
        };

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        // Ana ekran her zaman başta olsun; ayarlarda sıralama tutarlı görünsün.
        return list.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ToList();
    }

    /// <summary>Bütün ekranları kapsayan sanal masaüstü dikdörtgeni.</summary>
    public static Rectangle VirtualScreen()
    {
        return new Rectangle(
            NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));
    }

    private static Rectangle ToRectangle(NativeMethods.RECT r)
        => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    /// <summary>
    /// Aygıt adından donanım kimliğini çıkarır. Bulunamazsa aygıt adına düşülür;
    /// bu da çalışır ama ekranlar yer değiştirdiğinde ayarlar karışabilir.
    /// </summary>
    private static string ResolveStableId(string deviceName)
    {
        try
        {
            var dd = new NativeMethods.DISPLAY_DEVICE
            {
                cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>(),
                DeviceName = string.Empty,
                DeviceString = string.Empty,
                DeviceID = string.Empty,
                DeviceKey = string.Empty,
            };

            if (NativeMethods.EnumDisplayDevices(deviceName, 0, ref dd, NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME)
                && !string.IsNullOrWhiteSpace(dd.DeviceID))
            {
                return dd.DeviceID;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Monitör kimliği okunamadı ({deviceName}).", ex);
        }

        return deviceName;
    }
}
