using System.Diagnostics;
using System.IO;
using System.Text;

namespace Kalender.Core;

internal enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    None = 4,
}

/// <summary>
/// Küçük ve dönen bir dosya günlüğü. Varsayılan olarak yalnızca hatalar yazılır;
/// sorun ararken seviye Debug'a çekilebilir. Günlükler
/// %LOCALAPPDATA%\Kalender\logs altında tutulur, en yeni 5 dosya saklanır.
/// </summary>
internal static class Log
{
    private const long MaxFileBytes = 512 * 1024;
    private const int KeepFiles = 5;

    private static readonly object Gate = new();
    private static string? _currentFile;

    public static LogLevel Level { get; set; } = LogLevel.Error;

    public static void Debug(string message) => Write(LogLevel.Debug, message, null);

    public static void Info(string message) => Write(LogLevel.Info, message, null);

    public static void Warn(string message, Exception? ex = null) => Write(LogLevel.Warning, message, ex);

    public static void Error(string message, Exception? ex = null) => Write(LogLevel.Error, message, ex);

    private static void Write(LogLevel level, string message, Exception? ex)
    {
        if (level < Level)
        {
            return;
        }

        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(Etiket(level)).Append("] ")
            .Append(message);

        if (ex is not null)
        {
            line.Append(Environment.NewLine).Append("    ").Append(ex.GetType().Name)
                .Append(": ").Append(ex.Message);
            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                line.Append(Environment.NewLine).Append(ex.StackTrace);
            }
        }

        string text = line.ToString();
        Trace.WriteLine(text);

        try
        {
            lock (Gate)
            {
                string file = ResolveFile();
                File.AppendAllText(file, text + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Günlük yazılamıyorsa uygulamayı bunun için durdurmayız.
        }
    }

    private static string Etiket(LogLevel level) => level switch
    {
        LogLevel.Debug => "AYRINTI",
        LogLevel.Info => "BILGI",
        LogLevel.Warning => "UYARI",
        LogLevel.Error => "HATA",
        _ => "?",
    };

    private static string ResolveFile()
    {
        string wanted = Path.Combine(Paths.LogFolder, $"kalender-{DateTime.Now:yyyyMMdd}.log");

        if (_currentFile != wanted)
        {
            Directory.CreateDirectory(Paths.LogFolder);
            _currentFile = wanted;
            Temizle();
        }

        var info = new FileInfo(wanted);
        if (info.Exists && info.Length > MaxFileBytes)
        {
            string rotated = Path.Combine(
                Paths.LogFolder,
                $"kalender-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            try
            {
                File.Move(wanted, rotated);
            }
            catch
            {
                // Taşıyamazsak aynı dosyaya yazmaya devam ederiz.
            }

            Temizle();
        }

        return wanted;
    }

    private static void Temizle()
    {
        try
        {
            var files = new DirectoryInfo(Paths.LogFolder)
                .GetFiles("kalender-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(KeepFiles);

            foreach (var f in files)
            {
                f.Delete();
            }
        }
        catch
        {
            // Temizlik başarısız olursa önemsizdir.
        }
    }
}
