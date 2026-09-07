using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
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

    /// <summary>Ön ekin sonundaki yuvarlağın çapı (DIP).</summary>
    private const double BulletSize = 6;

    /// <summary>
    /// Renkli bir ön ekin koyu zeminde okunabilmesi için gereken en düşük
    /// parlaklık. Google'ın koyu takvim renkleri bunun altında kalabiliyor.
    /// </summary>
    private const double MinAccentLuminance = 0.34;

    /// <summary>
    /// Bir rengin "aslında gri" sayılması için en yüksek kanal farkı. Bunun
    /// altındaki renklerde göz bir renk seçmiyor, yalnızca açıklık görüyor.
    /// </summary>
    private const int NeutralTolerance = 16;

    /// <summary>
    /// Gri bir kayıt renginin kutunun kendi grisine çekilmesi için aradaki
    /// parlaklık farkının kalması gereken sınır.
    /// </summary>
    private const double MutedBand = 0.18;

    /// <summary>
    /// Rengi olmayan ön eklerin ve tamamlanmış kayıtların varsayılan grisi.
    /// Takvimdeki pasif gün grisiyle aynı ton; <see cref="MutedColor"/> ile
    /// takvimin o anki temasından gelen renge güncelleniyor.
    /// </summary>
    private static readonly Color DefaultMuted = Color.FromRgb(0x8D, 0x8F, 0x93);

    private static readonly Brush TitleBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xED));
    private static readonly Brush HolidayBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0x6C, 0x6C));
    private static readonly Brush SeparatorBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));

    private System.Drawing.Rectangle _anchor;

    /// <summary>
    /// Kutunun grisi: tamamlanmış kayıtlar, rengi olmayan ön ekler ve griye
    /// yakın takvim renkleri hep bunu kullanır. <see cref="MutedColor"/> ile
    /// değişir.
    /// </summary>
    private Brush _completedBrush = new SolidColorBrush(DefaultMuted);

    /// <summary><see cref="_completedBrush"/>'ın rengi; gri karşılaştırması buna bakar.</summary>
    private Color _mutedColor = DefaultMuted;

    public DayPopupWindow()
    {
        InitializeComponent();

        _completedBrush.Freeze();
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

    /// <summary>
    /// Tamamlanmış kayıtların rengi. Takvimin kendi pasif gün grisi veriliyor;
    /// böylece gün altındaki gri noktacıkla kutudaki gri satır birebir aynı
    /// tonda oluyor. İki ayrı gri yan yana gelince göze batıyordu.
    /// </summary>
    internal System.Drawing.Color MutedColor
    {
        set
        {
            _mutedColor = Brighten(Color.FromRgb(value.R, value.G, value.B));

            var brush = new SolidColorBrush(_mutedColor);
            brush.Freeze();
            _completedBrush = brush;
        }
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
    /// Bir satır: solda "10:30 •" gibi kısa bir ön ek, sağda başlık.
    ///
    /// Ön ek sütunu bütün satırlarda ortak genişliktedir (SharedSizeGroup);
    /// böylece "Tüm gün" ile "12:20" yan yana geldiğinde başlıklar aynı
    /// hizadan başlar. Başlık iki satıra sarkarsa ön ek satırların ortasında
    /// durur, ilk satıra yapışmaz.
    ///
    /// Ön ek ve sonundaki yuvarlak, kaydın takvimdeki rengini taşır: kutuya
    /// bakan kişi hangi kaydın hangi takvimden geldiğini gün altındaki
    /// noktacıklarla eşleştirebilsin. Tamamlanmış görevlerde renk yerine gri
    /// kullanılır.
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

        // Ön ek yalnızca saati anlatır: saat biliniyorsa saat, bilinmiyorsa
        // "Tüm gün". Kaydın görev mi etkinlik mi olduğu ön ekte yazmıyor;
        // rengi ve noktacığı bunu zaten söylüyor.
        bool allDay = entry.Start is null;
        string prefix = entry.Start?.ToString("HH\\:mm") ?? "Tüm gün";

        Brush accent = entry.Completed
            ? _completedBrush
            : AccentBrush(entry.DotColor);

        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
            SharedSizeGroup = "Onek",
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var prefixBlock = new TextBlock
        {
            Text = prefix,
            FontSize = allDay ? AllDayFontSize : TitleFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = accent,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };

        // Eskiden buradaki nokta bir metin karakteriydi ("·") ve ön ekle aynı
        // renkteydi. Artık gerçek bir yuvarlak: hem biraz daha büyük hem de
        // gün altındaki noktacıkla aynı renkte.
        var bullet = new Ellipse
        {
            Width = BulletSize,
            Height = BulletSize,
            Fill = accent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 0, 0),
        };

        // Yuvarlak her satırda aynı düşey çizgide dursun, metin de kalan yerin
        // ortasına gelsin: "Tüm gün" ile "11:30" aynı günde buluştuğunda ortak
        // sütun en uzun metne göre genişliyor ve kısa saat sola yapışık,
        // boşlukta asılı duruyordu.
        var prefixPanel = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        };

        prefixPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        prefixPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(prefixBlock, 0);
        Grid.SetColumn(bullet, 1);
        prefixPanel.Children.Add(prefixBlock);
        prefixPanel.Children.Add(bullet);

        var titleBlock = new TextBlock
        {
            Text = Temizle(entry.Title),
            FontSize = TitleFontSize,
            Foreground = entry.Completed ? _completedBrush : TitleBrush,
            VerticalAlignment = VerticalAlignment.Center,
            // Başlığın tamamı görünsün; sığmazsa alt satıra sarksın.
            TextWrapping = TextWrapping.Wrap,
        };

        Grid.SetColumn(prefixPanel, 0);
        Grid.SetColumn(titleBlock, 1);
        grid.Children.Add(prefixPanel);
        grid.Children.Add(titleBlock);

        return grid;
    }

    /// <summary>
    /// Kaydın takvim rengini kutuda kullanılabilir hâle getirir. Google'ın koyu
    /// renkleri (koyu mor, koyu yeşil) kutunun koyu zemininde okunmuyor;
    /// yeterince aydınlanana kadar beyaza doğru karıştırıyoruz. Rengi olmayan
    /// kayıtlar kutunun grisine düşer.
    /// </summary>
    private Brush AccentBrush(System.Drawing.Color source)
    {
        if (source.A == 0)
        {
            return _completedBrush;
        }

        Color color = Brighten(Color.FromRgb(source.R, source.G, source.B));

        // Google'ın gri takvim renkleri (grafit gibi) kutudaki tamamlanmış
        // kayıt grisinin bir tık yanına düşüyordu: aynı kutuda iki ayrı gri
        // yan yana gelince göz bunu anlamlı bir ayrım sanıyor, oysa değil.
        // Renk sayılmayacak kadar griye yakın olanları tek tona çekiyoruz.
        if (IsNearMuted(color))
        {
            return _completedBrush;
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Rengi, koyu zeminde okunacak kadar açılana dek beyaza doğru karıştırır.</summary>
    private static Color Brighten(Color color)
    {
        for (int i = 0; i < 6 && Luminance(color) < MinAccentLuminance; i++)
        {
            color = Color.FromRgb(
                (byte)(color.R + ((255 - color.R) * 0.25)),
                (byte)(color.G + ((255 - color.G) * 0.25)),
                (byte)(color.B + ((255 - color.B) * 0.25)));
        }

        return color;
    }

    /// <summary>
    /// Renk, kutunun grisiyle karıştırılacak kadar ona yakın mı? İki koşul
    /// birlikte aranıyor: rengin kendisi gri olacak (kanallar birbirine yakın)
    /// ve açıklığı kutunun grisinden belirgin biçimde ayrılmayacak. Beyaz ya da
    /// çok koyu bir gri bu sınırın dışında kalır, kendi tonunu korur.
    /// </summary>
    private bool IsNearMuted(Color color)
    {
        int max = Math.Max(color.R, Math.Max(color.G, color.B));
        int min = Math.Min(color.R, Math.Min(color.G, color.B));

        if (max - min > NeutralTolerance)
        {
            return false;
        }

        return Math.Abs(Luminance(color) - Luminance(_mutedColor)) <= MutedBand;
    }

    private static double Luminance(Color c)
        => ((0.2126 * c.R) + (0.7152 * c.G) + (0.0722 * c.B)) / 255.0;

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
