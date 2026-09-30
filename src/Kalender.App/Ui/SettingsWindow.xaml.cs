using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kalender.Core;
using Kalender.Google;
using Kalender.Interop;
using Kalender.Rendering;
using Kalender.Settings;
using Microsoft.Win32;

namespace Kalender.Ui;

/// <summary>
/// Görünüm ayarları. Değişiklikler anında masaüstüne yansır; "Tamam"
/// beklemeye gerek yok. Diske yazma kısa bir gecikmeyle toplanır ki
/// kaydırıcıyı sürüklerken her adımda dosya yazılmasın.
///
/// Pencere Windows 11 Ayarlar'ı gibi kurulu: solda sayfa listesi, sağda
/// seçili sayfanın kartları. Sayfalar yalnızca görünürlükle değişir; hepsi
/// baştan kurulu olduğu için ayarların denetimlere yüklenmesi tek yerde kalır.
/// </summary>
public partial class SettingsWindow : Window
{
    private static readonly (PhotoFit Fit, string Name)[] FitOptions =
    {
        (PhotoFit.Fill, "Kapla"),
        (PhotoFit.Fit, "Sığdır"),
        (PhotoFit.Center, "Ortala"),
        (PhotoFit.Tile, "Döşe"),
    };

    /// <summary>Günlük ayrıntı seçenekleri: (ayar değeri, görünen ad).</summary>
    private static readonly (int Level, string Label)[] LogLevels =
    {
        (3, "Yalnız hatalar"),
        (1, "Bilgi"),
        (0, "Ayrıntılı"),
    };

    private const string ReleasesUrl = "https://github.com/SCgrS/Kalender/releases";

    private readonly IAppearanceHost _host;
    private readonly DispatcherTimer _applyTimer;
    private readonly List<MonitorRow> _rows = new();

    /// <summary>Boşken otomatik hesaplanan, elle de seçilebilen dört takvim rengi.</summary>
    private readonly CalendarColorRow[] _calendarColors;

    private bool _loading;
    private int _page;

    /// <summary>Google'a bağlanma akışı sürüyor; durum satırı o akışa ait.</summary>
    private bool _connecting;
    private CancellationTokenSource? _connectCancel;

    /// <summary>Dönen çemberin açısı; bağlanma sürdükçe döner.</summary>
    private readonly DoubleAnimation _spinnerTurn = new(0, 360, TimeSpan.FromSeconds(1.2))
    {
        RepeatBehavior = RepeatBehavior.Forever,
    };

    /// <summary>Takvim ve görev listesi kartlarının en son hangi kaynaklarla kurulduğu.</summary>
    private readonly Dictionary<StackPanel, string> _sourceSignatures = new();

    internal SettingsWindow(IAppearanceHost host)
    {
        InitializeComponent();

        _host = host;

        // Kısa bir soluk: tek tıklamanın doğurduğu art arda olaylar birleşsin.
        // Arka plan önceliği, pencerenin kendi çizimi (çarkın imleci) önce bitsin diye.
        _applyTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(40),
        };
        _applyTimer.Tick += (_, _) =>
        {
            _applyTimer.Stop();
            _host.ApplyAppearance();
        };

        foreach ((PhotoFit _, string name) in FitOptions)
        {
            FitCombo.Items.Add(name);
        }

        foreach ((int _, string label) in GooglePoller.Intervals)
        {
            PollIntervalCombo.Items.Add(label);
        }

        foreach ((int _, string label) in LogLevels)
        {
            LogLevelCombo.Items.Add(label);
        }

        VersionText.Text = "Sürüm " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?");

        BackgroundColorButton.ColorPicked += OnBackgroundColorPicked;
        TextColorButton.ColorPicked += OnTextColorPicked;
        TaskColorButton.ColorPicked += OnTaskColorPicked;

        _calendarColors = new CalendarColorRow[]
        {
            new(WeekdayAutoBox, WeekdayColorButton, k => k.Weekday, (k, v) => k.Weekday = v, t => t.Text),
            new(HolidayAutoBox, HolidayColorButton, k => k.Holiday, (k, v) => k.Holiday = v, t => t.Holiday),
            new(MutedAutoBox, MutedColorButton, k => k.Muted, (k, v) => k.Muted = v, t => t.Muted),
            new(TodayCircleAutoBox, TodayCircleColorButton, k => k.TodayCircle, (k, v) => k.TodayCircle = v, t => t.TodayCircle),
        };

        foreach (CalendarColorRow row in _calendarColors)
        {
            row.AutoBox.Checked += (_, _) => SetCalendarColorAuto(row);
            row.AutoBox.Unchecked += (_, _) => SetCalendarColorAuto(row);
            row.Button.ColorPicked += c => PickCalendarColor(k => row.Write(k, ColorWheel.ToHex(c)));
        }

        // Pencere ilk kez açılıyorsa Windows'un uygulama teması izlenir;
        // kullanıcı güneş/ay anahtarıyla değiştirdiyse onun seçimi saklanır.
        ApplyWindowTheme(S.DarkSettingsWindow ?? UiTema.WindowsKaranlikMi());

        // Başlık çubuğu ancak pencerenin tanıtıcısı oluşunca boyanabiliyor.
        SourceInitialized += (_, _) => UiTema.BaslikCubugunuBoya(this, _darkWindow);

        BuildPresets();
        BuildReadyPhotos();
        BuildMonitorRows();
        LoadFromSettings();

        NavList.SelectedIndex = 0;

        // Pencere etkinleşince klavye odağı seçili sayfada olsun; oklar hemen çalışır.
        FocusManager.SetFocusedElement(this, (ListBoxItem)NavList.Items[0]);

