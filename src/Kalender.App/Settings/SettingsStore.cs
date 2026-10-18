using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kalender.Core;

namespace Kalender.Settings;

/// <summary>
/// Ayarları diskten okur, diske yazar ve değiştiğinde haber verir.
/// Yazma işlemi önce geçici dosyaya yapılır ve diske boşaltılır, sonra asıl
/// dosyanın yerine konur; asıl dosyanın önceki hâli yedek olarak kalır.
/// Böylece elektrik kesintisinde yarım kalmış bir ayar dosyası kalmaz; yine
/// de okunamayan bir dosya bulunursa kenara alınır ve yerine yedek okunur.
/// </summary>
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();

    public AppSettings Current { get; private set; } = new();

    /// <summary>Ayarlar diske yazıldığında tetiklenir.</summary>
    public event Action<AppSettings>? Changed;

    public void Load()
    {
        bool duzMetinVardi = false;

        lock (_gate)
        {
            Current = DosyadanOku(Paths.SettingsFile) ?? new AppSettings();
            duzMetinVardi = Current.Google.DuzMetinOkundu;
        }

        Log.Level = (LogLevel)Math.Clamp(Current.LogLevel, 0, 4);

        // 1.10'a kadar Google istemci gizli anahtarı, 1.16'ya kadar istemci
        // kimliği, hesabın e-posta adresi ve seçili takvimler düz metin
        // yazılıyordu; dosya bir kez şifreli hâliyle yeniden yazılır, düz metin kalmaz.
        if (duzMetinVardi)
        {
            Current.Google.DuzMetinOkundu = false;
            Save();

            // Dosyanın düz metinli önceki hâli yedek olarak da kalmasın. Kayıt
            // yazılamadıysa (asıl dosya yoksa) yedek tek kopyadır, silinmez.
            if (File.Exists(Paths.SettingsFile))
            {
                YedegiSil(Paths.SettingsFile);
            }

            Log.Info("Google bilgileri ayar dosyasında şifreli hâle getirildi.");
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Paths.RootFolder);
                string json = JsonSerializer.Serialize(Current, JsonOptions);
                DosyayaYaz(Paths.SettingsFile, json);
            }
            catch (Exception ex)
            {
                Log.Error("Ayarlar kaydedilemedi.", ex);
            }
        }

        Changed?.Invoke(Current);
    }

    /// <summary>Ayarları değiştirip tek seferde kaydeder.</summary>
    public void Update(Action<AppSettings> degistir)
    {
        lock (_gate)
        {
            degistir(Current);
        }

        Save();
    }

    /// <summary>Ayar dosyasının bir önceki hâli; asıl dosya okunamazsa bu okunur.</summary>
    internal static string YedekAdi(string dosya) => dosya + ".yedek";

    /// <summary>
    /// Ayarları okur. Asıl dosya okunamazsa (yarım kalmış yazım, elle
    /// düzenlemede kalan bir yanlış) kenara alınır, üstüne yazılmaz; yerine
    /// yedek okunur. Okunacak bir şey yoksa null döner, varsayılanlar kullanılır.
    /// </summary>
    internal static AppSettings? DosyadanOku(string dosya)
    {
        string yedek = YedekAdi(dosya);

        if (File.Exists(dosya))
        {
            if (Oku(dosya) is AppSettings okunan)
            {
                return okunan;
            }

            KenaraAl(dosya);
        }
        else if (!File.Exists(yedek))
        {
            // İlk açılış: henüz ayar yok.
            return null;
        }

        // Asıl dosya okunamadı ya da yerine konurken iş yarıda kaldı (yalnız yedek var).
        AppSettings? yedekten = File.Exists(yedek) ? Oku(yedek) : null;
        if (yedekten is not null)
        {
            Log.Warn("Ayarlar yedek dosyadan okundu.");
        }
        else
        {
            Log.Error("Ayar dosyası da yedeği de okunamadı, varsayılanlara dönülüyor.");
        }

        return yedekten;
    }

    private static AppSettings? Oku(string dosya)
    {
        try
        {
            string json = File.ReadAllText(dosya, Encoding.UTF8);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? throw new JsonException("Dosyada ayar yok.");
        }
        catch (Exception ex)
        {
            Log.Error("Ayar dosyası okunamadı: " + Path.GetFileName(dosya), ex);
            return null;
        }
    }

    /// <summary>
    /// Okunamayan ayar dosyasını "ayarlar.bozuk-&lt;tarih&gt;.json" adıyla kenara
    /// alır: yerine yazılacak ayarlar onu ezmez, gerekirse açılıp bakılabilir.
    /// Aynı adda bir dosya varsa adın sonuna sayı eklenir.
    /// </summary>
    private static void KenaraAl(string dosya)
    {
        try
        {
            string klasor = Path.GetDirectoryName(dosya)!;
            string ad = $"{Path.GetFileNameWithoutExtension(dosya)}.bozuk-{DateTime.Now:yyyy-MM-dd-HHmmss}";
            string hedef = Path.Combine(klasor, ad + ".json");
            for (int i = 2; File.Exists(hedef); i++)
            {
                hedef = Path.Combine(klasor, $"{ad}-{i}.json");
            }

            File.Move(dosya, hedef);
            Log.Warn("Okunamayan ayar dosyası kenara alındı: " + Path.GetFileName(hedef));
        }
        catch (Exception ex)
        {
            Log.Error("Okunamayan ayar dosyası kenara alınamadı.", ex);
        }
    }

    /// <summary>
    /// Önce geçici dosyaya yazar ve diske boşaltır, sonra asıl dosyanın yerine
    /// koyar; asıl dosyanın önceki hâli yedek olur. Boşaltma olmasaydı ad
    /// değişikliği diske ulaşıp içerik ulaşmadan elektrik gidebilir, dosya boş
    /// kalabilirdi.
    /// </summary>
    internal static void DosyayaYaz(string dosya, string json)
    {
        string temp = dosya + ".tmp";
        using (var akis = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            akis.Write(new UTF8Encoding(false).GetBytes(json));
            akis.Flush(flushToDisk: true);
        }

        if (File.Exists(dosya))
        {
            File.Replace(temp, dosya, YedekAdi(dosya));
        }
        else
        {
            File.Move(temp, dosya);
        }
    }

    private static void YedegiSil(string dosya)
    {
        try
        {
            File.Delete(YedekAdi(dosya));
        }
        catch (Exception ex)
        {
            Log.Warn("Ayar dosyasının yedeği silinemedi.", ex);
        }
    }
}
