namespace HanelSoftVLM.Templates;

public static class ItemDefinitionBuilder
{
    public static string Build(string itemNumber, string? manufacturer, string? productLine)
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
          "extensions" : [ ],
          "locks" : [ ],
          "storageZones" : [ ],
          "itemAttributes" : [ {{itemAttributesJson}} ],
          "dedicatedStorageSpaces" : [ ]
        }
        """;
    }
}
