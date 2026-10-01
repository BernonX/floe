using System.IO;

namespace Floe.Desktop.Diagnostics;

/// <summary>Best-effort, bounded local diagnostics. Logging must never replace the original failure.</summary>
internal static class DiagnosticLog
{
    private const long MaximumBytes = 256 * 1024;
    private static readonly object Gate = new();
    public static void Write(string category, Exception error)
    {
        try
        {
            var directory = Environment.GetEnvironmentVariable("FLOE_DATA_DIRECTORY");
            if (string.IsNullOrWhiteSpace(directory)) directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Floe");
            var logs = Path.Combine(directory, "logs");
            lock (Gate)
            {
                Directory.CreateDirectory(logs);
                var path = Path.Combine(logs, "floe.log");
                // No session snapshots, SQL data or task bodies are added to diagnostics.
                var description = error.ToString();
                if (description.Length > 16_384) description = description[..16_384];
                var entry = $"{DateTimeOffset.UtcNow:O} [{category}] {description}{Environment.NewLine}";
                if (File.Exists(path) && new FileInfo(path).Length + System.Text.Encoding.UTF8.GetByteCount(entry) > MaximumBytes)
                    File.Move(path, Path.Combine(logs, "floe.previous.log"), overwrite: true);
                File.AppendAllText(path, entry);
            }
        }
        catch (Exception loggingError) when (loggingError is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        { /* An unwritable diagnostic directory must not prevent the error dialog or shutdown. */ }
    }
}
