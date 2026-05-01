using RetainerMarketScout.Application.Abstractions;
using RetainerMarketScout.Domain.Entities;

namespace RetainerMarketScout.Application.UseCases;

public sealed class RankRetainerTargetsUseCase(
    IItemCandidateRepository candidateRepository,
    IMarketPriceProvider marketPriceProvider,
    IItemIdResolver itemIdResolver,
    IExpressVpnClient expressVpnClient)
{
    public string ItemsPath => candidateRepository.ItemsPath;

    public async Task<IReadOnlyList<MarketResult>> ExecuteAsync(
        RetainerRankingOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var world = options.WorldOrDc.Trim();
        if (string.IsNullOrWhiteSpace(world))
        {
            throw new InvalidOperationException("World または Data Center を入力してください。");
        }

        if (options.ConnectExpressVpnBeforeRefresh)
        {
            progress?.Report("ExpressVPN MCPへ接続しています...");
            ExpressVpnConnectionResult vpnResult;
            try
            {
                vpnResult = await expressVpnClient.EnsureConnectedAsync(options.ExpressVpnLocation, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException($"ExpressVPN MCP接続に失敗しました。{ex.Message}", ex);
            }

            if (!vpnResult.IsConnected)
            {
                throw new InvalidOperationException($"ExpressVPN MCP接続に失敗しました。{vpnResult.Message}");
            }

            progress?.Report($"ExpressVPN MCP実行済み: {vpnResult.Message}");
        }

        var candidates = candidateRepository.Load();
        var fetched = new List<MarketResult>();
        var completedCount = 0;
        var maxConcurrency = Math.Clamp(options.MaxConcurrentRequests, 1, 12);
        var recentSalesDays = Math.Clamp(options.RecentSalesDays, 1, 30);

        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrency,
                CancellationToken = cancellationToken
            },
            async (candidate, token) =>
            {
                MarketResult result;
                var currentCount = Interlocked.Increment(ref completedCount);
                progress?.Report($"取得中 {currentCount} / {candidates.Count}: {candidate.Name}");

                try
                {
                    var marketCandidate = await ResolveItemIdAsync(candidate, token);
                    result = await marketPriceProvider.GetMarketResultAsync(world, marketCandidate, recentSalesDays, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    result = MarketResult.Failed(candidate, ex.Message);
                }

                lock (fetched)
                {
                    fetched.Add(result);
                }
            });

        return fetched
            .OrderByDescending(item => item.Score)
            .ToList();
    }

    private async Task<CandidateItem> ResolveItemIdAsync(CandidateItem candidate, CancellationToken cancellationToken)
    {
        if (candidate.ItemId is > 0)
        {
            return candidate;
        }

        var item = await itemIdResolver.ResolveItemAsync(candidate.Name, cancellationToken);
        if (item is null)
        {
            throw new InvalidOperationException($"アイテムIDを解決できませんでした: {candidate.Name}");
        }

        return new CandidateItem
        {
            ItemId = item.ItemId,
            Name = candidate.Name,
            Category = candidate.Category,
            Level = candidate.Level,
            RequirementType = candidate.RequirementType,
            RequirementValue = candidate.RequirementValue,
            QuantityPerVenture = candidate.QuantityPerVenture,
            IconPath = item.IconPath,
            Notes = candidate.Notes
        };
    }
}
