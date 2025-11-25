// HanelSoftVLM - Syncs orders from our database to HanelSoft warehouse management system

using HanelSoftVLM.Config;
using HanelSoftVLM.Logging;
using HanelSoftVLM.Services;

var config = AppConfig.Load();
var processor = new CommissionProcessor(config);

Logger.Info($"HanelSoftVLM service started - processing every {config.ProcessingIntervalSeconds} seconds");
Logger.Info("Press Ctrl+C to stop");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;  // Prevent immediate termination
    cts.Cancel();     // Signal our loop to stop
    Logger.Info("Shutdown requested...");
};

while (!cts.Token.IsCancellationRequested)
{
    try
    {
        Logger.Info("--- Processing Issue commissions ---");
        await processor.ProcessIssueAsync();

        Logger.Info("--- Processing Receipt commissions ---");
        await processor.ProcessReceiptAsync();
    }
    catch (Exception ex)
    {
        // Log but don't crash - we'll retry next cycle
        Logger.Error("Processing cycle failed", ex);
    }

    try
    {
        await Task.Delay(config.ProcessingIntervalSeconds * 1000, cts.Token);
    }
    catch (TaskCanceledException)
    {
        // Ctrl+C was pressed during delay
        break;
    }
}

processor.Dispose();
Logger.Info("Service stopped");