        PresetPanel.PreviewGotKeyboardFocus += OnSwatchPanelPreviewGotKeyboardFocus;
        ReadyPhotoPanel.PreviewGotKeyboardFocus += OnSwatchPanelPreviewGotKeyboardFocus;

        // Çözünürlük ya da ölçek değişirse satırlardaki ölçüler eskir; taşıma
        // ekranı da o eski ölçülerle açılıp takvimi ekran dışına atardı.
        _host.MonitorsChanged += OnMonitorsChanged;
        _host.GoogleStateChanged += OnGoogleStateChanged;
    }

    /// <summary>
    /// Takvim renklerinden birinin satırı: otomatik anahtarı, renk düğmesi,
    /// ayardaki alanı ve otomatikken hesaplanan değeri.
    /// </summary>
    private sealed record CalendarColorRow(
        CheckBox AutoBox,
        ColorPickerButton Button,
        Func<CalendarTextColors, string?> Read,
        Action<CalendarTextColors, string?> Write,
        Func<Theme, System.Drawing.Color> Computed);

    // ------------------------------------------------------------- gezinti

    /// <summary>
    /// Soldaki listeden sayfa seçildi. Ctrl+tıklama seçimi kaldırabildiği
    /// için boş seçimde önceki sayfada kalınır.
    /// </summary>
    private void OnNavigate(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedIndex < 0)
        {
            NavList.SelectedIndex = _page;
            return;
        }

        _page = NavList.SelectedIndex;

        ScrollViewer[] pages = { BackgroundPage, CalendarPage, ScreensPage, GooglePage, GeneralPage };
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i].Visibility = i == _page ? Visibility.Visible : Visibility.Collapsed;
        }

        PageTitle.Text = AutomationProperties.GetName((ListBoxItem)NavList.Items[_page]);
    }

    // ------------------------------------------------------ pencere teması

    private bool _darkWindow;

    /// <summary>
    /// Pencereyi aydınlık ya da karanlık boyar ve güneş/ay anahtarını ona
    /// eşitler. Anahtarda yazı yok; ipucu ve ekran okuyucu adı durumu söyler.
    /// </summary>
    private void ApplyWindowTheme(bool dark)
    {
        _darkWindow = dark;
        UiTema.Uygula(this, dark);
        UiTema.BaslikCubugunuBoya(this, dark);

        ThemeToggle.IsChecked = dark;
        ThemeToggle.ToolTip = dark ? "Karanlık görünüm (aydınlık için tıklayın)" : "Aydınlık görünüm (karanlık için tıklayın)";
        AutomationProperties.SetName(ThemeToggle, dark ? "Görünüm: karanlık" : "Görünüm: aydınlık");
    }

    /// <summary>
    /// Fareyle, klavyeyle (Boşluk) ya da ekran okuyucuyla değiştirilebilsin
    /// diye Click'e değil Checked/Unchecked'e bağlı. ApplyWindowTheme anahtarı
    /// kendisi de ayarladığı için aynı değer ikinci kez gelirse geçiyoruz.
    /// </summary>
    private void OnThemeToggled(object sender, RoutedEventArgs e)
    {
        bool dark = ThemeToggle.IsChecked == true;
        if (dark == _darkWindow)
        {
            return;
        }

        ApplyWindowTheme(dark);
        S.DarkSettingsWindow = _darkWindow;
        ScheduleApply();
    }

    /// <summary>Metin içindeki bağlantıyı varsayılan tarayıcıda açar.</summary>
    private void OnLinkClicked(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Ac(e.Uri.ToString());
        e.Handled = true;
    }

    private void OnGoogleStateChanged() => Dispatcher.BeginInvoke(() => Silently(UpdateGoogleUi));

    private void OnMonitorsChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            BuildMonitorRows();
            LoadFromSettings();
        });
    }

    private AppSettings S => _host.Settings.Current;

    // ------------------------------------------------------------- yükleme

    private void LoadFromSettings()
    {
        _loading = true;
        try
        {
            AppearanceSettings a = S.Appearance;

            BackgroundColorButton.SelectedColor = WpfImaging.ToMedia(
                ColorTools.Parse(a.SolidColor, System.Drawing.Color.FromArgb(0x2E, 0x32, 0x38)));

            FitCombo.SelectedIndex = Array.FindIndex(FitOptions, o => o.Fit == a.PhotoFit);
            if (FitCombo.SelectedIndex < 0)
            {
                FitCombo.SelectedIndex = 0;
            }

            // Dar sütunda tam yol dosya adının ortasından kırılıyordu; tam yol ipucunda.
            PhotoPathText.Text = string.IsNullOrWhiteSpace(a.PhotoPath)
                ? "Şu an fotoğraf kullanılmıyor."
                : "Seçili fotoğraf: " + PhotoName(a.PhotoPath);
            PhotoPathText.ToolTip = string.IsNullOrWhiteSpace(a.PhotoPath) ? null : a.PhotoPath;

            RemovePhotoButton.IsEnabled = !string.IsNullOrWhiteSpace(a.PhotoPath);

            SaturdayRedBox.IsChecked = a.SaturdayIsRed;

            HolidaysBox.IsChecked = S.Holidays.Enabled;

            UpdateTextColorControls();
            LoadCalendarColors();

            ClientIdBox.Text = S.Google.ClientId ?? string.Empty;
            ClientSecretBox.Text = S.Google.ClientSecret ?? string.Empty;

            int intervalIndex = Array.FindIndex(
                GooglePoller.Intervals, i => i.Seconds == S.Google.PollIntervalSeconds);
            PollIntervalCombo.SelectedIndex = intervalIndex >= 0 ? intervalIndex : 1;

            TaskColorButton.SelectedColor = WpfImaging.ToMedia(
                ColorTools.Parse(S.Google.TaskColor, System.Drawing.Color.FromArgb(0x4C, 0x8D, 0xFF)));

            UpdateGoogleUi();

            StartupBox.IsChecked = S.StartWithWindows;
            UpdateStartupNote();

            WallpaperSyncBox.IsChecked = S.SyncSystemWallpaper;

            int logIndex = Array.FindIndex(LogLevels, l => l.Level == S.LogLevel);
            LogLevelCombo.SelectedIndex = logIndex >= 0 ? logIndex : 0;

            DimSlider.Value = a.BackdropDim;
            DimText.Text = $"%{a.BackdropDim}";

            int popupOpacity = Math.Clamp(a.DayPopupOpacity, 40, 100);
            PopupOpacitySlider.Value = popupOpacity;
            PopupOpacityText.Text = $"%{popupOpacity}";

            AllScreensRadio.IsChecked = a.DisplayTarget == DisplayTarget.AllScreens;
            PrimaryOnlyRadio.IsChecked = a.DisplayTarget == DisplayTarget.PrimaryOnly;
            SelectedScreensRadio.IsChecked = a.DisplayTarget == DisplayTarget.SelectedScreens;

            RefreshPresetSelection();
            RefreshReadyPhotoSelection();

            foreach (MonitorRow row in _rows)
            {
                row.RefreshAll();
            }
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Denetimleri ayara dokunmadan günceller: bu sırada olay işleyicileri
    /// susar. İç içe çağrılabilir; dıştaki yüklemenin koruması bozulmaz.
    /// </summary>
    private void Silently(Action update)
    {
        bool before = _loading;
        _loading = true;
        try
        {
            update();
        }
        finally
        {
            _loading = before;
        }
    }

    private void BuildPresets()
    {
        PresetPanel.Children.Clear();

        var style = (Style)FindResource("RenkOrnegi");
        var darkMark = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x21));

        foreach (ThemePreset preset in ThemePresets.All)
        {
            System.Drawing.Color color = ColorTools.Parse(preset.Color, System.Drawing.Color.Gray);

            // Ad örneğin altında yazmıyor; ipucunda ve ekran okuyucuda duruyor.
            var swatch = new RadioButton
            {
                Style = style,
                Tag = preset,
                Margin = new Thickness(0, 0, 8, 8),
                Background = new SolidColorBrush(WpfImaging.ToMedia(color)),
                Foreground = ColorTools.IsLight(color) ? darkMark : Brushes.White,
                ToolTip = $"{preset.Name} · {preset.Color}",
            };

            AutomationProperties.SetName(swatch, preset.Name);

            swatch.Click += OnPresetClicked;
            PresetPanel.Children.Add(swatch);
        }
    }

    /// <summary>
    /// Seçili hazır rengi işaretler. Düz renk hiçbir hazır renge uymuyorsa
    /// kullanıcının kendi rengi kullanılıyordur; bunu "Kendi rengim" satırı söyler.
    /// </summary>
    private void RefreshPresetSelection()
    {
        bool solid = S.Appearance.BackgroundMode == BackgroundMode.Solid;
        bool anyPreset = false;

        foreach (object item in PresetPanel.Children)
        {
            if (item is not RadioButton { Tag: ThemePreset preset } swatch)
            {
                continue;
            }

            bool selected = solid
                && string.Equals(preset.Color, S.Appearance.SolidColor, StringComparison.OrdinalIgnoreCase);

            swatch.IsChecked = selected;
            anyPreset |= selected;
        }

        CustomColorNote.Text = solid && !anyPreset
            ? "Şu an kendi renginiz kullanılıyor."
            : "Hazır renklerin dışında bir renk seçin.";
    }

    /// <summary>
    /// Hazır renkler ve hazır fotoğraflar birer sekme durağıdır. Gruba
    /// klavyeyle dışarıdan girilince (Tab, Shift+Tab) odak gruptaki ilk ya da
    /// en son gezilen öğeye değil, seçili öğeye gider; ekran okuyucu da
    /// girişte seçimi okur. Grup içinde oklarla gezinmeye ve fareyle tıklanan
    /// öğeye karışılmaz.
    /// </summary>
    private void OnSwatchPanelPreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not Panel grup)
        {
            return;
        }

        bool grupIcinden = e.OldFocus is Visual eski && grup.IsAncestorOf(eski);
        if (grupIcinden || InputManager.Current.MostRecentInputDevice is not KeyboardDevice)
        {
            return;
        }

        RadioButton? secili = grup.Children.OfType<RadioButton>().FirstOrDefault(s => s.IsChecked == true);
        if (secili is not null && !ReferenceEquals(e.NewFocus, secili))
        {
            e.Handled = true;
            secili.Focus();
        }
    }

    /// <summary>
    /// Hazır fotoğrafların küçük resimleri. Renk örnekleriyle aynı biçimde
    /// seçilir: seçilinin çevresinde vurgu halkası ve onay işareti durur.
    /// </summary>
    private void BuildReadyPhotos()
    {
        ReadyPhotoPanel.Children.Clear();

        var style = (Style)FindResource("RenkOrnegi");

        foreach (HazirFotograf foto in HazirFotograflar.All)
        {
            // 16:9 küçük resim; halka ve iç kenarlıkla birlikte 128 × 76, dördü tek sıraya sığsın.
            var kutu = new RadioButton
            {
                Style = style,
                Tag = foto,
                Width = 128,
                Height = 76,
                Margin = new Thickness(0, 0, 8, 8),
                Background = ThumbnailBrush(foto),
                Foreground = Brushes.White,
                ToolTip = $"{foto.Name} · Fotoğraf: {foto.Photographer} (Unsplash)",
            };

            AutomationProperties.SetName(kutu, foto.Name);

            kutu.Click += OnReadyPhotoClicked;
            ReadyPhotoPanel.Children.Add(kutu);
        }
    }

    /// <summary>Küçük resmi gömülü kaynaktan okur; okunamazsa düz gri.</summary>
    private static Brush ThumbnailBrush(HazirFotograf foto)
    {
        try
        {
            using Stream? akis = HazirFotograflar.OpenThumbnail(foto);
            if (akis is not null)
            {
                var resim = new BitmapImage();
                resim.BeginInit();
                resim.CacheOption = BitmapCacheOption.OnLoad;
                resim.StreamSource = akis;
                resim.EndInit();
                resim.Freeze();

                var firca = new ImageBrush(resim) { Stretch = Stretch.UniformToFill };
                firca.Freeze();
                return firca;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Küçük resim okunamadı ({foto.Id}): {ex.Message}");
        }

        return Brushes.Gray;
    }

    /// <summary>Zemin şu an hazır fotoğraflardan biriyse onu işaretler.</summary>
    private void RefreshReadyPhotoSelection()
    {
        HazirFotograf? secili = S.Appearance.BackgroundMode == BackgroundMode.Photo
            ? HazirFotograflar.Find(S.Appearance.PhotoPath)
            : null;

        foreach (RadioButton kutu in ReadyPhotoPanel.Children.OfType<RadioButton>())
        {
            kutu.IsChecked = secili is not null && Equals(kutu.Tag, secili);
        }
    }

    /// <summary>Fotoğrafın ayarlarda görünen adı: hazır fotoğrafın adı ya da dosya adı.</summary>
    private static string PhotoName(string path) => HazirFotograflar.Find(path)?.Name ?? Path.GetFileName(path);

    private void BuildMonitorRows()
    {
        _rows.Clear();

        foreach (MonitorTarget monitor in _host.Monitors)
        {
            MonitorPlacement placement = S.PlacementFor(monitor.StableId);
            _rows.Add(new MonitorRow(monitor, placement, ScheduleApply));
        }

        MonitorList.ItemsSource = null;
        MonitorList.ItemsSource = _rows;
    }

    /// <summary>
    /// Değişikliği masaüstüne uygular; art arda gelen değişiklikler birleştirilir.
    ///
    /// Bekleyen bir uygulama varken zamanlayıcı yeniden kurulmaz. Önceden her
    /// adımda baştan kuruluyordu: renk çarkında ya da kaydırıcıda sürükleme
    /// sürdükçe, fare durana kadar masaüstünde hiçbir şey değişmiyordu. Şimdi
    /// sürüklerken de her uygulama bitince en son hâl yansıyor.
    /// </summary>
    private void ScheduleApply()
    {
        if (_loading || _applyTimer.IsEnabled)
        {
            return;
        }

        _applyTimer.Start();
    }

    // --------------------------------------------------------------- zemin

    private void OnPresetClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: ThemePreset preset })
        {
            return;
        }

        S.Appearance.BackgroundMode = BackgroundMode.Solid;
        S.Appearance.SolidColor = preset.Color;
        S.Appearance.PresetId = preset.Id;

        LoadFromSettings();
        ScheduleApply();
    }

    private void OnBackgroundColorPicked(Color color)
    {
        if (_loading)
        {
            return;
        }

        S.Appearance.BackgroundMode = BackgroundMode.Solid;
        S.Appearance.SolidColor = ColorWheel.ToHex(color);
        S.Appearance.PresetId = null;

        RefreshPresetSelection();
        UpdateTextColorControls();

        // Otomatik renkler zeminden hesaplanıyor; düğmeleri de yeni zemine göre.
        LoadCalendarColors();
        ScheduleApply();
    }

    private void OnPickPhoto(object sender, RoutedEventArgs e)
    {
        string? path = AskForPhoto();
        if (path is null)
        {
            return;
        }

        S.Appearance.BackgroundMode = BackgroundMode.Photo;
        S.Appearance.PhotoPath = path;

        LoadFromSettings();
        ScheduleApply();
    }

    /// <summary>
    /// Hazır fotoğraflardan biri seçildi: exe'den diske çıkarılır ve zemin
    /// olur. Hepsi koyu fotoğraflar; fotoğraf zeminde yazı rengini kullanıcı
    /// seçtiği için koyu bir yazı rengi kalmışsa takvim okunmasın diye beyaza
    /// dönülür. Açık bir renk seçilmişse dokunulmaz.
    /// </summary>
    private void OnReadyPhotoClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: HazirFotograf foto })
        {
            return;
        }

        string? yol = HazirFotograflar.Prepare(foto);
        if (yol is null)
        {
            LoadFromSettings();
            MessageBox.Show(this,
                "Fotoğraf hazırlanamadı. Ayrıntılar günlük dosyasına yazıldı.",
                "Kalender", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        S.Appearance.BackgroundMode = BackgroundMode.Photo;
        S.Appearance.PhotoPath = yol;

        if (!ColorTools.IsLight(ColorTools.Parse(S.Appearance.TextColor, System.Drawing.Color.White)))
        {
            S.Appearance.TextColor = "#FFFFFF";
        }

        LoadFromSettings();
        ScheduleApply();
    }

    private void OnRemovePhoto(object sender, RoutedEventArgs e)
    {
        S.Appearance.PhotoPath = null;

        // Hiçbir ekranda fotoğraf kalmadıysa düz renge dönüyoruz.
        if (!S.Monitors.Values.Any(p => !string.IsNullOrWhiteSpace(p.PhotoPath)))
        {
            S.Appearance.BackgroundMode = BackgroundMode.Solid;
        }

        LoadFromSettings();
        ScheduleApply();
    }

    private void OnFitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || FitCombo.SelectedIndex < 0)
        {
            return;
        }

        S.Appearance.PhotoFit = FitOptions[FitCombo.SelectedIndex].Fit;
        ScheduleApply();
    }

    private string? AskForPhoto()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Zemin fotoğrafı seçin",
            Filter = "Resim dosyaları|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|Tüm dosyalar|*.*",
            CheckFileExists = true,
        };

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void OnDimChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        DimText.Text = $"%{(int)Math.Round(e.NewValue)}";

        if (_loading)
        {
            return;
        }

        S.Appearance.BackdropDim = (int)Math.Round(e.NewValue);
        ScheduleApply();
    }

    // ---------------------------------------------------------- yazı rengi

    /// <summary>
    /// "Otomatik" açıldıysa yazı rengi yeniden zeminden hesaplanır. Kapatıldıysa
    /// düğmede o an görünen (hesaplanmış) renk elle seçim olarak saklanır;
    /// böylece anahtarı kapatmak takvimdeki yazıyı sıçratmaz.
    /// </summary>
    private void OnAutoTextColorChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (AutoTextColorBox.IsChecked == true)
        {
            S.Appearance.TextColorMode = TextColorMode.Auto;
        }
        else
        {
            S.Appearance.TextColor = ColorWheel.ToHex(TextColorButton.SelectedColor);
            S.Appearance.TextColorMode = TextColorMode.Manual;
        }

        UpdateTextColorControls();
        LoadCalendarColors();
        ScheduleApply();
    }

    /// <summary>
    /// Yazı rengi satırını ve fotoğrafa bağlı denetimleri zemin kipine eşitler.
    /// Fotoğraf zeminde otomatik kontrast hesabı yapılamaz: anahtar kapalı
    /// ve kilitli durur, rengi kullanıcı seçer.
    /// </summary>
    private void UpdateTextColorControls()
    {
        AppearanceSettings a = S.Appearance;
        bool photoMode = a.BackgroundMode == BackgroundMode.Photo;

        Silently(() => AutoTextColorBox.IsChecked = !photoMode && a.TextColorMode == TextColorMode.Auto);
        AutoTextColorBox.IsEnabled = !photoMode;

        AutoTextNote.Text = photoMode
            ? "Fotoğraf zeminde otomatik seçim yapılamaz; rengi siz belirlersiniz."
            : "Otomatikte zemine göre açık ya da koyu seçilir; öteki renkler buna göre hesaplanır.";

        DimPanel.IsEnabled = photoMode;
    }

    private void OnTextColorPicked(Color color)
    {
        if (_loading)
        {
            return;
        }

        S.Appearance.TextColor = ColorWheel.ToHex(color);

        // Renk çarkından bir renk seçildiyse artık elle seçim istenmiştir.
        if (S.Appearance.BackgroundMode != BackgroundMode.Photo)
        {
            S.Appearance.TextColorMode = TextColorMode.Manual;
        }

        UpdateTextColorControls();

        // Soluk günler ve bugünün dairesi yazı renginden türetiliyor.
        LoadCalendarColors();
        ScheduleApply();
    }

    /// <summary>
    /// Yazı renginin şu an geçerli hâli: elle seçildiyse o, otomatikse zeminden
    /// hesaplanan. Hafta günleri rengi (yazının üstüne yazılan seçim) buna karışmaz.
    /// </summary>
    private System.Drawing.Color EffectiveTextColor()
    {
        AppearanceSettings a = S.Appearance;
        var textOnly = new AppearanceSettings
        {
            BackgroundMode = a.BackgroundMode,
            SolidColor = a.SolidColor,
            TextColorMode = a.TextColorMode,
            TextColor = a.TextColor,
        };

        return Theme.From(textOnly).Text;
    }

    private void OnSaturdayChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        S.Appearance.SaturdayIsRed = SaturdayRedBox.IsChecked == true;
        ScheduleApply();
    }

    /// <summary>
    /// Gün kutusunun opaklığı. Takvim görüntüsünü değiştirmediği için
    /// masaüstüne yeniden yazılmaz, yalnızca kaydedilir; ayar bir sonraki kutu
    /// açılışında geçerlidir. Önceden her adım duvar kağıdını baştan yazdırıyordu.
    /// </summary>
    private void OnPopupOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // XAML yüklenirken Minimum atanır atanmaz değer 0'dan 40'a çekiliyor ve
        // bu olay yandaki metin kutusu daha oluşmadan bir kez tetikleniyor.
        if (PopupOpacityText is null)
        {
            return;
        }

        PopupOpacityText.Text = $"%{(int)Math.Round(e.NewValue)}";

        if (_loading)
        {
            return;
        }

        S.Appearance.DayPopupOpacity = (int)Math.Round(e.NewValue);
        _host.Settings.Save();
    }

    // ------------------------------------------------------------- tatiller

    private void OnHolidaysEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        S.Holidays.Enabled = HolidaysBox.IsChecked == true;
        ScheduleApply();
    }

    // ------------------------------------------------------ takvim renkleri

    /// <summary>
    /// Renk düğmelerini ve otomatik anahtarlarını ayara eşitler. Ayar boşsa
    /// "Otomatik" açık kalır ve düğmede o an hesaplanan renk görünür; kullanıcı
    /// otomatiği kapattığında elinde makul bir başlangıç rengi olur.
    /// </summary>
    private void LoadCalendarColors()
    {
        CalendarTextColors c = S.Appearance.Colors;
        var theme = Theme.From(S.Appearance);

        Silently(() =>
        {
            TextColorButton.SelectedColor = Opaque(EffectiveTextColor());

            foreach (CalendarColorRow row in _calendarColors)
            {
                string? value = row.Read(c);
                bool otomatik = string.IsNullOrWhiteSpace(value);
                System.Drawing.Color hesaplanan = row.Computed(theme);

                row.AutoBox.IsChecked = otomatik;
                row.Button.SelectedColor = Opaque(otomatik ? hesaplanan : ColorTools.Parse(value, hesaplanan));
            }
        });
    }

    /// <summary>
    /// Düğmeler saydamlık göstermez: bugünün dairesinin yarı saydamlığı
    /// hesabın parçasıdır ve seçilen renge de aynen uygulanır.
    /// </summary>
    private static Color Opaque(System.Drawing.Color c) => Color.FromRgb(c.R, c.G, c.B);

    /// <summary>Bir renk seçildi: ayarı yaz, o satırın otomatik anahtarını kapat.</summary>
    private void PickCalendarColor(Action<CalendarTextColors> uygula)
    {
        if (_loading)
        {
            return;
        }

        uygula(S.Appearance.Colors);
        LoadCalendarColors();
        ScheduleApply();
    }

    /// <summary>
    /// "Otomatik" açıldığında ayarı boşaltır, kapatıldığında düğmede duran
    /// rengi yazar; böylece anahtarı kapatmak görünümü sıçratmaz.
    /// </summary>
    private void SetCalendarColorAuto(CalendarColorRow row)
    {
        if (_loading)
        {
            return;
        }

        if (row.AutoBox.IsChecked == true)
        {
            row.Write(S.Appearance.Colors, null);
            ScheduleApply();

            // Otomatik renk yeniden hesaplandı; düğmeler de onu göstersin.
            _applyTimer.Stop();
            _host.ApplyAppearance();
            LoadCalendarColors();
            return;
        }

        // Otomatik kapatıldı: düğmede duran renk artık kalıcı seçim.
        string hex = ColorWheel.ToHex(row.Button.SelectedColor);
        PickCalendarColor(k => row.Write(k, hex));
    }

    // --------------------------------------------------------------- Google

    private void UpdateGoogleUi()
    {
        bool connected = _host.GoogleConnected;

        GoogleDetailsPanel.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;

        // Bağlanma sürerken düğmeler ve durum satırı o akışa ait; yoklama
        // bittiğinde gelen tazeleme aşama metnini ezmesin.
        if (!_connecting)
        {
            ConnectButton.Content = connected ? "Yeniden bağlan" : "Google'a bağlan";
            DisconnectButton.IsEnabled = connected;
        }

        if (!connected)
        {
            if (!_connecting)
            {
                GoogleStatusText.Text = string.IsNullOrWhiteSpace(S.Google.ClientId)
                    ? "Bağlı değil. Önce aşağıdaki iki değeri yapıştırın."
                    : "Bağlı değil. \"Google'a bağlan\" düğmesine basın; tarayıcıda onay ekranı açılacak.";
            }

            return;
        }

        if (!_connecting)
        {
            string durum = _host.GoogleOnline ? "bağlı" : "bağlı, şu an çevrimdışı (yerel kopya gösteriliyor)";

            GoogleStatusText.Text =
                $"{_host.GoogleAccountEmail ?? "Hesap"} · {durum}"
                + $"\nBu ayda {_host.GoogleEventCount} etkinlik, {_host.GoogleTaskCount} görev.";
        }

        BuildSourceList(CalendarListPanel, _host.GoogleCalendars.Select(c => (c.Id, c.Name)).ToList(),
            S.Google.SelectedCalendarIds);

        BuildSourceList(TaskListPanel, _host.GoogleTaskLists.Select(l => (l.Id, l.Name)).ToList(),
            S.Google.SelectedTaskListIds);
    }

    /// <summary>
    /// Takvim ya da görev listesi satırlarını kurar: solda ad, sağda anahtar,
    /// aralarında ince çizgi. Seçim listesi boşsa "hepsi" anlamına gelir;
    /// kullanıcı ilk kez bir anahtarı kapatınca liste gerçek seçimle doldurulur.
    ///
    /// Her yoklamadan sonra çağrılır; kaynaklar değişmediyse satırlara
    /// dokunulmaz, yoksa kullanıcının tam tıklayacağı anahtar altından kayardı.
    /// </summary>
    private void BuildSourceList(StackPanel panel, List<(string Id, string Name)> sources, List<string> selected)
    {
        string signature = string.Join("\n", sources.Select(s => s.Id + "\t" + s.Name));
        if (_sourceSignatures.TryGetValue(panel, out string? previous) && previous == signature
            && panel.Children.Count > 0)
        {
            return;
        }

        _sourceSignatures[panel] = signature;
        panel.Children.Clear();

        var satir = (Style)FindResource("Satir");

        if (sources.Count == 0)
        {
            var bos = new DockPanel { Style = satir };
            bos.Children.Add(new TextBlock
            {
                Text = "Henüz yüklenmedi.",
                Style = (Style)FindResource("SatirAciklamasi"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            panel.Children.Add(bos);
            return;
        }

        var baslik = (Style)FindResource("SatirBasligi");

        foreach ((string id, string name) in sources)
        {
            if (panel.Children.Count > 0)
            {
                panel.Children.Add(new Separator());
            }

            var box = new CheckBox
            {
                IsChecked = selected.Count == 0 || selected.Contains(id),
                Tag = id,
                Margin = new Thickness(24, 0, 0, 0),
            };

            AutomationProperties.SetName(box, name);
            DockPanel.SetDock(box, Dock.Right);

            box.Checked += OnSourceToggled;
            box.Unchecked += OnSourceToggled;

            var row = new DockPanel { Style = satir };
            row.Children.Add(box);
            row.Children.Add(new TextBlock
            {
                Text = name,
                Style = baslik,
                VerticalAlignment = VerticalAlignment.Center,
            });

            panel.Children.Add(row);
        }
    }

    /// <summary>Listedeki takvim ya da görev listesi anahtarları, görünüş sırasıyla.</summary>
    private static IEnumerable<CheckBox> SourceBoxes(StackPanel panel)
        => panel.Children.OfType<DockPanel>().SelectMany(d => d.Children.OfType<CheckBox>());

    private void OnSourceToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not CheckBox { Tag: string } box)
        {
            return;
        }

        bool isCalendar = CalendarListPanel.IsAncestorOf(box);
        StackPanel panel = isCalendar ? CalendarListPanel : TaskListPanel;
        List<string> target = isCalendar ? S.Google.SelectedCalendarIds : S.Google.SelectedTaskListIds;

        List<CheckBox> boxes = SourceBoxes(panel).ToList();

        target.Clear();
        foreach (CheckBox child in boxes)
        {
            if (child is { IsChecked: true, Tag: string id })
            {
                target.Add(id);
            }
        }

        // Hepsi işaretliyse listeyi boş bırakmak "hepsi" demek; ileride yeni
        // takvim eklenirse kendiliğinden dahil olsun.
        if (target.Count == boxes.Count)
        {
            target.Clear();
        }

        _host.ApplyAppearance();
        _ = _host.RefreshGoogleAsync(force: true);
    }

    private void OnGoogleCredentialsChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        string id = ClientIdBox.Text.Trim();
        string secret = ClientSecretBox.Text.Trim();

        if (S.Google.ClientId == id && S.Google.ClientSecret == secret)
        {
            return;
        }

        S.Google.ClientId = string.IsNullOrEmpty(id) ? null : id;
        S.Google.ClientSecret = string.IsNullOrEmpty(secret) ? null : secret;

        _host.ApplyAppearance();
        UpdateGoogleUi();
    }

    /// <summary>
    /// Bağlanma akışı: tarayıcıda onay, ardından ilk veri alışı. Bu sırada
    /// dönen çember görünür, düğme "Vazgeç" olur ve durum satırı aşamayı
    /// yazar. Önceden düğme yalnızca sönüyor, onaydan sonraki alış sırasında
    /// "onay bekleniyor" yazısı kalıyor ve hata iletisi tazelemeyle siliniyordu.
    /// </summary>
    private async void OnConnectGoogle(object sender, RoutedEventArgs e)
    {
        if (_connecting)
        {
            _connectCancel?.Cancel();
            return;
        }

        OnGoogleCredentialsChanged(sender, e);

        using var cancel = new CancellationTokenSource();
        _connectCancel = cancel;
        SetConnecting(true);
        GoogleStatusText.Text = "Tarayıcıda Google onay ekranı açıldı; onayınız bekleniyor…";

        SignInResult result;
        try
        {
            result = await _host.ConnectGoogleAsync(
                message => GoogleStatusText.Text = message, cancel.Token);
        }
        catch (Exception ex)
        {
            Log.Error("Google bağlantısı kurulamadı.", ex);
            result = new SignInResult(false, "Bağlantı kurulamadı: " + ex.Message);
        }
        finally
        {
            _connectCancel = null;
            SetConnecting(false);
        }

        LoadFromSettings();

        if (!result.Success)
        {
            // Tazeleme "bağlı değil" yazdı; nedenini görünür bırakıyoruz.
            GoogleStatusText.Text = result.Message;
        }
    }

    /// <summary>Dönen çemberi ve düğmeleri bağlanma akışına göre kurar.</summary>
    private void SetConnecting(bool on)
    {
        _connecting = on;

        ConnectSpinner.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ConnectSpinnerAngle.BeginAnimation(RotateTransform.AngleProperty, on ? _spinnerTurn : null);

        ConnectButton.Content = on ? "Vazgeç" : (_host.GoogleConnected ? "Yeniden bağlan" : "Google'a bağlan");
        DisconnectButton.IsEnabled = !on && _host.GoogleConnected;
    }

    private void OnDisconnectGoogle(object sender, RoutedEventArgs e)
    {
        MessageBoxResult answer = MessageBox.Show(this,
            "Google bağlantısı kesilecek ve bu bilgisayardaki kopya silinecek. Devam edilsin mi?",
            "Kalender", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _host.DisconnectGoogle();
        LoadFromSettings();
    }

    private void OnPollIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PollIntervalCombo.SelectedIndex < 0)
        {
            return;
        }

        S.Google.PollIntervalSeconds = GooglePoller.Intervals[PollIntervalCombo.SelectedIndex].Seconds;
        _host.ApplyAppearance();
        _host.ApplyPollInterval();
    }

    private void OnTaskColorPicked(Color color)
    {
        if (_loading)
        {
            return;
        }

        S.Google.TaskColor = ColorWheel.ToHex(color);
        ScheduleApply();
    }

    // ------------------------------------------------------------- ekranlar

    private void OnDisplayTargetChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        S.Appearance.DisplayTarget = true switch
        {
            _ when PrimaryOnlyRadio.IsChecked == true => DisplayTarget.PrimaryOnly,
            _ when SelectedScreensRadio.IsChecked == true => DisplayTarget.SelectedScreens,
            _ => DisplayTarget.AllScreens,
        };

        ScheduleApply();
    }

    private void OnMoveCalendar(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MonitorRow row })
        {
            return;
        }

        try
        {
            // Monitör ölçülerini tam bu anda yeniden okuyoruz; arada çözünürlük
            // değişmiş olabilir ve eski ölçüyle açılan ekran takvimi yanlış
            // koyar.
            MonitorTarget monitor = _host.Monitors
                .FirstOrDefault(m => m.StableId == row.Monitor.StableId) ?? row.Monitor;

            var overlay = new PlacementOverlayWindow(monitor, S, row.Placement, _host.Data, _host.Today)
            {
                Owner = this,
            };

            bool? result = overlay.ShowDialog();
            if (result == true && overlay.Result is not null)
            {
                S.Monitors[monitor.StableId] = overlay.Result;
                BuildMonitorRows();
                LoadFromSettings();
                _host.ApplyAppearance();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Yerleştirme ekranı açılamadı.", ex);
            MessageBox.Show(this,
                "Yerleştirme ekranı açılamadı. Ayrıntılar günlük dosyasına yazıldı.",
                "Kalender", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// "Varsayılana dön": takvimi her ekranda varsayılan yerine (sağ alt köşe;
    /// bkz. <see cref="PlacementDefaults"/>) ve %100 boyutuna döndürür. Hangi
    /// ekranlarda görüneceği, "bu ekranda göster" ve ekran fotoğrafları
    /// korunur. Elle yerleştirme emeği boşa gitmesin diye önce sorulur.
    /// </summary>
    private void OnResetPlacement(object sender, RoutedEventArgs e)
    {
        MessageBoxResult cevap = MessageBox.Show(this,
            "Takvim bütün ekranlarda varsayılan yerine ve %100 boyutuna dönsün mü?",
            "Kalender", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

        if (cevap != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (MonitorTarget monitor in _host.Monitors)
        {
            // Kimliği o an okunamayan ekranın geçici adı ayarlara yazılmıyor.
            if (monitor.KimlikOkunamadi)
            {
                continue;
            }

            MonitorPlacement varsayilan = PlacementDefaults.For(monitor);
            MonitorPlacement yerlesim = S.PlacementFor(monitor.StableId);
            yerlesim.XRatio = varsayilan.XRatio;
            yerlesim.YRatio = varsayilan.YRatio;
            yerlesim.ScalePercent = varsayilan.ScalePercent;
        }

        BuildMonitorRows();
        LoadFromSettings();
        _host.ApplyAppearance();
    }

    private void OnPickMonitorPhoto(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MonitorRow row })
        {
            return;
        }

        string? path = AskForPhoto();
        if (path is null)
        {
            return;
        }

        row.Placement.PhotoPath = path;
        S.Appearance.BackgroundMode = BackgroundMode.Photo;

        LoadFromSettings();
        _host.ApplyAppearance();
    }

    private void OnRemoveMonitorPhoto(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MonitorRow row })
        {
            return;
        }

        row.Placement.PhotoPath = null;

        if (string.IsNullOrWhiteSpace(S.Appearance.PhotoPath)
            && !S.Monitors.Values.Any(p => !string.IsNullOrWhiteSpace(p.PhotoPath)))
        {
            S.Appearance.BackgroundMode = BackgroundMode.Solid;
        }

        LoadFromSettings();
        _host.ApplyAppearance();
    }

    // ---------------------------------------------------------------- genel

    private void OnStartupChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        bool enabled = StartupBox.IsChecked == true;
        S.StartWithWindows = enabled;
        StartupManager.Apply(enabled);

        UpdateStartupNote();
        ScheduleApply();
    }

    private void UpdateStartupNote()
    {
        if (StartupBox.IsChecked != true)
        {
            StartupNote.Text = "Kalender'i her seferinde kendiniz açmanız gerekir.";
            return;
        }

        StartupNote.Text = StartupManager.PointsToCurrentExe()
            ? "Şu an çalışan dosya açılışta başlatılacak: " + Environment.ProcessPath
            : "Açılış kaydı başka bir dosyayı gösteriyor. Bu anahtarı kapatıp yeniden açarsanız "
              + "şu an çalışan dosyaya güncellenir.";
    }

    private void OnWallpaperSyncChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        S.SyncSystemWallpaper = WallpaperSyncBox.IsChecked == true;
        ScheduleApply();
    }

    private void OnLogLevelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LogLevelCombo.SelectedIndex < 0)
        {
            return;
        }

        S.LogLevel = LogLevels[LogLevelCombo.SelectedIndex].Level;
        Log.Level = (LogLevel)S.LogLevel;
        ScheduleApply();
    }

    private void OnOpenLogFolder(object sender, RoutedEventArgs e) => Ac(Paths.LogFolder);

    /// <summary>
    /// Kaldırma penceresini ayrı bir süreçte açar. Kaldırma sırasında çalışan
    /// Kalender kapatılacağı için işi bu sürece yaptıramayız.
    /// </summary>
    private void OnUninstall(object sender, RoutedEventArgs e)
    {
        if (!Installer.IsInstalled)
        {
            MessageBox.Show(this,
                "Kalender kurulu değil; şu an taşınabilir olarak çalışıyor. "
                + "Kapatmak için tepsi menüsünden Çıkış'ı kullanabilirsiniz.",
                "Kalender", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                Installer.InstalledExe, "--kaldir")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Error("Kaldırma penceresi açılamadı.", ex);
        }
    }

    private void OnOpenReleases(object sender, RoutedEventArgs e) => Ac(ReleasesUrl);

    /// <summary>Klasörü ya da adresi kullanıcının varsayılan uygulamasında açar.</summary>
    private static void Ac(string target)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Warn("Açılamadı: " + target, ex);
        }
    }

    // -------------------------------------------------------------- kapanış

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _host.MonitorsChanged -= OnMonitorsChanged;
        _host.GoogleStateChanged -= OnGoogleStateChanged;
        _applyTimer.Stop();

        // Pencere kapanırken tarayıcıda onay bekleniyorsa bekleyiş bitsin.
        _connectCancel?.Cancel();

        // Bekleyen bir değişiklik varsa kapanmadan önce diske yazılsın.
        _host.ApplyAppearance();

        base.OnClosed(e);
    }
}
