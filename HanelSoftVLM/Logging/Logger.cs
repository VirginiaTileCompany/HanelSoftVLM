namespace HanelSoftVLM.Logging;

public static class Logger
{
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
        Console.WriteLine($"[{timestamp}] [{tag}] {message}");
    }
}
