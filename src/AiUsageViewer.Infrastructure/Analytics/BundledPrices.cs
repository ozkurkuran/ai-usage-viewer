using System.Text.Json;
using AiUsageViewer.Core;

namespace AiUsageViewer.Infrastructure.Analytics;
public static class BundledPrices
{
    public static PriceCatalog Load()
    {
        using var stream=typeof(BundledPrices).Assembly.GetManifestResourceStream("AiUsageViewer.Prices.json")!;
        return new(JsonSerializer.Deserialize<List<ModelPrice>>(stream,new JsonSerializerOptions { PropertyNameCaseInsensitive=true })??[]);
    }
}
