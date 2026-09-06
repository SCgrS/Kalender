using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using Kalender.Calendar;
using Kalender.Core;
using Kalender.Interop;

namespace Kalender.Ui;

/// <summary>
/// Gün kutusu: fare bir günün üzerinde beklediğinde açılan küçük bilgi penceresi.
///
/// Windows'un kendi ipucu balonları gibi davranır: odak almaz, tıklanmaz,
/// sürüklenmez ve ekran kenarına taşarsa kendi konumunu düzeltir. Bu üç şey
/// üst üste sağlanıyor: ShowActivated=false, WS_EX_NOACTIVATE ve
/// WS_EX_TRANSPARENT.
/// </summary>
public partial class DayPopupWindow : Window
{
    // GeneratedRegex için sınıfın partial olması yeterli; XAML zaten partial yapıyor.

    /// <summary>Kutunun etrafındaki gölge payı (DIP). Konum hesabında düşülüyor.</summary>
    private const double ShadowMargin = 10;

    /// <summary>Başlıkların punto ölçüsü.</summary>
    private const double TitleFontSize = 12.5;

    /// <summary>
    /// "Tüm gün" ön eki saat ön eklerinden uzun; bir punto küçültünce sütun
    /// daralıyor ve satır daha dengeli duruyor.
    /// </summary>
    private const double AllDayFontSize = 11.5;

