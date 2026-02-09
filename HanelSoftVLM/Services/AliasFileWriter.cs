using HanelSoftVLM.Logging;

namespace HanelSoftVLM.Services;

// Writes parent/child alias pairs to a CSV file that HanelSoft consumes on a ~1 minute interval.
// Thread-safe — both Issue and Receipt loops may call WriteAliases concurrently.
public static class AliasFileWriter
{
    private static string _filePath = "";
    private static readonly object _lock = new();

    public static void Initialize(string filePath)
    {
        _filePath = filePath ?? "";
    }

    public static void WriteAliases(List<(string Parent, List<string> Children)> aliasBatch)
    {
        if (string.IsNullOrEmpty(_filePath) || aliasBatch.Count == 0)
            return;

        var lines = new List<string>();
        foreach (var (parent, children) in aliasBatch)
        {
            foreach (var child in children)
            {
                lines.Add($"{parent};{child}");
            }
        }

        if (lines.Count == 0)
            return;

        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                File.AppendAllLines(_filePath, lines);
                Logger.Debug($"Wrote {lines.Count} alias lines to {_filePath}");
            }
            catch (IOException ex)
            {
                Logger.Warn($"Alias file write failed (file may be locked by HanelSoft): {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.Warn($"Alias file write failed (access denied): {ex.Message}");
            }
        }
    }
}
