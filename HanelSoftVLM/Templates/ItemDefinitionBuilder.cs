namespace HanelSoftVLM.Templates;

public static class ItemDefinitionBuilder
{
    public static string Build(string itemNumber, string? manufacturer, string? productLine, IEnumerable<string>? additionalItemNumbers = null)
    {
        // Build itemAttributes only if manufacturer/productLine available (Issue commissions only)
        var itemAttributesJson = "";
        if (!string.IsNullOrEmpty(manufacturer) && !string.IsNullOrEmpty(productLine))
        {
            itemAttributesJson = $$"""
            {
              "attribute" : "ProdLine",
              "value" : "{{productLine}}"
            }, {
              "attribute" : "MFR",
              "value" : "{{manufacturer}}"
            }
            """;
        }

        // Build extensions with additionalItemNumberExtension if additional SKUs exist
        var extensionsJson = "[ ]";
        var additionalItems = additionalItemNumbers?.ToList();
        if (additionalItems != null && additionalItems.Count > 0)
        {
            var itemNumbersArray = string.Join(", ", additionalItems.Select(i => $"\"{i}\""));
            extensionsJson = $$"""
            [ {
                "@type" : "additionalItemNumberExtension",
                "additionalItemNumbers" : [ {{itemNumbersArray}} ]
              } ]
            """;
        }

        return $$"""
        {
          "identifier" : "{{itemNumber}}",
          "description" : null,
          "group" : null,
          "measure" : "Piece",
          "width" : null,
          "depth" : null,
          "height" : null,
          "loadUnitType" : null,
          "reorderPoint" : 0,
          "remainingQuantityThreshold" : null,
          "defaultBookingQuantity" : null,
          "dedicatedStorageSpacesAllowed" : true,
          "storeStrategy" : {
            "@type" : "minimumPicksStrategy"
          },
          "disposable" : false,
          "clearStockForIssue" : false,
          "quantAttributeSettings" : [ {
            "quantAttribute" : "Serial_Number",
            "requiredOnIssue" : "OPTIONAL"
          } ],
          "extensions" : {{extensionsJson}},
          "locks" : [ ],
          "storageZones" : [ ],
          "itemAttributes" : [ {{itemAttributesJson}} ],
          "dedicatedStorageSpaces" : [ ]
        }
        """;
    }
}
