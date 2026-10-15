using System.Windows;
using Kalender.Core;

namespace Kalender.Ui;

/// <summary>Pencerenin hangi iş için açıldığı.</summary>
internal enum SetupMode
{
    Install,
    Uninstall,

    /// <summary>Çalışan uygulamanın açtığı "yeni sürüm var" penceresi.</summary>
    Update,
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
    private readonly UpdateService? _updates;
    private bool _done;

    /// <summary>Kurulu sürüm bu dosyanınkinden yeni: kurulum yapılmaz, kurulu olan açılır.</summary>
    private bool _newerInstalled;

    /// <summary>
    /// Güncelleme kipi: çalışan uygulama yeni sürüm bulunca bu pencereyi açar.
    /// "Güncelle" yeni sürümü indirip kurar, "Sonra" pencereyi kapatır; bir
    /// hafta sonra yeniden hatırlatılır. Pencere öne gelir ama klavye odağını
    /// almaz: kendiliğinden açıldığı için kullanıcı o sırada başka bir yere
    /// yazıyor olabilir, bir tuş yanlışlıkla güncellemeyi başlatmamalı.
    /// </summary>
    internal SetupWindow(UpdateService updates)
    {
        InitializeComponent();

        _mode = SetupMode.Update;
        _updates = updates;

        ShowActivated = false;
        Topmost = true;

        Title = "Kalender güncellemesi";
        PathBox.Visibility = Visibility.Collapsed;

        _updates.Changed += RefreshUpdate;
        RefreshUpdate();
    }

