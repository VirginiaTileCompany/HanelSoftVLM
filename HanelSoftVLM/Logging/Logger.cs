namespace HanelSoftVLM.Logging;

public static class Logger
{
    private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
    private static int _retentionDays = 14;
    private static bool _debugEnabled = false;
    private static readonly HashSet<string> _loggedOnceKeys = new();

    public static void Initialize(int retentionDays, bool debugEnabled = false)
    {
        _retentionDays = retentionDays;
        _debugEnabled = debugEnabled;
        Directory.CreateDirectory(LogDirectory);
        CleanupOldLogs(); // Clean on startup to avoid unbounded disk usage
    }

    // Cumulative stats tracking for periodic summaries
    public static class Stats
    {
        private static int _created;
        private static int _failed;
        private static int _released;
        private static int _itemsSynced;
        private static int _activeCycles;
        private static int _idleCycles;
        private static readonly DateTime _startTime = DateTime.Now;

        public static void IncrementCreated(int count = 1) => Interlocked.Add(ref _created, count);
        public static void IncrementFailed(int count = 1) => Interlocked.Add(ref _failed, count);
        public static void IncrementReleased(int count = 1) => Interlocked.Add(ref _released, count);
        public static void IncrementItemsSynced(int count = 1) => Interlocked.Add(ref _itemsSynced, count);
        public static void IncrementActiveCycles() => Interlocked.Increment(ref _activeCycles);
        public static void IncrementIdleCycles() => Interlocked.Increment(ref _idleCycles);

        public static (int Created, int Failed, int Released, int ItemsSynced, int ActiveCycles, int IdleCycles, TimeSpan Uptime) GetStats()
        {
            return (_created, _failed, _released, _itemsSynced, _activeCycles, _idleCycles, DateTime.Now - _startTime);
        }

        public static string GetSummary()
        {
            var (created, failed, released, itemsSynced, activeCycles, idleCycles, uptime) = GetStats();
            return $"Stats: {created} created, {failed} failed, {itemsSynced} items synced | {activeCycles} active, {idleCycles} idle cycles | uptime {uptime:hh\\:mm\\:ss}";
        }
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

    // Only logs if debug mode is enabled
    public static void Debug(string message)
    {
        if (_debugEnabled)
            Log("DEBUG", message);
    }

    // Only logs the first occurrence of a message per key (per session)
    public static bool FailOnce(string key, string message)
    {
        lock (_loggedOnceKeys)
        {
            if (_loggedOnceKeys.Contains(key))
                return false;
            _loggedOnceKeys.Add(key);
        }
        Fail(message);
        return true;
    }

    // Clear the once-logged keys (useful for testing or long-running sessions)
    public static void ResetOnceKeys()
    {
        lock (_loggedOnceKeys)
        {
            _loggedOnceKeys.Clear();
        }
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
