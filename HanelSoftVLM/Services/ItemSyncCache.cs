namespace HanelSoftVLM.Services;

// Caches synced items to avoid duplicate logging within a session
public static class ItemSyncCache
{
    private static readonly HashSet<string> _syncedItems = new();
    private static readonly object _lock = new();

    // Returns true if this is a new item (not previously synced this session)
    public static bool TryMarkSynced(string itemNumber)
    {
        lock (_lock)
        {
            return _syncedItems.Add(itemNumber);
        }
    }

    // Check if an item has been synced
    public static bool IsSynced(string itemNumber)
    {
        lock (_lock)
        {
            return _syncedItems.Contains(itemNumber);
        }
    }

    // Get the count of synced items
    public static int Count
    {
        get
        {
            lock (_lock)
            {
                return _syncedItems.Count;
            }
        }
    }

    // Clear the cache (useful for testing or long-running sessions)
    public static void Clear()
    {
        lock (_lock)
        {
            _syncedItems.Clear();
        }
    }
}
