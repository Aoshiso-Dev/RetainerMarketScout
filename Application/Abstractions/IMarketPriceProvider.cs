using RetainerMarketScout.Domain.Entities;

namespace RetainerMarketScout.Application.Abstractions;

public interface IMarketPriceProvider
{
    Task<MarketResult> GetMarketResultAsync(
        string worldOrDc,
        CandidateItem candidate,
        int recentSalesDays,
        CancellationToken cancellationToken);
}
