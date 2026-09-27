namespace AiUsageViewer.Core;

public sealed record CostSummary(IReadOnlyDictionary<string,decimal> Amounts,long PricedRecords,long UnknownRecords,
    IReadOnlyList<string> UnknownModels,DateTimeOffset TariffDate);

public sealed class PriceCatalog(IReadOnlyList<ModelPrice> bundled)
{
    public IReadOnlyList<ModelPrice> Entries=>bundled;
    // Estimates use the selected tariff date for all events. This is explicitly
    // a current-tariff equivalent, not a reconstructed historical invoice.
    public CostSummary Estimate(IEnumerable<UsageEvent> events,IReadOnlyList<ModelPrice> overrides,DateTimeOffset tariffDate)
    {
        var chosen=bundled.Where(p=>p.IsValid&&p.EffectiveFrom<=tariffDate).GroupBy(p=>p.Model,StringComparer.Ordinal)
            .ToDictionary(g=>g.Key,g=>g.MaxBy(p=>p.EffectiveFrom)!,StringComparer.Ordinal);
        foreach(var group in overrides.Where(p=>p.IsValid&&p.EffectiveFrom<=tariffDate).GroupBy(p=>p.Model,StringComparer.Ordinal))
            chosen[group.Key]=group.MaxBy(p=>p.EffectiveFrom)!;
        var totals=new Dictionary<string,decimal>();var unknown=new HashSet<string>();long knownCount=0,unknownCount=0;
        foreach(var e in events.Where(e=>e.Identity!=IdentityQuality.UncertainFork))
        {
            if(chosen.TryGetValue(e.Model,out var price)&&price.Estimate(e.Tokens) is { } amount)
            { totals[price.Currency]=totals.GetValueOrDefault(price.Currency)+amount;knownCount++; }
            else { unknown.Add(e.Model);unknownCount++; }
        }
        return new(totals,knownCount,unknownCount,unknown.Order().ToList(),tariffDate);
    }
}