    /// <summary>Güncelleme kipinde yazıları ve düğmeleri güncellemenin o anki durumuna uydurur.</summary>
    private void RefreshUpdate()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(RefreshUpdate);
            return;
        }

        UpdateService u = _updates!;
        if (u.Available is not UpdateInfo info)
        {
            // Sonraki bir denetim yeni sürüm göstermiyor (sürüm geri çekilmiş olabilir).
            if (IsLoaded)
            {
                Close();
            }

            return;
        }

        string yeni = Installer.VersionText(info.Version);

        switch (u.State)
        {
            case UpdateState.Downloading:
                HeadingText.Text = "Güncelleniyor…";
                BodyText.Text = $"Kalender {yeni} indiriliyor · %{u.Progress}";
                NoteText.Text = "İndirme bitince Kalender kapanıp yeni sürümle kendiliğinden açılır.";
                break;

            case UpdateState.Installing:
                HeadingText.Text = "Kuruluyor…";
                BodyText.Text = "Kalender birazdan yeni sürümle yeniden açılacak.";
                NoteText.Text = string.Empty;
                break;

            case UpdateState.Failed:
                HeadingText.Text = "Güncelleme yapılamadı";
                BodyText.Text = u.Error ?? "Yeni sürüm indirilemedi.";
                NoteText.Text = "İnternet bağlantınızı denetleyip yeniden deneyebilirsiniz. "
                    + "Şimdi olmazsa bir hafta sonra yeniden hatırlatılır.";
                break;

            default:
                HeadingText.Text = "Kalender'in yeni sürümü çıktı";
                BodyText.Text = $"Bu bilgisayarda {Installer.VersionText(UpdateService.CurrentVersion)} sürümü kurulu; "
                    + $"yeni sürüm {yeni}. Güncelle'ye basınca yeni sürüm indirilip kurulur ve Kalender "
                    + "kendiliğinden yeniden açılır. Ayarlarınıza dokunulmaz.";
                NoteText.Text = "Şimdi güncellemezseniz bir hafta sonra yeniden hatırlatılır. Ayarlar "
                    + "penceresinin sol altındaki bildirimden istediğiniz zaman güncelleyebilirsiniz.";
                break;
        }

        bool busy = u.State is UpdateState.Downloading or UpdateState.Installing;
        PrimaryButton.Content = u.State == UpdateState.Failed ? "Yeniden dene" : "Güncelle";
        PrimaryButton.IsEnabled = !busy;
        CancelButton.Content = u.State == UpdateState.Failed ? "Kapat" : "Sonra";
        CancelButton.IsEnabled = !busy;
    }

    internal SetupWindow(SetupMode mode)
    {
        InitializeComponent();

        _mode = mode;
        PathText.Text = Installer.AppFolder;

        if (mode == SetupMode.Install)
        {
            ShowInstall(Installer.IsInstalled, Installer.InstalledVersion);
        }
        else
        {
            Title = "Kalender kaldırma";
            HeadingText.Text = "Kalender kaldırılacak";
            BodyText.Text = "Kalender'den önceki duvar kâğıdınız geri yüklenecek, Başlat menüsü "
                + "kısayolu ve açılışta başlatma kaydı silinecek.";

            NoteText.Text = "Ayarlarınızı saklarsanız Kalender'i sonra yeniden kurduğunuzda "
                + "her şey yerinde olur.";

            RemoveDataBox.Visibility = Visibility.Visible;
            PrimaryButton.Content = "Kaldır";
        }
    }

    /// <summary>
    /// Kurulum kipinin yazıları: ilk kurulum, güncelleme ya da aynı sürümü
    /// yeniden kurma. Kurulu sürüm bu dosyanınkinden yeniyse kurulum yapılmaz
    /// (bkz. <see cref="Installer.NewerInstalledVersion"/>); pencere kurulu
    /// Kalender'i açmayı ya da kapatmayı önerir.
    /// </summary>
    /// <param name="installed">Kalender kurulu mu?</param>
    /// <param name="installedVersion">Kurulu sürüm; okunamadıysa null.</param>
    private void ShowInstall(bool installed, Version? installedVersion)
    {
        _newerInstalled = installedVersion is not null && installedVersion > UpdateService.CurrentVersion;
        if (_newerInstalled)
        {
            Title = "Kalender kurulumu";
            HeadingText.Text = $"Bu bilgisayarda daha yeni bir sürüm ({Installer.VersionText(installedVersion!)}) kurulu";
            BodyText.Text = $"Bu dosya Kalender'in eski bir sürümü ({Installer.Version}). Eski sürüm, yeni "
                + "sürümün ayarlarını okuyamayabileceği için kurulum yapılmaz.";
            NoteText.Text = "Kurulu Kalender'i açabilir ya da bu pencereyi kapatabilirsiniz.";
            PathBox.Visibility = Visibility.Collapsed;
            PrimaryButton.Content = "Kalender'i aç";
            CancelButton.Content = "Kapat";
            return;
        }

        if (!installed)
        {
            Title = "Kalender kurulumu";
            HeadingText.Text = "Kalender bu bilgisayara kurulacak";
            BodyText.Text = "Kalender yalnızca sizin kullanıcı hesabınıza kurulur. Yönetici hakkı istenmez, "
                + "sistem klasörlerine dosya yazılmaz.";
            PrimaryButton.Content = "Kur";
        }
        else if (installedVersion == UpdateService.CurrentVersion)
        {
            Title = "Kalender kurulumu";
            HeadingText.Text = $"Kalender {Installer.Version} zaten kurulu";
            BodyText.Text = "Bu bilgisayarda aynı sürüm kurulu. Yeniden kurarsanız program dosyası yeniden "
                + "yazılır; ayarlarınıza dokunulmaz.";
            PrimaryButton.Content = "Yeniden kur";
        }
        else
        {
            // Kurulu sürüm daha eski ya da okunamadı.
            Title = "Kalender güncelleniyor";
            HeadingText.Text = "Kalender güncellenecek";
            BodyText.Text = installedVersion is null
                ? $"Kurulu sürümün üzerine {Installer.Version} sürümü yazılacak. Ayarlarınıza dokunulmaz."
                : $"Kurulu {Installer.VersionText(installedVersion)} sürümünün üzerine {Installer.Version} "
                  + "sürümü yazılacak. Ayarlarınıza dokunulmaz.";
            PrimaryButton.Content = "Güncelle";
        }

        NoteText.Text = "Kurulumdan sonra Başlat menüsüne kısayol eklenir ve Kalender "
            + "Windows açılışında kendiliğinden başlar. Bunu Ayarlar'dan kapatabilirsiniz.";

        PathBox.Visibility = Visibility.Visible;
        CancelButton.Content = "Vazgeç";
    }

    private async void OnPrimary(object sender, RoutedEventArgs e)
    {
        if (_mode == SetupMode.Update)
        {
            // İndirir, doğrular ve kurucuyu başlatır; başarılı olursa uygulama kapanır.
            await _updates!.InstallAsync(fromPrompt: true);
            return;
        }

        if (_done)
        {
            Close();
            return;
        }

        if (_newerInstalled)
        {
            // Eskiye dönülmez; kurulu (daha yeni) Kalender açılır.
            Installer.LaunchInstalled();
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

    protected override void OnClosed(EventArgs e)
    {
        if (_updates is not null)
        {
            _updates.Changed -= RefreshUpdate;
        }

        base.OnClosed(e);
    }
}
