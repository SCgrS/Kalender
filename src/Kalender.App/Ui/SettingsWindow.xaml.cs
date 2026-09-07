using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

    private bool _loading;

    internal SettingsWindow(IAppearanceHost host)
    {
        InitializeComponent();

        _host = host;
        _applyTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(150),
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

        BackgroundWheel.ColorChanged += OnBackgroundColorPicked;
        TextWheel.ColorChanged += OnTextColorPicked;
        TaskColorWheel.ColorChanged += OnTaskColorPicked;

        WeekdayWheel.ColorChanged += c => PickCalendarColor(k => k.Weekday = ColorWheel.ToHex(c));
        HolidayWheel.ColorChanged += c => PickCalendarColor(k => k.Holiday = ColorWheel.ToHex(c));
        MutedWheel.ColorChanged += c => PickCalendarColor(k => k.Muted = ColorWheel.ToHex(c));
        TodayCircleWheel.ColorChanged += c => PickCalendarColor(k => k.TodayCircle = ColorWheel.ToHex(c));

        // Pencere ilk kez açılıyorsa Windows'un uygulama teması izlenir;
        // kullanıcı düğmeyle değiştirdiyse onun seçimi saklanır.
        ApplyWindowTheme(S.DarkSettingsWindow ?? UiTema.WindowsKaranlikMi());

        BuildPresets();
        BuildMonitorRows();
        LoadFromSettings();

        // Çözünürlük ya da ölçek değişirse satırlardaki ölçüler eskir; taşıma
        // ekranı da o eski ölçülerle açılıp takvimi ekran dışına atardı.
        _host.MonitorsChanged += OnMonitorsChanged;
        _host.GoogleStateChanged += OnGoogleStateChanged;
    }

    // ------------------------------------------------------ pencere teması

    private bool _darkWindow;

    private void ApplyWindowTheme(bool dark)
    {
        _darkWindow = dark;
        UiTema.Uygula(this, dark);
        ThemeButton.Content = dark ? "Aydınlık kip" : "Karanlık kip";
    }

    private void OnToggleTheme(object sender, RoutedEventArgs e)
    {
        ApplyWindowTheme(!_darkWindow);
        S.DarkSettingsWindow = _darkWindow;
        ScheduleApply();
    }

    /// <summary>Metin içindeki bağlantıyı varsayılan tarayıcıda açar.</summary>
    private void OnLinkClicked(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Ac(e.Uri.ToString());
        e.Handled = true;
    }

    private void OnGoogleStateChanged() => Dispatcher.BeginInvoke(() =>
    {
        _loading = true;
        try
        {
            UpdateGoogleUi();
        }
        finally
        {
            _loading = false;
        }
    });

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
            bool photoMode = a.BackgroundMode == BackgroundMode.Photo;

            BackgroundWheel.SelectedColor = WpfImaging.ToMedia(
                ColorTools.Parse(a.SolidColor, System.Drawing.Color.FromArgb(0x2E, 0x32, 0x38)));

            TextWheel.SelectedColor = WpfImaging.ToMedia(
                ColorTools.Parse(a.TextColor, System.Drawing.Color.White));

            FitCombo.SelectedIndex = Array.FindIndex(FitOptions, o => o.Fit == a.PhotoFit);
            if (FitCombo.SelectedIndex < 0)
            {
                FitCombo.SelectedIndex = 0;
            }

            PhotoPathText.Text = string.IsNullOrWhiteSpace(a.PhotoPath)
                ? "Şu an fotoğraf kullanılmıyor."
                : "Seçili fotoğraf: " + a.PhotoPath;

            RemovePhotoButton.IsEnabled = !string.IsNullOrWhiteSpace(a.PhotoPath);

            // Fotoğraf zeminde otomatik kontrast hesabı yapılamaz.
            bool manualText = photoMode || a.TextColorMode == TextColorMode.Manual;
            AutoTextColorBox.IsChecked = !photoMode && a.TextColorMode == TextColorMode.Auto;
            AutoTextColorBox.IsEnabled = !photoMode;
            TextColorExpander.IsEnabled = manualText;
            TextColorExpander.IsExpanded = manualText;

            SaturdayRedBox.IsChecked = a.SaturdayIsRed;

            HolidaysBox.IsChecked = S.Holidays.Enabled;

            LoadCalendarColors();

            ClientIdBox.Text = S.Google.ClientId ?? string.Empty;
            ClientSecretBox.Text = S.Google.ClientSecret ?? string.Empty;

            int intervalIndex = Array.FindIndex(
                GooglePoller.Intervals, i => i.Seconds == S.Google.PollIntervalSeconds);
            PollIntervalCombo.SelectedIndex = intervalIndex >= 0 ? intervalIndex : 1;

            TaskColorWheel.SelectedColor = WpfImaging.ToMedia(
                ColorTools.Parse(S.Google.TaskColor, System.Drawing.Color.FromArgb(0x4C, 0x8D, 0xFF)));

            UpdateGoogleUi();

            StartupBox.IsChecked = S.StartWithWindows;
            UpdateStartupNote();

            WallpaperSyncBox.IsChecked = S.SyncSystemWallpaper;

            int logIndex = Array.FindIndex(LogLevels, l => l.Level == S.LogLevel);
            LogLevelCombo.SelectedIndex = logIndex >= 0 ? logIndex : 0;

            DimSlider.Value = a.BackdropDim;
            DimText.Text = $"%{a.BackdropDim}";
            DimPanel.IsEnabled = photoMode;

            int popupOpacity = Math.Clamp(a.DayPopupOpacity, 40, 100);
            PopupOpacitySlider.Value = popupOpacity;
            PopupOpacityText.Text = $"%{popupOpacity}";

            AllScreensRadio.IsChecked = a.DisplayTarget == DisplayTarget.AllScreens;
            PrimaryOnlyRadio.IsChecked = a.DisplayTarget == DisplayTarget.PrimaryOnly;
            SelectedScreensRadio.IsChecked = a.DisplayTarget == DisplayTarget.SelectedScreens;

            RefreshPresetSelection();

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

    private void BuildPresets()
    {
        PresetPanel.Children.Clear();

        foreach (ThemePreset preset in ThemePresets.All)
        {
            var swatch = new Border
            {
                Width = 74,
                Height = 40,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(WpfImaging.ToMedia(
                    ColorTools.Parse(preset.Color, System.Drawing.Color.Gray))),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)),
            };

            var caption = new TextBlock
            {
                Text = preset.Name,
                FontSize = 11,
                Margin = new Thickness(0, 5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var content = new StackPanel();
            content.Children.Add(swatch);
            content.Children.Add(caption);

            var button = new Button
            {
                Content = content,
                Tag = preset,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(6),
                MinWidth = 0,
                ToolTip = $"{preset.Name} · {preset.Color}",
            };

            // Düğmenin içeriği bir yığın olduğu için erişilebilirlik adı
            // kendiliğinden oluşmuyor; açıkça veriyoruz.
            System.Windows.Automation.AutomationProperties.SetName(button, preset.Name);

            button.Click += OnPresetClicked;
            PresetPanel.Children.Add(button);
        }
    }

    private void RefreshPresetSelection()
    {
        foreach (object item in PresetPanel.Children)
        {
            if (item is not Button button || button.Tag is not ThemePreset preset)
            {
                continue;
            }

            bool selected = S.Appearance.BackgroundMode == BackgroundMode.Solid
                && string.Equals(preset.Color, S.Appearance.SolidColor, StringComparison.OrdinalIgnoreCase);

            button.BorderThickness = new Thickness(selected ? 2 : 1);
            button.BorderBrush = selected
                ? SystemColors.HighlightBrush
                : new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0));
        }
    }

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

    /// <summary>Değişikliği masaüstüne uygular; art arda gelen değişiklikler birleştirilir.</summary>
    private void ScheduleApply()
    {
        if (_loading)
        {
            return;
        }

        _applyTimer.Stop();
        _applyTimer.Start();
    }

    // --------------------------------------------------------------- zemin

    private void OnPresetClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ThemePreset preset })
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

    // ---------------------------------------------------------------- yazı

    private void OnAutoTextColorChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        S.Appearance.TextColorMode = AutoTextColorBox.IsChecked == true
            ? TextColorMode.Auto
            : TextColorMode.Manual;

        UpdateTextColorControls();
        ScheduleApply();
    }

    private void UpdateTextColorControls()
    {
        bool photoMode = S.Appearance.BackgroundMode == BackgroundMode.Photo;
        bool manualText = photoMode || S.Appearance.TextColorMode == TextColorMode.Manual;

        AutoTextColorBox.IsEnabled = !photoMode;
        TextColorExpander.IsEnabled = manualText;
        TextColorExpander.IsExpanded = manualText;
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
            _loading = true;
            AutoTextColorBox.IsChecked = false;
            _loading = false;
        }

        ScheduleApply();
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

    /// <summary>
    /// Gün kutusunun opaklığı. Takvim görüntüsünü değiştirmediği için
    /// masaüstüne yeniden yazılmaz; ayar bir sonraki kutu açılışında geçerlidir.
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
        ScheduleApply();
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
    /// Dört renk ayarını denetimlere yansıtır. Ayar boşsa "otomatik" işaretli
    /// kalır ve çarkta o an hesaplanan renk gösterilir; kullanıcı otomatiği
    /// kapattığında elinde makul bir başlangıç rengi olur.
    /// </summary>
    private void LoadCalendarColors()
    {
        CalendarTextColors c = S.Appearance.Colors;
        var theme = Theme.From(S.Appearance);

        Bind(WeekdayAutoBox, WeekdayColorExpander, WeekdayWheel, c.Weekday, theme.Text);
        Bind(HolidayAutoBox, HolidayColorExpander, HolidayWheel, c.Holiday, theme.Holiday);
        Bind(MutedAutoBox, MutedColorExpander, MutedWheel, c.Muted, theme.Muted);
        Bind(TodayCircleAutoBox, TodayCircleExpander, TodayCircleWheel, c.TodayCircle, theme.TodayCircle);

        static void Bind(CheckBox box, Expander expander, ColorWheel wheel,
            string? value, System.Drawing.Color hesaplanan)
        {
            bool otomatik = string.IsNullOrWhiteSpace(value);

            box.IsChecked = otomatik;
            expander.IsEnabled = !otomatik;
            expander.IsExpanded = !otomatik;

            wheel.SelectedColor = WpfImaging.ToMedia(
                otomatik ? hesaplanan : ColorTools.Parse(value, hesaplanan));
        }
    }

    /// <summary>Çarktan bir renk seçildi: ayarı yaz, otomatik işaretini kaldır.</summary>
    private void PickCalendarColor(Action<CalendarTextColors> uygula)
    {
        if (_loading)
        {
            return;
        }

        uygula(S.Appearance.Colors);

        _loading = true;
        try
        {
            WeekdayAutoBox.IsChecked = string.IsNullOrWhiteSpace(S.Appearance.Colors.Weekday);
            HolidayAutoBox.IsChecked = string.IsNullOrWhiteSpace(S.Appearance.Colors.Holiday);
            MutedAutoBox.IsChecked = string.IsNullOrWhiteSpace(S.Appearance.Colors.Muted);
            TodayCircleAutoBox.IsChecked = string.IsNullOrWhiteSpace(S.Appearance.Colors.TodayCircle);
        }
        finally
        {
            _loading = false;
        }

        ScheduleApply();
    }

    private void OnWeekdayAutoChanged(object sender, RoutedEventArgs e)
        => SetCalendarColorAuto(WeekdayAutoBox, WeekdayColorExpander, k => k.Weekday = null);

    private void OnHolidayColorAutoChanged(object sender, RoutedEventArgs e)
        => SetCalendarColorAuto(HolidayAutoBox, HolidayColorExpander, k => k.Holiday = null);

    private void OnMutedColorAutoChanged(object sender, RoutedEventArgs e)
        => SetCalendarColorAuto(MutedAutoBox, MutedColorExpander, k => k.Muted = null);

    private void OnTodayCircleAutoChanged(object sender, RoutedEventArgs e)
        => SetCalendarColorAuto(TodayCircleAutoBox, TodayCircleExpander, k => k.TodayCircle = null);

    /// <summary>
    /// "Otomatik" işaretlendiğinde ayarı boşaltır, kaldırıldığında çarkta duran
    /// rengi yazar; böylece kutunun işaretini kaldırmak görünümü sıçratmaz.
    /// </summary>
    private void SetCalendarColorAuto(CheckBox box, Expander expander, Action<CalendarTextColors> temizle)
    {
        if (_loading)
        {
            return;
        }

        bool otomatik = box.IsChecked == true;
        expander.IsEnabled = !otomatik;
        expander.IsExpanded = !otomatik;

        if (otomatik)
        {
            temizle(S.Appearance.Colors);
            ScheduleApply();

            // Otomatik renk yeniden hesaplandı; çarklar da onu göstersin.
            _applyTimer.Stop();
            _host.ApplyAppearance();
            _loading = true;
            try
            {
                LoadCalendarColors();
            }
            finally
            {
                _loading = false;
            }

            return;
        }

        // Otomatik kapatıldı: çarkta duran renk artık kalıcı seçim.
        ColorWheel wheel = (ColorWheel)expander.Content;
        PickCalendarColor(k =>
        {
            if (ReferenceEquals(wheel, WeekdayWheel))
            {
                k.Weekday = ColorWheel.ToHex(wheel.SelectedColor);
            }
            else if (ReferenceEquals(wheel, HolidayWheel))
            {
                k.Holiday = ColorWheel.ToHex(wheel.SelectedColor);
            }
            else if (ReferenceEquals(wheel, MutedWheel))
            {
                k.Muted = ColorWheel.ToHex(wheel.SelectedColor);
            }
            else
            {
                k.TodayCircle = ColorWheel.ToHex(wheel.SelectedColor);
            }
        });
    }

    // --------------------------------------------------------------- Google

    private void UpdateGoogleUi()
    {
        bool connected = _host.GoogleConnected;

        ConnectButton.Content = connected ? "Yeniden bağlan" : "Google'a bağlan";
        DisconnectButton.IsEnabled = connected;
        GoogleDetailsPanel.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;

        if (!connected)
        {
            GoogleStatusText.Text = string.IsNullOrWhiteSpace(S.Google.ClientId)
                ? "Bağlı değil. Önce yukarıdaki iki değeri yapıştırın."
                : "Bağlı değil. \"Google'a bağlan\" düğmesine basın; tarayıcıda onay ekranı açılacak.";
            return;
        }

        string durum = _host.GoogleOnline ? "bağlı" : "bağlı, şu an çevrimdışı (yerel kopya gösteriliyor)";

        GoogleStatusText.Text =
            $"{_host.GoogleAccountEmail ?? "Hesap"} · {durum}"
            + $"\nBu ayda {_host.GoogleEventCount} etkinlik, {_host.GoogleTaskCount} görev.";

        BuildSourceList(CalendarListPanel, _host.GoogleCalendars.Select(c => (c.Id, c.Name)).ToList(),
            S.Google.SelectedCalendarIds);

        BuildSourceList(TaskListPanel, _host.GoogleTaskLists.Select(l => (l.Id, l.Name)).ToList(),
            S.Google.SelectedTaskListIds);
    }

    /// <summary>
    /// Takvim ya da görev listesi seçim kutularını kurar. Seçim listesi boşsa
    /// "hepsi" anlamına gelir; kullanıcı ilk kez bir kutunun işaretini kaldırınca
    /// liste gerçek seçimle doldurulur.
    /// </summary>
    private void BuildSourceList(StackPanel panel, List<(string Id, string Name)> sources, List<string> selected)
    {
        panel.Children.Clear();

        if (sources.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Henüz yüklenmedi.",
                Opacity = 0.65,
            });
            return;
        }

        foreach ((string id, string name) in sources)
        {
            var box = new CheckBox
            {
                Content = name,
                IsChecked = selected.Count == 0 || selected.Contains(id),
                Tag = id,
                Margin = new Thickness(0, 3, 0, 3),
            };

            box.Checked += OnSourceToggled;
            box.Unchecked += OnSourceToggled;
            panel.Children.Add(box);
        }
    }

    private void OnSourceToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not CheckBox { Tag: string } box)
        {
            return;
        }

        bool isCalendar = ReferenceEquals(box.Parent, CalendarListPanel);
        StackPanel panel = isCalendar ? CalendarListPanel : TaskListPanel;
        List<string> target = isCalendar ? S.Google.SelectedCalendarIds : S.Google.SelectedTaskListIds;

        target.Clear();
        foreach (object child in panel.Children)
        {
            if (child is CheckBox { IsChecked: true, Tag: string id })
            {
                target.Add(id);
            }
        }

        // Hepsi işaretliyse listeyi boş bırakmak "hepsi" demek; ileride yeni
        // takvim eklenirse kendiliğinden dahil olsun.
        if (target.Count == panel.Children.Count)
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

    private async void OnConnectGoogle(object sender, RoutedEventArgs e)
    {
        OnGoogleCredentialsChanged(sender, e);

        ConnectButton.IsEnabled = false;
        GoogleStatusText.Text = "Tarayıcıda onay bekleniyor…";

        try
        {
            SignInResult result = await _host.ConnectGoogleAsync();

            if (!result.Success)
            {
                GoogleStatusText.Text = result.Message;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Google bağlantısı kurulamadı.", ex);
            GoogleStatusText.Text = "Bağlantı kurulamadı: " + ex.Message;
        }
        finally
        {
            ConnectButton.IsEnabled = true;
            LoadFromSettings();
        }
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
            : "Açılış kaydı başka bir dosyayı gösteriyor. Bu kutuyu kapatıp yeniden açarsanız "
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

        // Bekleyen bir değişiklik varsa kapanmadan önce diske yazılsın.
        _host.ApplyAppearance();

        base.OnClosed(e);
    }
}
