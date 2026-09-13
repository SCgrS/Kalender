using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Kalender.Ui;

/// <summary>
/// Rengi küçük bir örnek ve renk koduyla gösteren düğme; tıklanınca hemen
/// altında renk çarkı açılır.
///
/// Çark yalnızca istenince göründüğü için ayarlar sayfası sakin kalır.
/// Çarkta gezinirken <see cref="ColorPicked"/> her adımda tetiklenir; böylece
/// seçim masaüstüne canlı yansır. Dışarı tıklamak, Esc ya da "Tamam" kutuyu kapatır.
/// </summary>
public partial class ColorPickerButton : UserControl
{
    public ColorPickerButton()
    {
        InitializeComponent();

        Flyout.CustomPopupPlacementCallback = PlaceFlyout;
        Wheel.ColorChanged += OnWheelColorChanged;
        UpdateChip();
    }

    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor),
        typeof(Color),
        typeof(ColorPickerButton),
        new FrameworkPropertyMetadata(Colors.White, OnAppearanceChanged));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(ColorPickerButton),
        new FrameworkPropertyMetadata("Renk", OnAppearanceChanged));

    /// <summary>
    /// Düğmede gösterilen renk. Koddan verilmesi <see cref="ColorPicked"/>'i
    /// tetiklemez; yalnızca kullanıcının seçimi tetikler.
    /// </summary>
    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    /// <summary>Açılır kutunun başlığı; ekran okuyucu da düğmeyi bu adla okur.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Kullanıcı çarktan ya da renk kodundan bir renk seçti.</summary>
    public event Action<Color>? ColorPicked;

    private static void OnAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ColorPickerButton)d).UpdateChip();

    /// <summary>Kutuyu açılmaya hazırlar: başlığı yazar, çarkı düğmenin rengine getirir.</summary>
    internal void PrepareFlyout()
    {
        FlyoutTitle.Text = Title;
        Wheel.SelectedColor = SelectedColor;
    }

    private void OnChipClick(object sender, RoutedEventArgs e)
    {
        PrepareFlyout();
        Flyout.IsOpen = true;
    }

    private void OnFlyoutOpened(object? sender, EventArgs e)
    {
        // Kutu açıkken düğme tıklamayı almasın: dışarı tıklama kutuyu kapatır,
        // aynı tıklama düğmeye düşüp kutuyu hemen yeniden açmasın.
        Chip.IsHitTestVisible = false;
        Wheel.FocusHexBox();
    }

    private void OnFlyoutClosed(object? sender, EventArgs e)
    {
        Chip.IsHitTestVisible = true;

        // WPF odağı önce kendisi yerleştirsin; ardından bakılır.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, RestoreFocusAfterClose);
    }

    /// <summary>
    /// Kutu dışarı tıklamayla ya da pencereden çıkılınca kapandıysa klavye odağı
    /// kapanan kutuda (görünmeyen renk kodu kutusunda) ya da pencerenin kendisinde
    /// kalır; düğmeye döner. Tıklama odak alabilen başka bir denetime düştüyse
    /// odak onda kalır.
    /// </summary>
    private void RestoreFocusAfterClose()
    {
        if (Flyout.IsOpen)
        {
            return;
        }

        switch (Keyboard.FocusedElement)
        {
            case null:
                // Pencereden çıkılınca kapandı. Klavye odağı vermek pencereyi öne
                // alırdı; pencereye dönülünce odağın düğmede olması yeter.
                FocusManager.SetFocusedElement(FocusManager.GetFocusScope(Chip), Chip);
                break;

            // Kapanan kutunun içeriği görsel ağaçtan ayrıldığı için IsKeyboardFocusWithin
            // doğru sonuç vermez; odaklı öğenin kutunun içinde olup olmadığına bakılır.
            case Window:
            case Visual odakli when Flyout.Child.IsAncestorOf(odakli):
                Chip.Focus();
                break;
        }
    }

    /// <summary>
    /// Kutu açıkken fare tekerleği sayfayı kaydırmasın: kutu yerinde durur,
    /// düğmesi ise kayıp giderdi. Tekerlek olayı kutunun içinden de, kutu fareyi
    /// yakaladığı için dışarıdan da Popup'tan geçerek sayfaya ulaşıyor.
    /// </summary>
    private void OnFlyoutMouseWheel(object sender, MouseWheelEventArgs e) => e.Handled = true;

    private void OnFlyoutKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseFlyout();
            e.Handled = true;
        }
    }

    private void OnDone(object sender, RoutedEventArgs e) => CloseFlyout();

    /// <summary>Kutuyu kapatır ve klavye odağını düğmeye geri verir.</summary>
    private void CloseFlyout()
    {
        Flyout.IsOpen = false;
        Chip.Focus();
    }

    private void OnWheelColorChanged(Color color)
    {
        SelectedColor = color;
        ColorPicked?.Invoke(color);
    }

    private void UpdateChip()
    {
        string hex = ColorWheel.ToHex(SelectedColor);
        Swatch.Background = new SolidColorBrush(SelectedColor);
        HexText.Text = hex;
        AutomationProperties.SetName(Chip, $"{Title}: {hex}");
    }

    /// <summary>
    /// Kutuyu düğmenin sağ kenarına hizalı olarak altına, yer yoksa üstüne
    /// koyar. Kutunun çevresindeki gölge payı hizada hesaba katılır. Boyutlar
    /// ekran ölçeğinde gelebildiği için pay, düğmenin gelen genişliğiyle
    /// kendi genişliği arasındaki oranla aynı ölçeğe çevrilir.
    /// </summary>
    private CustomPopupPlacement[] PlaceFlyout(Size popupSize, Size targetSize, Point offset)
    {
        double scale = Chip.ActualWidth > 0 ? targetSize.Width / Chip.ActualWidth : 1;
        Thickness pay = ((FrameworkElement)Flyout.Child).Margin;
        const double aralik = 4;

        double x = targetSize.Width - popupSize.Width + (pay.Right * scale);

        return new[]
        {
            new CustomPopupPlacement(
                new Point(x, targetSize.Height + ((aralik - pay.Top) * scale)), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(
                new Point(x, -popupSize.Height + ((pay.Bottom - aralik) * scale)), PopupPrimaryAxis.Horizontal),
        };
    }
}
