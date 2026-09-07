using Kalender.Core;

namespace Kalender.Interop;

/// <summary>Açılır menüdeki tek bir satır. <see cref="Id"/> 0 ise ayraçtır.</summary>
/// <param name="Id">Seçildiğinde bildirilecek kimlik.</param>
/// <param name="Text">Görünen metin.</param>
/// <param name="Enabled">Tıklanabilir mi?</param>
/// <param name="Checked">Yanında işaret olsun mu?</param>
internal sealed record PopupMenuItem(uint Id, string Text, bool Enabled = true, bool Checked = false)
{
    public static PopupMenuItem Separator => new(0, string.Empty);

    public bool IsSeparator => Id == 0;
}

/// <summary>
/// Windows'un kendi açılır menüsünü gösterir. Tepsi simgesi de, takvimin
/// üzerindeki sağ tık menüsü de bunu kullanır; böylece ikisi de sistemin
/// menüleriyle birebir aynı görünür ve temaya kendiliğinden uyar.
/// </summary>
internal static class PopupMenu
{
    /// <summary>Windows'un menü pencerelerinin sınıf adı.</summary>
    private const string MenuClassName = "#32768";

    /// <summary>
    /// Menüyü ekran koordinatında açar ve seçilen satırın kimliğini döndürür.
    /// Hiçbir şey seçilmezse 0 döner.
    /// </summary>
    public static uint Show(IntPtr owner, int x, int y, IReadOnlyList<PopupMenuItem> items)
    {
        if (items.Count == 0)
        {
            return 0;
        }

        IntPtr menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return 0;
        }

        try
        {
            foreach (PopupMenuItem item in items)
            {
                if (item.IsSeparator)
                {
                    NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, UIntPtr.Zero, null);
                    continue;
                }

                uint flags = NativeMethods.MF_STRING;
                if (!item.Enabled)
                {
                    flags |= NativeMethods.MF_GRAYED | NativeMethods.MF_DISABLED;
                }

                if (item.Checked)
                {
                    flags |= NativeMethods.MF_CHECKED;
                }

                NativeMethods.AppendMenu(menu, flags, new UIntPtr(item.Id), item.Text);
            }

            // Menünün dışına tıklanınca (ve Esc'e basınca) kapanabilmesi için
            // sahibi pencerenin ön planda olması şart. Aksi hâlde menü çiziliyor
            // ama girdi almıyor: ne tıklama ne Esc kapatıyor, ekranda asılı kalıyor.
            // Kabuğun (ya da başka bir uygulamanın) açık menüsü varsa önce onu
            // kapatıyoruz; yoksa iki menü üst üste kalıyor.
            CloseOtherMenus();
            BringToForeground(owner);

            uint secim = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_NONOTIFY,
                x, y, owner, IntPtr.Zero);

            // Menü kapandıktan sonra sahibi pencere serbest bırakılır.
            NativeMethods.PostMessage(owner, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);

            return secim;
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    /// <summary>
    /// Başka bir uygulamanın açık menüsünü kapatır.
    ///
    /// Takvimin üzerindeki sağ tıklamayı alt düzey kancayla yutuyoruz. Kabuk o
    /// tıklamayı hiç görmediği için, o sırada açık duran masaüstü menüsü
    /// kendiliğinden kapanmıyordu: Windows'un menüsü solda, Kalender'inki sağda,
    /// ikisi birden ekranda kalıyordu.
    ///
    /// Menü penceresine WM_CLOSE gönderiyoruz. WM_CANCELMODE denendi ve bu
    /// yapıda menüyü kapatmıyor; WM_CLOSE kapatıyor. PostMessage kullanılıyor,
    /// böylece kabuk yanıt vermese bile burada beklemiyoruz.
    /// </summary>
    private static void CloseOtherMenus()
    {
        try
        {
            IntPtr menu = IntPtr.Zero;
            while ((menu = NativeMethods.FindWindowEx(IntPtr.Zero, menu, MenuClassName, null)) != IntPtr.Zero)
            {
                if (NativeMethods.IsWindowVisible(menu))
                {
                    NativeMethods.PostMessage(menu, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Açık menüler kapatılamadı: " + ex.Message);
        }
    }

    /// <summary>
    /// Pencereyi ön plana alır.
    ///
    /// Düz <c>SetForegroundWindow</c> yetmiyor: Windows, ön planda olmayan bir
    /// sürecin bu çağrısını sessizce yok sayıyor (true dönse bile ön plan
    /// değişmiyor). Takvim menüsünde bu tam olarak yaşanıyor — sağ tıklamayı
    /// alt düzey kancayla yutuyoruz, dolayısıyla kabuk ön planda kalıyor ve
    /// Kalender "son girdiyi alan süreç" sayılmıyor.
    ///
    /// Belgelenmiş çözüm, ön plandaki pencerenin iş parçacığıyla girdi
    /// kuyruğunu geçici olarak birleştirmek. Bağ yalnızca çağrı boyunca
    /// duruyor; menü açılmadan önce çözülüyor ki kabuğun girdi kuyruğu
    /// bizim menümüzün ömrü boyunca bize bağlı kalmasın.
    /// </summary>
    private static void BringToForeground(IntPtr owner)
    {
        if (NativeMethods.SetForegroundWindow(owner)
            && NativeMethods.GetForegroundWindow() == owner)
        {
            return;
        }

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return;
        }

        uint digerIsParcacigi = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        uint bizimIsParcacigi = NativeMethods.GetCurrentThreadId();

        if (digerIsParcacigi == 0 || digerIsParcacigi == bizimIsParcacigi)
        {
            return;
        }

        bool bagli = NativeMethods.AttachThreadInput(bizimIsParcacigi, digerIsParcacigi, true);
        try
        {
            NativeMethods.SetForegroundWindow(owner);
        }
        finally
        {
            if (bagli)
            {
                NativeMethods.AttachThreadInput(bizimIsParcacigi, digerIsParcacigi, false);
            }
        }

        if (NativeMethods.GetForegroundWindow() != owner)
        {
            Log.Debug("Menü sahibi pencere ön plana alınamadı; menü girdi almayabilir.");
        }
    }
}
