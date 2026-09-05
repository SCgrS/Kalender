using System.Windows;
using Kalender.Core;

namespace Kalender.Ui;

/// <summary>Pencerenin hangi iş için açıldığı.</summary>
internal enum SetupMode
{
    Install,
    Uninstall,
}

/// <summary>
/// Kurulum ve kaldırma penceresi.
///
/// Kasten sade: tek ekran, tek düğme. Kalender kullanıcının kendi alanına
/// kurulduğu için seçilecek bir klasör, kabul edilecek bir sözleşme ya da
/// yönetici onayı yok.
/// </summary>
public partial class SetupWindow : Window
{
    private readonly SetupMode _mode;
    private bool _done;

    internal SetupWindow(SetupMode mode)
    {
        InitializeComponent();

        _mode = mode;
        PathText.Text = Installer.AppFolder;

        if (mode == SetupMode.Install)
        {
            bool update = Installer.IsInstalled;

            Title = update ? "Kalender güncelleniyor" : "Kalender kurulumu";
            HeadingText.Text = update ? "Kalender güncellenecek" : "Kalender bu bilgisayara kurulacak";
            BodyText.Text = update
                ? $"Kurulu sürümün üzerine {Installer.Version} sürümü yazılacak. Ayarlarınıza dokunulmaz."
                : "Kalender yalnızca sizin kullanıcı hesabınıza kurulur. Yönetici hakkı istenmez, "
                  + "sistem klasörlerine dosya yazılmaz.";

            NoteText.Text = "Kurulumdan sonra Başlat menüsüne kısayol eklenir ve Kalender "
                + "Windows açılışında kendiliğinden başlar. İkisini de Ayarlar'dan kapatabilirsiniz.";

            PrimaryButton.Content = update ? "Güncelle" : "Kur";
        }
        else
        {
            Title = "Kalender kaldırma";
            HeadingText.Text = "Kalender kaldırılacak";
            BodyText.Text = "Kalender'den önceki duvar kağıdınız geri yüklenecek, Başlat menüsü "
                + "kısayolu ve açılışta başlatma kaydı silinecek.";

            NoteText.Text = "Ayarlarınızı saklarsanız Kalender'i sonra yeniden kurduğunuzda "
                + "her şey yerinde olur.";

            RemoveDataBox.Visibility = Visibility.Visible;
            PrimaryButton.Content = "Kaldır";
        }
    }

    private void OnPrimary(object sender, RoutedEventArgs e)
    {
        if (_done)
        {
            Close();
            return;
        }

        PrimaryButton.IsEnabled = false;
        CancelButton.IsEnabled = false;

        SetupResult result = _mode == SetupMode.Install
            ? Installer.Install()
            : Installer.Uninstall(RemoveDataBox.IsChecked == true);

        _done = true;

        HeadingText.Text = result.Success
            ? (_mode == SetupMode.Install ? "Kuruldu" : "Kaldırıldı")
            : "İşlem tamamlanamadı";

        BodyText.Text = result.Message;
        NoteText.Text = string.Empty;
        RemoveDataBox.Visibility = Visibility.Collapsed;
        PathBox.Visibility = Visibility.Collapsed;

        PrimaryButton.Content = "Kapat";
        PrimaryButton.IsEnabled = true;

        if (result.Success && _mode == SetupMode.Install)
        {
            Installer.LaunchInstalled();

            // Kurulan sürüm açıldı; bu pencereyi tutmanın anlamı yok.
            Close();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
