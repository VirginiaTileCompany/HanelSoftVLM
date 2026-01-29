namespace HanelSoftVLM.Logging;

// Tracks persistent failures to reduce log spam and escalate chronic issues
public static class FailureTracker
{
    private static readonly Dictionary<string, FailureInfo> _failures = new();
    private static readonly object _lock = new();

    private const int LogEveryNthFailure = 10;
    private const int PersistentFailureMinutes = 5;
    private const int MaxFailuresBeforeAbandonment = 50;

    private class FailureInfo
    {
        public int Count { get; set; }
        public DateTime FirstFailure { get; set; }
        public DateTime LastFailure { get; set; }
        public string LastMessage { get; set; } = "";
        public bool EscalatedToPersistent { get; set; }
        public bool Abandoned { get; set; }
    }

    // Returns: (shouldLog, isAbandoned)
    // shouldLog = true if this failure should be logged
    // isAbandoned = true if max failures reached and order should be marked for manual intervention
    public static (bool ShouldLog, bool IsAbandoned) RecordFailure(string orderNumber, string message)
    {
        lock (_lock)
        {
            if (!_failures.TryGetValue(orderNumber, out var info))
            {
                // First failure for this order - always log
                _failures[orderNumber] = new FailureInfo
                {
                    Count = 1,
                    FirstFailure = DateTime.Now,
                    LastFailure = DateTime.Now,
                    LastMessage = message
                };
                return (true, false);
            }

            // Already abandoned - don't log or retry
            if (info.Abandoned)
                return (false, false);

            info.Count++;
            info.LastFailure = DateTime.Now;
            info.LastMessage = message;

            // Check if we've hit the abandonment threshold
            if (info.Count >= MaxFailuresBeforeAbandonment)
            {
                info.Abandoned = true;
                Logger.Warn($"Order {orderNumber} abandoned after {info.Count} failures - manual intervention required");
                return (false, true);
            }

            // Check for persistent failure escalation (5+ minutes)
            var duration = info.LastFailure - info.FirstFailure;
            if (!info.EscalatedToPersistent && duration.TotalMinutes >= PersistentFailureMinutes)
            {
                info.EscalatedToPersistent = true;
                Logger.Warn($"PERSISTENT FAILURE: Order {orderNumber} has failed {info.Count} times over {duration:hh\\:mm\\:ss}");
                return (false, false);
            }

            // Log every Nth failure to show it's still failing
            if (info.Count % LogEveryNthFailure == 0)
            {
                return (true, false);
            }

            return (false, false);
        }
    }

    // Call when an order is successfully processed to clear its failure history
    public static void ClearFailure(string orderNumber)
    {
        lock (_lock)
        {
            _failures.Remove(orderNumber);
        }
    }

    // Check if an order has been abandoned
    public static bool IsAbandoned(string orderNumber)
    {
        lock (_lock)
        {
            return _failures.TryGetValue(orderNumber, out var info) && info.Abandoned;
        }
    }

    // Get count of abandoned orders
    public static int GetAbandonedCount()
    {
        lock (_lock)
        {
            return _failures.Values.Count(f => f.Abandoned);
        }
    }

    // Get all abandoned order numbers (for recording to database)
    public static List<string> GetAbandonedOrders()
    {
        lock (_lock)
        {
            return _failures.Where(f => f.Value.Abandoned).Select(f => f.Key).ToList();
        }
    }
}
