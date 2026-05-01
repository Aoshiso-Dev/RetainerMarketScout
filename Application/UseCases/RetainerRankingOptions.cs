namespace RetainerMarketScout.Application.UseCases;

public sealed class RetainerRankingOptions
{
    public required string WorldOrDc { get; init; }
    public required bool ConnectExpressVpnBeforeRefresh { get; init; }
    public string? ExpressVpnLocation { get; init; }
    public required int MaxConcurrentRequests { get; init; }
    public required int RecentSalesDays { get; init; }
}
