using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kalender.Ui;

/// <summary>
/// Ton/doygunluk çarkı + parlaklık şeridi + renk kodu kutusu.
///
/// Hazır bir denetim yerine kendimiz çizdik: proje üçüncü taraf paket
/// eklemeden ilerliyor ve çarkın davranışını (yapışma, canlı uygulama)
/// tam olarak istediğimiz gibi tutmak istiyoruz.
/// </summary>
public partial class ColorWheel : UserControl
{
    private const int WheelSize = 200;
    private const double Radius = WheelSize / 2.0;

    private static BitmapSource? _wheelCache;

    private double _hue;          // 0-360
    private double _saturation;   // 0-1
    private double _value = 1;    // 0-1
    private bool _updating;

    public ColorWheel()
    {
        InitializeComponent();

        WheelImage.Source = _wheelCache ??= BuildWheel();
        Loaded += (_, _) => RefreshVisuals();
    }

    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor),
        typeof(Color),
        typeof(ColorWheel),
        new FrameworkPropertyMetadata(Colors.White,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

    /// <summary>Şu an seçili renk.</summary>
    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    /// <summary>
    /// Renk değiştiğinde tetiklenir. Kullanıcı çarkta gezinirken de tetiklenir;
    /// böylece değişiklik masaüstüne canlı yansıtılabilir.
    /// </summary>
    public event Action<Color>? ColorChanged;

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ColorWheel wheel || wheel._updating)
        {
            return;
        }

        var color = (Color)e.NewValue;
        (wheel._hue, wheel._saturation, wheel._value) = ToHsv(color);
        wheel.RefreshVisuals();
    }

    // ------------------------------------------------------------------ çark

    private static BitmapSource BuildWheel()
    {
        var bitmap = new WriteableBitmap(WheelSize, WheelSize, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[WheelSize * WheelSize * 4];

        for (int y = 0; y < WheelSize; y++)
        {
            double dy = y - Radius + 0.5;
            for (int x = 0; x < WheelSize; x++)
            {
                double dx = x - Radius + 0.5;
                double distance = Math.Sqrt((dx * dx) + (dy * dy)) / Radius;
                int i = ((y * WheelSize) + x) * 4;

                if (distance > 1.0)
                {
                    continue;
                }

                double angle = (Math.Atan2(dy, dx) * 180.0 / Math.PI) + 90.0;
                if (angle < 0)
                {
                    angle += 360.0;
                }

                Color color = FromHsv(angle, distance, 1.0);

                // Kenarda yumuşak geçiş; çarkın çevresi tırtıklı görünmesin. Geçiş
                // karartma örtüsünün kırpma dairesi içinde biter: dışına taşan kenar
                // pikselleri koyu renklerde karartılmadan parlak bir hale olarak kalırdı.
                double alpha = distance <= 0.98 ? 1.0 : (1.0 - distance) / 0.02;

                pixels[i + 0] = color.B;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.R;
                pixels[i + 3] = (byte)Math.Round(alpha * 255);
            }
        }

        bitmap.WritePixels(new Int32Rect(0, 0, WheelSize, WheelSize), pixels, WheelSize * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private void RefreshVisuals()
    {
        // Parlaklık, çarkın üzerine konan siyah örtüyle gösteriliyor.
        WheelShade.Opacity = 1 - _value;

        double r = _saturation * Radius;
        double angle = (_hue - 90.0) * Math.PI / 180.0;
        double x = Radius + (r * Math.Cos(angle));
        double y = Radius + (r * Math.Sin(angle));

        Canvas.SetLeft(Marker, x - (Marker.Width / 2));
        Canvas.SetTop(Marker, y - (Marker.Height / 2));
        Marker.Fill = new SolidColorBrush(Current);

        ValueBar.Background = new LinearGradientBrush(
            FromHsv(_hue, _saturation, 1.0),
            Colors.Black,
            new Point(0, 0),
            new Point(0, 1));

        Canvas.SetTop(ValueThumb, ((1 - _value) * WheelSize) - (ValueThumb.Height / 2));

        Preview.Background = new SolidColorBrush(Current);

        // Kutu, açılır pencerede odaktayken de çarkla eşit kalmalı: yoksa
        // kapanırken (odak kutudan çıkınca) eski kod okunur ve çarkta seçilen
        // renk geri alınırdı. Yazı yazılırken bu yöntem çağrılmaz.
        HexBox.Text = ToHex(Current);
        if (HexBox.IsKeyboardFocusWithin)
        {
            HexBox.SelectAll(); // Yazılan kod yine eskisinin tamamının yerine geçer.
        }
    }

    private Color Current => FromHsv(_hue, _saturation, _value);

    private void Commit()
    {
        Color color = Current;

        _updating = true;
        SelectedColor = color;
        _updating = false;

        RefreshVisuals();
        ColorChanged?.Invoke(color);
    }

    // ------------------------------------------------------------- etkileşim

    private void OnWheelMouseDown(object sender, MouseButtonEventArgs e)
    {
        WheelSurface.CaptureMouse();
        PickFromWheel(e.GetPosition(WheelSurface));
    }

    private void OnWheelMouseMove(object sender, MouseEventArgs e)
    {
        if (WheelSurface.IsMouseCaptured)
        {
            PickFromWheel(e.GetPosition(WheelSurface));
        }
    }

    private void OnWheelMouseUp(object sender, MouseButtonEventArgs e) => WheelSurface.ReleaseMouseCapture();

    private void PickFromWheel(Point point)
    {
        double dx = point.X - Radius;
        double dy = point.Y - Radius;
        double distance = Math.Sqrt((dx * dx) + (dy * dy)) / Radius;

        double angle = (Math.Atan2(dy, dx) * 180.0 / Math.PI) + 90.0;
        if (angle < 0)
        {
            angle += 360.0;
        }

        _hue = angle;
        _saturation = Math.Clamp(distance, 0, 1);
        Commit();
    }

    private void OnValueMouseDown(object sender, MouseButtonEventArgs e)
    {
        ValueSurface.CaptureMouse();
        PickValue(e.GetPosition(ValueSurface));
    }

    private void OnValueMouseMove(object sender, MouseEventArgs e)
    {
        if (ValueSurface.IsMouseCaptured)
        {
            PickValue(e.GetPosition(ValueSurface));
        }
    }

    private void OnValueMouseUp(object sender, MouseButtonEventArgs e) => ValueSurface.ReleaseMouseCapture();

    private void PickValue(Point point)
    {
        _value = Math.Clamp(1 - (point.Y / WheelSize), 0, 1);
        Commit();
    }

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyHex();
            e.Handled = true;
        }
    }

    private void OnHexLostFocus(object sender, RoutedEventArgs e) => ApplyHex();

    private void ApplyHex()
    {
        // Kod değişmediyse (ör. kutu yalnızca odak alıp bıraktıysa) seçim
        // yapılmış sayılmaz; yoksa otomatik bir renk durup dururken elle
        // seçilmişe dönerdi. Geçersiz yazıldıysa sessizce eski değere dönülür.
        if (TryParseHex(HexBox.Text, out Color color) && color != Current)
        {
            (_hue, _saturation, _value) = ToHsv(color);
            Commit();
        }
        else
        {
            HexBox.Text = ToHex(Current);
        }
    }

    /// <summary>Klavye odağını renk kodu kutusuna verir; yazılan kod eskisinin yerine geçer.</summary>
    public void FocusHexBox()
    {
        HexBox.Focus();
        HexBox.SelectAll();
    }

    // --------------------------------------------------------------- yardımcı

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static bool TryParseHex(string? text, out Color color)
    {
        color = Colors.Black;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string s = text.Trim().TrimStart('#');
        if (s.Length == 3)
        {
            s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
        }

        if (s.Length != 6
            || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
        {
            return false;
        }

        color = Color.FromRgb((byte)(value >> 16), (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF));
        return true;
    }

    public static Color FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);

        double c = v * s;
        double x = c * (1 - Math.Abs(((h / 60.0) % 2) - 1));
        double m = v - c;

        (double r, double g, double b) = (h / 60) switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    public static (double H, double S, double V) ToHsv(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0;
        if (delta > 0.00001)
        {
            if (Math.Abs(max - r) < 0.00001)
            {
                h = 60 * (((g - b) / delta) % 6);
            }
            else if (Math.Abs(max - g) < 0.00001)
            {
                h = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                h = 60 * (((r - g) / delta) + 4);
            }
        }

        if (h < 0)
        {
            h += 360;
        }

        double s = max <= 0 ? 0 : delta / max;
        return (h, s, max);
    }
}
