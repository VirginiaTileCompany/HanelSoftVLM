namespace HanelSoftVLM.Logging;

public static class Logger
{
    private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
    private static int _retentionDays = 14;

    public static void Initialize(int retentionDays)
    {
        _retentionDays = retentionDays;
        Directory.CreateDirectory(LogDirectory);
        CleanupOldLogs(); // Clean on startup to avoid unbounded disk usage
    }

    // Delete logs older than retention period to prevent disk space issues
    private static void CleanupOldLogs()
    {
        var cutoff = DateTime.Now.AddDays(-_retentionDays);
        foreach (var file in Directory.GetFiles(LogDirectory, "vlm-*.log"))
        {
            if (File.GetCreationTime(file) < cutoff)
                File.Delete(file);
        }
    }

    public static void Info(string message) => Log("INFO", message);
    public static void Ok(string message) => Log("OK", message);
    public static void Warn(string message) => Log("WARN", message);
    public static void Fail(string message) => Log("FAIL", message);

    public static void Error(string message, Exception? ex = null)
    {
        Log("ERROR", ex != null ? $"{message}: {ex.Message}" : message);
    }

    private static void Log(string tag, string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var line = $"[{timestamp}] [{tag}] {message}";
        Console.WriteLine(line);

        // Daily log rotation - each day gets its own file for easier debugging
        var logFile = Path.Combine(LogDirectory, $"vlm-{DateTime.Now:yyyy-MM-dd}.log");
        File.AppendAllText(logFile, line + Environment.NewLine);
    }
}