    private static readonly Brush PrefixBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAE));
    private static readonly Brush TitleBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xED));
    private static readonly Brush HolidayBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0x6C, 0x6C));
    private static readonly Brush SeparatorBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));

    private System.Drawing.Rectangle _anchor;

    public DayPopupWindow()
    {
        InitializeComponent();

        PrefixBrush.Freeze();
        TitleBrush.Freeze();
        HolidayBrush.Freeze();
        SeparatorBrush.Freeze();

        SourceInitialized += OnSourceInitialized;
        DpiChanged += (_, _) => Reposition();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Pencere hiçbir şekilde odak ya da fare girdisi almasın.
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        IntPtr ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            new IntPtr((long)ex
                | NativeMethods.WS_EX_NOACTIVATE
                | NativeMethods.WS_EX_TRANSPARENT
                | NativeMethods.WS_EX_TOOLWINDOW));
    }

    /// <summary>Kutuyu verilen günün içeriğiyle, verilen hücrenin yanında gösterir.</summary>
    internal void ShowFor(DayContent content, System.Drawing.Rectangle cellScreenBounds)
    {
        _anchor = cellScreenBounds;

        BuildLines(content);

        // Önce görünmez hâlde yerleştirip sonra görünür yapıyoruz; yoksa kutu
        // bir kare boyunca eski yerinde parlıyor.
        Opacity = 0;

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        Reposition();
        Opacity = 1;
    }

    public void HidePopup()
    {
        if (!IsVisible)
        {
            return;
        }

        Opacity = 0;
        Hide();
    }

    private void BuildLines(DayContent content)
    {
        Lines.Children.Clear();

        bool ilk = true;
        foreach (DayEntry entry in content.Entries)
        {
            // Kayıtlar arasına ince bir ayraç: hangi satırın nerede bittiği
            // uzun başlıklarda gözle seçilemiyordu.
            if (!ilk)
            {
                Lines.Children.Add(new Border
                {
                    Height = 1,
                    Background = SeparatorBrush,
                    Margin = new Thickness(0, 4, 0, 4),
                });
            }

            ilk = false;
            Lines.Children.Add(BuildLine(entry, content));
        }
    }

    /// <summary>
    /// Bir satır: solda "10:30 ·" gibi kısa bir ön ek, sağda başlık.
    ///
    /// Ön ek sütunu bütün satırlarda ortak genişliktedir (SharedSizeGroup);
    /// böylece "Tüm gün" ile "12:20" yan yana geldiğinde başlıklar aynı
    /// hizadan başlar. Başlık iki satıra sarkarsa ön ek satırların ortasında
    /// durur, ilk satıra yapışmaz.
    /// </summary>
    private UIElement BuildLine(DayEntry entry, DayContent content)
    {
        if (entry.Kind == EntryKind.Holiday)
        {
            // Tam gün tatil kırmızı, arife normal renkte; takvimdeki renkle aynı mantık.
            return new TextBlock
            {
                Text = Temizle(entry.Title),
                FontSize = TitleFontSize,
                Margin = new Thickness(0, 2, 0, 2),
                TextWrapping = TextWrapping.Wrap,
                Foreground = content.IsHoliday ? HolidayBrush : TitleBrush,
                FontWeight = FontWeights.SemiBold,
            };
        }

        bool allDay = entry.Kind != EntryKind.Timed || entry.Start is null;

        string prefix = entry.Kind switch
        {
            EntryKind.Timed => entry.Start?.ToString("HH\\:mm") ?? "Tüm gün",
            EntryKind.Task => "Görev",
            _ => "Tüm gün",
        };

        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
            SharedSizeGroup = "Onek",
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var prefixBlock = new TextBlock
        {
            Text = prefix + " ·",
            FontSize = allDay ? AllDayFontSize : TitleFontSize,
            Foreground = PrefixBrush,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        };

        var titleBlock = new TextBlock
        {
            Text = Temizle(entry.Title),
            FontSize = TitleFontSize,
            Foreground = TitleBrush,
            VerticalAlignment = VerticalAlignment.Center,
            // Başlığın tamamı görünsün; sığmazsa alt satıra sarksın.
            TextWrapping = TextWrapping.Wrap,
        };

        Grid.SetColumn(prefixBlock, 0);
        Grid.SetColumn(titleBlock, 1);
        grid.Children.Add(prefixBlock);
        grid.Children.Add(titleBlock);

        return grid;
    }

    /// <summary>
    /// Başlıktaki boşlukları tekleştirir. Google'dan gelen başlıklarda sekme ve
    /// satır sonu olabiliyor; bunlar kutuda kocaman boşluklar açıyordu.
    /// </summary>
    private static string Temizle(string title)
    {
        string temiz = WhitespaceRuns().Replace(title, " ").Trim();

        // Uçuk uzun bir başlık kutuyu boğmasın; görsel sınır zaten MaxLines.
        return temiz.Length <= 240 ? temiz : temiz[..239].TrimEnd() + "…";
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\s+")]
    private static partial System.Text.RegularExpressions.Regex WhitespaceRuns();

    /// <summary>
    /// Kutuyu gün hücresinin altına, hücreyle ortalayarak koyar. Alta sığmazsa
    /// üste alır; yanlara taşarsa ekranın içine çeker.
    /// </summary>
    private void Reposition()
    {
        if (_anchor.Width <= 0)
        {
            return;
        }

        try
        {
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (scale <= 0)
            {
                scale = 1;
            }

            int width = (int)Math.Round(ActualWidth * scale);
            int height = (int)Math.Round(ActualHeight * scale);
            int margin = (int)Math.Round(ShadowMargin * scale);

            System.Drawing.Rectangle work = MonitorEnumerator.WorkAreaAt(
                new System.Drawing.Point(
                    _anchor.X + (_anchor.Width / 2),
                    _anchor.Y + (_anchor.Height / 2)));

            int x = _anchor.X + (_anchor.Width / 2) - (width / 2);
            int y = _anchor.Bottom - margin;

            if (y + height > work.Bottom)
            {
                y = _anchor.Top - height + margin;
            }

            x = Math.Clamp(x, work.Left - margin, Math.Max(work.Left, work.Right - width + margin));
            y = Math.Clamp(y, work.Top - margin, Math.Max(work.Top, work.Bottom - height + margin));

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }
        catch (Exception ex)
        {
            Log.Debug("Gün kutusu yerleştirilemedi: " + ex.Message);
        }
    }
}
