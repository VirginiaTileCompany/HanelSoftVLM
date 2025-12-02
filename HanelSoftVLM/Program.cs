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

// Run both tasks concurrently with their own intervals
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

await Task.WhenAll(receiptTask, issueTask);

processor.Dispose();
Logger.Info("Service stopped");

// Runs the action periodically, skipping if the previous run is still in progress
static async Task RunPeriodicAsync(string name, Func<Task> action, int intervalSeconds, CancellationToken ct)
{
    var isRunning = false;

    while (!ct.IsCancellationRequested)
    {
        if (!isRunning)
        {
            isRunning = true;
            try
            {
                Logger.Info($"--- Processing {name} commissions ---");
                await action();
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
