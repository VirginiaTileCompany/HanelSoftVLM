using System.Data;

namespace HanelSoftVLM.Templates;

// Builds JSON payloads for the HanelSoft warehouse API
public static class CommissionBuilder
{
    public static string BuildIssuePosition(DataRow row, string itemNumber)
    {
        var lineNumber = row["LINENUMBER"]?.ToString() ?? "0";
        var quantity = decimal.TryParse(row["QUANTITY"]?.ToString(), out var q) ? q : 1m;
        var serialNumber = row["SERIALNUMBER"]?.ToString()?.Trim() ?? "";
        var location = row["BINLOCATION"]?.ToString()?.Trim() ?? "";

        var quantAttr = string.IsNullOrEmpty(serialNumber) ? "" :
            $$"""{ "attribute" : "Serial_Number", "value" : "{{serialNumber}}" }""";
        var posAttr = string.IsNullOrEmpty(location) ? "" :
            $$"""{ "attribute" : "BinLocation", "value" : "{{location}}" }""";

        return $$"""
        {
          "@type" : "issuePosition",
          "number" : "{{lineNumber}}",
          "stateType" : "DESIGNED",
          "requiredQuantity" : {{quantity}},
          "itemDefinition" : "{{itemNumber}}",
          "positionAttributes" : [ {{posAttr}} ],
          "quantAttributes" : [ {{quantAttr}} ]
        }
        """;
    }

    public static string BuildReceiptPosition(DataRow row, string itemNumber)
    {
        // Receipt uses different column names than issue
        var lineNumber = row["INVLINE"]?.ToString() ?? "0";
        var quantity = decimal.TryParse(row["QUANTITY"]?.ToString(), out var q) ? q : 1m;
        var serialNumber = row["SERIALNUMBER"]?.ToString()?.Trim() ?? "";
        var location = row["LDCLOCATION"]?.ToString()?.Trim() ?? "";
        var ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff+00:00");

        var quantAttr = string.IsNullOrEmpty(serialNumber) ? "" :
            $$"""{ "attribute" : "Serial_Number", "value" : "{{serialNumber}}" }""";
        var posAttr = string.IsNullOrEmpty(location) ? "" :
            $$"""{ "attribute" : "LDCLocation", "value" : "{{location}}" }""";

        // Receipt positions have more fields than issue positions
        return $$"""
        {
          "@type" : "receiptPosition",
          "number" : "{{lineNumber}}",
          "stateType" : "DESIGNED",
          "creationDate" : "{{ts}}",
          "changeDate" : "{{ts}}",
          "releaseDate" : null,
          "releaseUser" : null,
          "readyDate" : null,
          "requiredQuantity" : {{quantity}},
          "realisedQuantity" : 0,
          "width" : null,
          "depth" : null,
          "height" : null,
          "loadUnitType" : null,
          "itemDefinition" : "{{itemNumber}}",
          "positionAttributes" : [ {{posAttr}} ],
          "bookingAttributes" : [ ],
          "disabledBookingAttributes" : [ ],
          "quantAttributes" : [ {{quantAttr}} ]
        }
        """;
    }

    public static string BuildIssueCommission(string orderNumber, string positionsArray)
    {
        return $$"""
        {
          "@type" : "issueCommission",
          "identifier" : "{{orderNumber}}",
          "stateType" : "DESIGNED",
          "executionType" : "SHORTEST_PATH_OPTIMISATION",
          "positions" : [ {{positionsArray}} ],
          "bookingAttributes" : [ ]
        }
        """;
    }

    public static string BuildReceiptCommission(string orderNumber, string positionsArray)
    {
        var ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff+00:00");
        return $$"""
        {
          "@type" : "receiptCommission",
          "identifier" : "{{orderNumber}}",
          "description" : "",
          "stateType" : "DESIGNED",
          "executionType" : "SHORTEST_PATH_OPTIMISATION",
          "creationDate" : "{{ts}}",
          "changeDate" : "{{ts}}",
          "plannedReleaseDate" : null,
          "releaseDate" : null,
          "readyDate" : null,
          "releaseUser" : null,
          "positions" : [ {{positionsArray}} ],
          "commissionAttributes" : [ ],
          "bookingAttributes" : [ ]
        }
        """;
    }
}
