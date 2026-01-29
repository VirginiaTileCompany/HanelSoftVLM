// HanelSoftVLM - Syncs orders from our database to HanelSoft warehouse management system

using HanelSoftVLM.Config;
using HanelSoftVLM.Logging;
using HanelSoftVLM.Services;

var config = AppConfig.Load();
Logger.Initialize(config.LogRetentionDays);
var processor = new CommissionProcessor(config);

Logger.Info($"HanelSoftVLM service started");
Logger.Info($"  Receipt (Putaway) interval: {config.ReceiptIntervalSeconds} seconds");
Logger.Info($"  Issue (Pick) interval: {config.IssueIntervalSeconds} seconds");
Logger.Info("Press Ctrl+C to stop");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;  // Prevent immediate termination
    cts.Cancel();     // Signal our loops to stop
    Logger.Info("Shutdown requested...");
};

// Run processing tasks and periodic summary concurrently
var receiptTask = RunPeriodicAsync(
    "Receipt",
    () => processor.ProcessReceiptAsync(),
    config.ReceiptIntervalSeconds,
    cts.Token);

var issueTask = RunPeriodicAsync(
    "Issue",
    () => processor.ProcessIssueAsync(),
    config.IssueIntervalSeconds,
    cts.Token);

var summaryTask = RunSummaryAsync(cts.Token);

await Task.WhenAll(receiptTask, issueTask, summaryTask);

processor.Dispose();
Logger.Info(Logger.Stats.GetSummary());
Logger.Info("Service stopped");

// Runs the action periodically, logging only when there's actual work
static async Task RunPeriodicAsync(string name, Func<Task<ProcessingResult>> action, int intervalSeconds, CancellationToken ct)
{
    var isRunning = false;
    var consecutiveIdleCycles = 0;
    const int idleLogThreshold = 30;  // Only log idle status every 30 cycles

    while (!ct.IsCancellationRequested)
    {
        if (!isRunning)
        {
            isRunning = true;
            try
            {
                var result = await action();

                // Update cumulative stats
                Logger.Stats.IncrementCreated(result.Created);
                Logger.Stats.IncrementFailed(result.Failed);
                Logger.Stats.IncrementReleased(result.Released);
                Logger.Stats.IncrementItemsSynced(result.ItemsSynced);

                if (result.RowsFound > 0)
                {
                    // Active cycle - reset idle counter and log summary
                    consecutiveIdleCycles = 0;
                    Logger.Stats.IncrementActiveCycles();
                    Logger.Info($"[{name}] Processed: {result.Created} created, {result.Failed} failed, {result.Released} released");
                }
                else
                {
                    // Idle cycle - only log periodically
                    consecutiveIdleCycles++;
                    Logger.Stats.IncrementIdleCycles();
                    if (consecutiveIdleCycles % idleLogThreshold == 0)
                    {
                        Logger.Info($"[{name}] Idle for {consecutiveIdleCycles} cycles, waiting for work...");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"{name} processing failed", ex);
            }
            finally
            {
                isRunning = false;
            }
        }

        try
        {
            await Task.Delay(intervalSeconds * 1000, ct);
        }
        catch (TaskCanceledException)
        {
            break;
        }
    }
}

// Logs cumulative stats every 15 minutes
static async Task RunSummaryAsync(CancellationToken ct)
{
    const int summaryIntervalMinutes = 15;

    while (!ct.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(summaryIntervalMinutes), ct);
            Logger.Info(Logger.Stats.GetSummary());

            // Report abandoned orders count if any
            var abandonedCount = FailureTracker.GetAbandonedCount();
            if (abandonedCount > 0)
            {
                Logger.Warn($"Abandoned orders requiring manual intervention: {abandonedCount}");
            }
        }
        catch (TaskCanceledException)
        {
            break;
        }
    }
}
