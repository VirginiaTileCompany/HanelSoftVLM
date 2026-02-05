using System.Data;
using System.Text;
using Dancik;
using HanelSoftVLM.Config;
using HanelSoftVLM.Logging;
using HanelSoftVLM.Templates;

namespace HanelSoftVLM.Services;

// Result of processing a batch of commissions
public record ProcessingResult(
    int RowsFound,
    int Created,
    int Failed,
    int Released,
    int ItemsSynced,
    int Skipped  // Orders skipped due to abandonment
);

// Helper to safely extract string values from DataRow without null issues
internal static class DataRowExtensions
{
    public static string GetString(this DataRow row, string column) =>
        row[column]?.ToString()?.Trim() ?? "";
}

// Syncs orders from our database to the HanelSoft warehouse API.
// Processes Issue (outbound) and Receipt (inbound) commissions.
public class CommissionProcessor
{
    private readonly AppConfig _config;
    private readonly HttpClient _client;

    public CommissionProcessor(AppConfig config)
    {
        _config = config;
        _client = new HttpClient();
    }

    public Task<ProcessingResult> ProcessIssueAsync() => ProcessAsync(CommissionType.Issue, "IssueQuery.sql");

    public Task<ProcessingResult> ProcessReceiptAsync() => ProcessAsync(CommissionType.Receipt, "ReceiptQuery.sql");

    private async Task<ProcessingResult> ProcessAsync(CommissionType type, string queryFile)
    {
        var data = LoadQuery(queryFile);
        var groups = data.AsEnumerable().GroupBy(r => r.GetString("ORDERNUMBER"));
        var typeName = type == CommissionType.Issue ? "issueCommission" : "receiptCommission";

        int created = 0, failed = 0, released = 0, itemsSynced = 0, skipped = 0;

        foreach (var group in groups)
        {
            var orderNumber = group.Key;

            // Skip orders that have been abandoned due to persistent failures
            if (FailureTracker.IsAbandoned(orderNumber))
            {
                skipped++;
                continue;
            }

            // Create or update items in HanelSoft before referencing them in a commission
            foreach (var row in group)
            {
                var item = row.GetString("ITEMNUMBER");
                if (!string.IsNullOrEmpty(item))
                {
                    var isNewSync = ItemSyncCache.TryMarkSynced(item);
                    if (await PutItemAsync(item, row.GetString("MANUFACTURER"), row.GetString("PRODUCTLINE")))
                    {
                        if (isNewSync)
                        {
                            itemsSynced++;
                            Logger.Ok($"Synced item: {item}");
                        }
                    }
                    else
                    {
                        // Only log item sync failures once per item
                        Logger.FailOnce($"item-{item}", $"Failed to sync item: {item}");
                    }
                }
            }

            var positions = group.Select(r => CommissionBuilder.BuildPosition(r, r.GetString("ITEMNUMBER"), type));
            var json = CommissionBuilder.BuildCommission(orderNumber, string.Join(",", positions), type);

            // Commissions are created in DESIGNED state, then released to activate them
            var (createSuccess, createBody) = await PutAsync($"{_config.ApiBaseUrl}{_config.Endpoints.CommissionSave}", json, orderNumber);
            if (createSuccess)
            {
                created++;
                FailureTracker.ClearFailure(orderNumber);  // Clear any previous failures
                var (releaseSuccess, releaseBody) = await PutAsync($"{_config.ApiBaseUrl}{_config.Endpoints.CommissionRelease}/{typeName}/{orderNumber}", "", orderNumber);
                if (releaseSuccess)
                {
                    released++;
                    Logger.Ok($"Order {orderNumber} - Created and Released");

                    RecordProcessedOrders(group, type);
                    Logger.Debug($"Order {orderNumber} - {group.Count()} lines recorded in VLMORDERS");
                }
                else
                {
                    // Check if release failed because order is already released
                    if (releaseBody.Contains("NOT_ALLOWED") || releaseBody.Contains("Release not successful"))
                    {
                        Logger.Info($"Order {orderNumber} - Already released, marking as processed");
                        RecordProcessedOrders(group, type);
                        FailureTracker.ClearFailure(orderNumber);
                    }
                    else
                    {
                        Logger.Warn($"Order {orderNumber} - Created but Release Failed");
                    }
                }
            }
            else
            {
                failed++;
                // If order already exists in HanelSoft, record in VLMORDERS to prevent retries
                if (createBody.Contains("already assigned"))
                {
                    RecordProcessedOrders(group, type);
                    Logger.Info($"Order {orderNumber} - Marked as processed (already exists in HanelSoft)");
                    FailureTracker.ClearFailure(orderNumber);
                }
                else
                {
                    // Track failure and check if we should abandon this order
                    var (_, isAbandoned) = FailureTracker.RecordFailure(orderNumber, createBody);
                    if (isAbandoned)
                    {
                        // Record abandoned order in VLMORDERS with failure flag to prevent future retries
                        RecordAbandonedOrder(group, type);
                    }
                }
            }
        }

        return new ProcessingResult(data.Rows.Count, created, failed, released, itemsSynced, skipped);
    }

