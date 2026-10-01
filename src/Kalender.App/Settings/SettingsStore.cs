using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kalender.Core;

namespace Kalender.Settings;

/// <summary>
/// Ayarları diskten okur, diske yazar ve değiştiğinde haber verir.
/// Yazma işlemi önce geçici dosyaya yapılır; böylece elektrik kesintisinde
/// yarım kalmış bir ayar dosyası kalmaz.
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
            try
            {
                if (File.Exists(Paths.SettingsFile))
                {
                    string json = File.ReadAllText(Paths.SettingsFile, Encoding.UTF8);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (loaded is not null)
                    {
                        Current = loaded;
                        duzMetinVardi = loaded.Google.DuzMetinOkundu;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Ayar dosyası okunamadı, varsayılanlara dönülüyor.", ex);
                Current = new AppSettings();
            }
        }

        Log.Level = (LogLevel)Math.Clamp(Current.LogLevel, 0, 4);

        // 1.10'a kadar Google istemci gizli anahtarı düz metin yazılıyordu;
        // dosya bir kez şifreli hâliyle yeniden yazılır, düz metin kalmaz.
        if (duzMetinVardi)
        {
            Current.Google.DuzMetinOkundu = false;
            Save();
            Log.Info("Google istemci gizli anahtarı ayar dosyasında şifreli hâle getirildi.");
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
                string temp = Paths.SettingsFile + ".tmp";
                File.WriteAllText(temp, json, new UTF8Encoding(false));

                if (File.Exists(Paths.SettingsFile))
                {
                    File.Replace(temp, Paths.SettingsFile, null);
                }
                else
                {
                    File.Move(temp, Paths.SettingsFile);
                }
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
}
