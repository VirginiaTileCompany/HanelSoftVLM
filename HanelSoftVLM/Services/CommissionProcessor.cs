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

    public async Task ProcessIssueAsync()
    {
        var data = LoadQuery("IssueQuery.sql");
        var groups = data.AsEnumerable().GroupBy(r => r.GetString("ORDERNUMBER"));

        int created = 0, skipped = 0, failed = 0, released = 0;

        foreach (var group in groups)
        {
            var orderNumber = group.Key;

            // Skip orders that already exist in HanelSoft to avoid duplicates
            if (await GetAsync($"{_config.ApiBaseUrl}{_config.Endpoints.CommissionExists}/issueCommission/{orderNumber}"))
            {
                Logger.Info($"Order {orderNumber} - Already exists, skipping");
                skipped++;
                continue;
            }

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

            var positions = group.Select(r => CommissionBuilder.BuildIssuePosition(r, r.GetString("ITEMNUMBER")));
            var json = CommissionBuilder.BuildIssueCommission(orderNumber, string.Join(",", positions));

            // Commissions are created in DESIGNED state, then released to activate them
            if (await PutAsync($"{_config.ApiBaseUrl}{_config.Endpoints.CommissionSave}", json, $"Order {orderNumber} - Creation Failed"))
            {
                created++;
                if (await PutAsync($"{_config.ApiBaseUrl}{_config.Endpoints.CommissionRelease}/issueCommission/{orderNumber}", "", $"Release {orderNumber} failed"))
                {
                    released++;
                    Logger.Ok($"Order {orderNumber} - Created and Released");
                }
                else
                    Logger.Warn($"Order {orderNumber} - Created but Release Failed");
            }
            else
                failed++;
        }

        Logger.Info($"Completed: {created} created, {skipped} skipped, {failed} failed, {released} released");
    }

    // Receipt processing follows same workflow as Issue: check existence -> create items -> save -> release
    public async Task ProcessReceiptAsync()
    {
        var data = LoadQuery("ReceiptQuery.sql");
        var groups = data.AsEnumerable().GroupBy(r => r.GetString("ORDERNUMBER"));

        int created = 0, skipped = 0, failed = 0, released = 0;

        foreach (var group in groups)
        {
            var orderNumber = group.Key;

            if (await GetAsync($"{_config.ApiBaseUrl}{_config.Endpoints.CommissionExists}/receiptCommission/{orderNumber}"))
            {
                Logger.Info($"Order {orderNumber} - Already exists, skipping");
                skipped++;
                continue;
            }

            foreach (var row in group)
            {
                var item = row.GetString("ITEMNUMBER");
                // Receipt items don't have manufacturer/productLine in our data source
                if (!string.IsNullOrEmpty(item) && !await GetItemAsync(item))
                {
                    if (await PutItemAsync(item, null, null))
                        Logger.Ok($"Created item: {item}");
                    else
                        Logger.Warn($"Failed to create item: {item}");
                }
            }

            var positions = group.Select(r => CommissionBuilder.BuildReceiptPosition(r, r.GetString("ITEMNUMBER")));
            var json = CommissionBuilder.BuildReceiptCommission(orderNumber, string.Join(",", positions));

            if (await PutAsync($"{_config.ApiBaseUrl}{_config.Endpoints.CommissionSave}", json, $"Order {orderNumber} - Creation Failed"))
            {
                created++;
                if (await PutAsync($"{_config.ApiBaseUrl}{_config.Endpoints.CommissionRelease}/receiptCommission/{orderNumber}", "", $"Release {orderNumber} failed"))
                {
                    released++;
                    Logger.Ok($"Order {orderNumber} - Created and Released");
                }
                else
                    Logger.Warn($"Order {orderNumber} - Created but Release Failed");
            }
            else
                failed++;
        }

        Logger.Info($"Completed: {created} created, {skipped} skipped, {failed} failed, {released} released");
    }

    private DataTable LoadQuery(string queryFile)
    {
        var queryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Queries", queryFile);
        var query = File.ReadAllText(queryPath);

        // DancikObjects connects to our IBM database and runs the query
        var dancik = new DancikObjects(query, new Dictionary<string, string>());
        Logger.Info(string.Join(',', dancik.Messages));
        Logger.Info($"Total rows retrieved: {dancik.ReturnData.Rows.Count}");
        return dancik.ReturnData;
    }

    private async Task<bool> PutAsync(string url, string json, string context)
    {
        try
        {
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _client.PutAsync(url, content);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Fail($"{context}: {response.StatusCode}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(context, ex);
            return false;
        }
    }

    private async Task<bool> GetAsync(string url)
    {
        try { return (await _client.GetAsync(url)).IsSuccessStatusCode; }
        catch { return false; }
    }

    private Task<bool> GetItemAsync(string item) =>
        GetAsync($"{_config.ApiBaseUrl}{_config.Endpoints.ItemDefinitionFind}/{item}");

    private Task<bool> PutItemAsync(string item, string? manufacturer, string? productLine) =>
        PutAsync($"{_config.ApiBaseUrl}{_config.Endpoints.ItemDefinitionSave}",
            ItemDefinitionBuilder.Build(item, manufacturer, productLine), $"Create item {item} failed");

    public void Dispose() => _client.Dispose();
}