    private DataTable LoadQuery(string queryFile)
    {
        var queryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Queries", queryFile);
        var query = File.ReadAllText(queryPath);

        // DancikObjects connects to our IBM database and runs the query
        var dancik = new DancikObjects(query, new Dictionary<string, string>());
        Logger.Debug($"Total rows retrieved: {dancik.ReturnData.Rows.Count}");
        return dancik.ReturnData;
    }

    private void InsertVLMOrder(string voType, string orderNumber, string referenceNumber, string lineNumber, string itemNumber)
    {
        var queryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Queries", "VLMOrderInsert.sql");
        var query = File.ReadAllText(queryPath);

        var parameters = new Dictionary<string, string>
        {
            { "VOTYPE", voType },
            { "VOORDER", orderNumber },
            { "VOREF", referenceNumber },
            { "VOLINE", lineNumber },
            { "VOITEM", itemNumber }
        };

        var dancik = new DancikObjects(query, parameters);
    }

    private void RecordProcessedOrders(IEnumerable<DataRow> rows, CommissionType type)
    {
        var voType = type == CommissionType.Issue ? "I" : "R";
        foreach (var row in rows)
        {
            InsertVLMOrder(
                voType,
                row.GetString("ORDERNUMBER"),
                row.GetString("REFERENCENUMBER"),
                row.GetString("LINENUMBER"),
                row.GetString("ITEMNUMBER")
            );
        }
    }

    private void RecordAbandonedOrder(IEnumerable<DataRow> rows, CommissionType type)
    {
        // Use "F" (Failed) type to mark abandoned orders so they won't be retried
        foreach (var row in rows)
        {
            InsertVLMOrder(
                "F",
                row.GetString("ORDERNUMBER"),
                row.GetString("REFERENCENUMBER"),
                row.GetString("LINENUMBER"),
                row.GetString("ITEMNUMBER")
            );
        }
    }

    private async Task<(bool Success, string Body)> PutAsync(string url, string json, string orderNumber)
    {
        try
        {
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _client.PutAsync(url, content);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = $"{response.StatusCode} - {body}";
                var (shouldLog, _) = FailureTracker.RecordFailure(orderNumber, errorMessage);
                if (shouldLog)
                    Logger.Fail($"Order {orderNumber}: {errorMessage}");
                return (false, body);
            }
            return (true, body);
        }
        catch (Exception ex)
        {
            var (shouldLog, _) = FailureTracker.RecordFailure(orderNumber, ex.Message);
            if (shouldLog)
                Logger.Error($"Order {orderNumber}", ex);
            return (false, "");
        }
    }

    private async Task<bool> PutItemAsync(string item, string? manufacturer, string? productLine)
    {
        var additionalItems = GetAdditionalItemNumbers(item);
        var (success, _) = await PutItemRequestAsync($"{_config.ApiBaseUrl}{_config.Endpoints.ItemDefinitionSave}",
            ItemDefinitionBuilder.Build(item, manufacturer, productLine, additionalItems), item);
        return success;
    }

    // Separate PUT method for item syncs that doesn't use order failure tracking
    private async Task<(bool Success, string Body)> PutItemRequestAsync(string url, string json, string itemNumber)
    {
        try
        {
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _client.PutAsync(url, content);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                Logger.Debug($"Item sync {itemNumber} failed: {response.StatusCode} - {body}");
                return (false, body);
            }
            return (true, body);
        }
        catch (Exception ex)
        {
            Logger.Debug($"Item sync {itemNumber} failed: {ex.Message}");
            return (false, "");
        }
    }

    private List<string> GetAdditionalItemNumbers(string parentItem)
    {
        var queryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Queries", "AdditionalItemsQuery.sql");
        var query = File.ReadAllText(queryPath);

        var parameters = new Dictionary<string, string>
        {
            { "PARENTITEM", parentItem }
        };

        var dancik = new DancikObjects(query, parameters);
        return dancik.ReturnData.AsEnumerable()
            .Select(r => r.GetString("CHILDITEM"))
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    public void Dispose() => _client.Dispose();
}
