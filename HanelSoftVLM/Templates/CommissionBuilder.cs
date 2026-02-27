using System.Data;

namespace HanelSoftVLM.Templates;

public enum CommissionType { Issue, Receipt }

// Builds JSON payloads for the HanelSoft warehouse API
public static class CommissionBuilder
{
    public static string BuildPosition(DataRow row, string itemNumber, CommissionType type)
    {
        var lineNumber = row["LINENUMBER"]?.ToString() ?? "0";
        var quantity = decimal.TryParse(row["QUANTITY"]?.ToString(), out var q) ? q : 1m;
        var location = row["BINLOCATION"]?.ToString()?.Trim() ?? "";

        // Issue = outbound (picking from warehouse), Receipt = inbound (receiving stock)
        if (type == CommissionType.Issue)
        {
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
              "quantAttributes" : [ ]
            }
            """;
        }
        else
        {
            // Receipt API requires additional date/state fields that Issue doesn't need
            var ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff+00:00");
            var posAttr = string.IsNullOrEmpty(location) ? "" :
                $$"""{ "attribute" : "LDCLocation", "value" : "{{location}}" }""";

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
              "quantAttributes" : [ ]
            }
            """;
        }
    }

    public static string BuildCommission(string orderNumber, string positionsArray, CommissionType type)
    {
        if (type == CommissionType.Issue)
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
        else
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
}
