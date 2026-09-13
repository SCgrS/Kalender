using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Kalender.Interop;
using Kalender.Settings;

namespace Kalender.Ui;

/// <summary>
/// Ayarlar ekranındaki monitör satırı. Her monitörün kendi yerleşimi,
/// boyutu ve fotoğrafı olduğu için satırlar tek tek düzenlenebilir.
/// </summary>
internal sealed class MonitorRow : INotifyPropertyChanged
{
    private readonly Action _changed;

    public MonitorRow(MonitorTarget monitor, MonitorPlacement placement, Action changed)
    {
        Monitor = monitor;
        Placement = placement;
        _changed = changed;
    }

    public MonitorTarget Monitor { get; }

    public MonitorPlacement Placement { get; }

    public string Title => Monitor.IsPrimary
        ? $"{ShortName} (ana ekran)"
        : ShortName;

    public string Detail
    {
        get
        {
            string photo = string.IsNullOrWhiteSpace(Placement.PhotoPath)
                ? "genel zemin"
                : Path.GetFileName(Placement.PhotoPath);

            return $"{Monitor.Bounds.Width} × {Monitor.Bounds.Height} · %{Monitor.Dpi * 100 / 96} ölçek · {photo}";
        }
    }

    /// <summary>Bu ekrana ayrı bir fotoğraf verilmiş mi? "Kaldır" düğmesi buna göre açılır.</summary>
    public bool HasPhoto => !string.IsNullOrWhiteSpace(Placement.PhotoPath);

    public bool Enabled
    {
        get => Placement.Enabled;
        set
        {
            if (Placement.Enabled == value)
            {
                return;
            }

            Placement.Enabled = value;
            Notify();
            _changed();
        }
    }

    public double ScalePercent
    {
        get => Placement.ScalePercent;
        set
        {
            int v = Math.Clamp((int)Math.Round(value), 80, 150);
            if (Placement.ScalePercent == v)
            {
                return;
            }

            Placement.ScalePercent = v;
            Notify();
            Notify(nameof(ScaleText));
            _changed();
        }
    }

    public string ScaleText => $"%{Placement.ScalePercent}";

    private string ShortName
    {
        get
        {
            // \\.\DISPLAY1 -> Ekran 1
            string name = Monitor.DeviceName;
            int i = name.LastIndexOf("DISPLAY", StringComparison.OrdinalIgnoreCase);
            return i >= 0 ? "Ekran " + name[(i + 7)..] : name;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Dışarıdan değişiklik yapıldığında satırın tazelenmesi için.</summary>
    public void RefreshAll()
    {
        Notify(nameof(Detail));
        Notify(nameof(HasPhoto));
        Notify(nameof(Enabled));
        Notify(nameof(ScalePercent));
        Notify(nameof(ScaleText));
    }

    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
