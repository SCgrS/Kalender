using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Kalender.Core;
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

        BackgroundWheel.ColorChanged += OnBackgroundColorPicked;
        TextWheel.ColorChanged += OnTextColorPicked;

        BuildPresets();
        BuildMonitorRows();
        LoadFromSettings();

        // Çözünürlük ya da ölçek değişirse satırlardaki ölçüler eskir; taşıma
        // ekranı da o eski ölçülerle açılıp takvimi ekran dışına atardı.
        _host.MonitorsChanged += OnMonitorsChanged;
    }

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

            DimSlider.Value = a.BackdropDim;
            DimText.Text = $"%{a.BackdropDim}";
            DimPanel.IsEnabled = photoMode;

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

    // -------------------------------------------------------------- kapanış

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _host.MonitorsChanged -= OnMonitorsChanged;
        _applyTimer.Stop();

        // Bekleyen bir değişiklik varsa kapanmadan önce diske yazılsın.
        _host.ApplyAppearance();

        base.OnClosed(e);
    }
}
