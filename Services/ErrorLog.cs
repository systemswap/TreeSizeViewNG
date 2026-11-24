using System.Text;

namespace TreeSizeViewNG.Services;

/// <summary>
/// Schreibt unerwartete Fehler in eine lokale Logdatei, damit ein Absturz auf einem
/// anderen Rechner nachvollziehbar ist:
///   Windows: %LOCALAPPDATA%\TreeSizeViewNG\fehler.log
///   Linux:   ~/.local/share/TreeSizeViewNG/fehler.log
/// Die Datei bleibt auf dem Rechner, es wird nichts verschickt.
/// </summary>
public static class ErrorLog
{
    private const long MaxSizeBytes = 1024 * 1024;
    private static readonly object Sync = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TreeSizeViewNG",
        "fehler.log");

    public static void Write(string context, Exception exception)
    {
        try
        {
            var entry = new StringBuilder()
                .AppendLine($"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {context}")
                .AppendLine($"Version: {typeof(ErrorLog).Assembly.GetName().Version} · " +
                            $"{Environment.OSVersion} · .NET {Environment.Version}")
                .AppendLine(exception.ToString())
                .AppendLine()
                .ToString();

            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

                // Nicht endlos wachsen lassen.
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxSizeBytes)
                    File.Move(FilePath, FilePath + ".alt", overwrite: true);

                File.AppendAllText(FilePath, entry, Encoding.UTF8);
            }
        }
        catch
        {
            // Protokollieren darf selbst nie zum Absturz führen.
        }
    }
}
