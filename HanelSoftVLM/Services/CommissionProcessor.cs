using System.Data;
using System.Text;
using Dancik;
using HanelSoftVLM.Config;
using HanelSoftVLM.Logging;
using HanelSoftVLM.Templates;

namespace HanelSoftVLM.Services;

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

    public Task ProcessIssueAsync() => ProcessAsync(CommissionType.Issue, "IssueQuery.sql");

    public Task ProcessReceiptAsync() => ProcessAsync(CommissionType.Receipt, "ReceiptQuery.sql");

    private async Task ProcessAsync(CommissionType type, string queryFile)
    {
        var data = LoadQuery(queryFile);
        var groups = data.AsEnumerable().GroupBy(r => r.GetString("ORDERNUMBER"));
        var typeName = type == CommissionType.Issue ? "issueCommission" : "receiptCommission";

        int created = 0, failed = 0, released = 0;

        foreach (var group in groups)
        {
            var orderNumber = group.Key;

            // Items must exist in HanelSoft before we can reference them in a commission
            foreach (var row in group)
            {
                var item = row.GetString("ITEMNUMBER");
                if (!string.IsNullOrEmpty(item) && !await GetItemAsync(item))
                {
                    if (await PutItemAsync(item, row.GetString("MANUFACTURER"), row.GetString("PRODUCTLINE")))
                        Logger.Ok($"Created item: {item}");
                    else
                        Logger.Warn($"Failed to create item: {item}");
                }
            }

            var positions = group.Select(r => CommissionBuilder.BuildPosition(r, r.GetString("ITEMNUMBER"), type));
            var json = CommissionBuilder.BuildCommission(orderNumber, string.Join(",", positions), type);

            // Commissions are created in DESIGNED state, then released to activate them
            var (createSuccess, createBody) = await PutAsync($"{_config.ApiBaseUrl}{_config.Endpoints.CommissionSave}", json, $"Order {orderNumber} - Creation Failed");
            if (createSuccess)
            {
                created++;
                var (releaseSuccess, _) = await PutAsync($"{_config.ApiBaseUrl}{_config.Endpoints.CommissionRelease}/{typeName}/{orderNumber}", "", $"Release {orderNumber} failed");
                if (releaseSuccess)
                {
                    released++;
                    Logger.Ok($"Order {orderNumber} - Created and Released");

                    RecordProcessedOrders(group, type);
                    Logger.Ok($"Order {orderNumber} - {group.Count()} lines recorded in VLMORDERS");
                }
                else
                    Logger.Warn($"Order {orderNumber} - Created but Release Failed");
            }
            else
            {
                failed++;
                // If order already exists in HanelSoft, record in VLMORDERS to prevent retries
                if (createBody.Contains("already assigned"))
                {
                    RecordProcessedOrders(group, type);
                    Logger.Info($"Order {orderNumber} - Marked as processed (already exists in HanelSoft)");
                }
            }
        }

        Logger.Info($"Completed: {created} created, {failed} failed, {released} released");
    }

    private DataTable LoadQuery(string queryFile)
    {
        var queryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Queries", queryFile);
        var query = File.ReadAllText(queryPath);

        // DancikObjects connects to our IBM database and runs the query
        var dancik = new DancikObjects(query, new Dictionary<string, string>());
        Logger.Info($"Total rows retrieved: {dancik.ReturnData.Rows.Count}");
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

    private async Task<(bool Success, string Body)> PutAsync(string url, string json, string context)
    {
        try
        {
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _client.PutAsync(url, content);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                Logger.Fail($"{context}: {response.StatusCode} - {body}");
                return (false, body);
            }
            return (true, body);
        }
        catch (Exception ex)
        {
            Logger.Error(context, ex);
            return (false, "");
        }
    }

    private async Task<bool> GetAsync(string url)
    {
        try { return (await _client.GetAsync(url)).IsSuccessStatusCode; }
        catch (Exception ex)
        {
            Logger.Warn($"GET request failed for {url}: {ex.Message}");
            return false;
        }
    }

    private Task<bool> GetItemAsync(string item) =>
        GetAsync($"{_config.ApiBaseUrl}{_config.Endpoints.ItemDefinitionFind}/{item}");

    private async Task<bool> PutItemAsync(string item, string? manufacturer, string? productLine)
    {
        var (success, _) = await PutAsync($"{_config.ApiBaseUrl}{_config.Endpoints.ItemDefinitionSave}",
            ItemDefinitionBuilder.Build(item, manufacturer, productLine), $"Create item {item} failed");
        return success;
    }

    public void Dispose() => _client.Dispose();
}
