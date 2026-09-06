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

            // Menünün dışına tıklanınca kapanabilmesi için sahibi pencerenin ön
            // planda olması gerekir; kapandıktan sonra bir mesajla serbest bırakılır.
            NativeMethods.SetForegroundWindow(owner);

            uint secim = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_NONOTIFY,
                x, y, owner, IntPtr.Zero);

            NativeMethods.PostMessage(owner, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);

            return secim;
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }
}
