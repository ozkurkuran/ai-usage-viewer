using AiUsageViewer.Core;
using AiUsageViewer.Infrastructure.Analytics;

namespace AiUsageViewer.Tests;
public sealed class PricingTests
{
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-09-27T12:00:00Z");
    private static ModelPrice Price(string model="model",string currency="USD")=>new(model,Now.AddDays(-1),2,10,0.2m,2.5m,4,"test",currency);
    private static UsageEvent Event(string model="model",TokenUsage? tokens=null)=>new(Guid.NewGuid().ToString(),ProviderKind.Claude,"s","p",model,Now.AddMonths(-1),tokens??new(1_000_000,100_000,500_000,200_000,100_000,50_000));
    [Fact] public void AllDisjointBucketsUseDecimalRatesAndReasoningIsNotChargedTwice()=>Assert.Equal(4m,Price().Estimate(Event().Tokens));
    [Fact] public void UnknownModelsAreNotFreeOrFamilyMatched()
    {
        var result=new PriceCatalog([Price()]).Estimate([Event("model-next"),Event()],[],Now);
        Assert.Equal(1,result.UnknownRecords);Assert.Equal(1,result.PricedRecords);Assert.Equal(4,result.Amounts["USD"]);
    }
    [Fact] public void MissingCacheRateOnlyBlocksEventsThatUseIt()
    {
        var price=Price() with { CacheWrite1hPerMillion=null };
        Assert.Null(price.Estimate(Event().Tokens));Assert.Equal(2,price.Estimate(new(1_000_000)));
    }
    [Fact] public void OverridesWinAndCurrenciesStaySeparate()
    {
        var result=new PriceCatalog([Price(),Price("other","EUR")]).Estimate([Event(),Event("other")],[Price() with { InputPerMillion=3 }],Now);
        Assert.Equal(5,result.Amounts["USD"]);Assert.Equal(4,result.Amounts["EUR"]);
    }
    [Fact] public void FutureTariffsDoNotApplyAndCurrentTariffsAreExplicitlyUsedForPastEvents()
    {
        var result=new PriceCatalog([Price(),Price() with { EffectiveFrom=Now.AddDays(1),InputPerMillion=99 }]).Estimate([Event()],[],Now);
        Assert.Equal(4,result.Amounts["USD"]);Assert.Equal(Now,result.TariffDate);
    }
    [Fact] public void BundledCatalogHasValidExactIdsAndSourceDates()
    {
        var entries=BundledPrices.Load().Entries;Assert.NotEmpty(entries);
        Assert.All(entries,p=> { Assert.True(p.IsValid);Assert.StartsWith("https://",p.Source);Assert.True(p.EffectiveFrom.Year>=2026); });
    }
    [Fact] public async Task CsvEscapesFormulaAndQuotedProjectNamesWithoutContentOrSecrets()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".csv");
        try
        {
            await UsageExport.WriteAsync(path,[Event() with { Project="=SUM(1,2)\"test" }]);
            var csv=await File.ReadAllTextAsync(path);Assert.Contains("'\u003dSUM(1,2)\"\"test",csv);Assert.DoesNotContain("AccountId",csv);
        }
        finally { File.Delete(path); }
    }
}
