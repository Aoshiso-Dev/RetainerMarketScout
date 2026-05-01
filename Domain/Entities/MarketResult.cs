using System.Globalization;

namespace RetainerMarketScout.Domain.Entities;

public sealed class MarketResult
{
    public required int Score { get; init; }
    public required int? ItemId { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required int Level { get; init; }
    public required string RequirementType { get; init; }
    public required int RequirementValue { get; init; }
    public required int Quantity { get; init; }
    public string? IconPath { get; init; }
    public required int? LowestPrice { get; init; }
    public required int? SaleAverage { get; init; }
    public required int SalesCount { get; init; }
    public required int SalesQuantity { get; init; }
    public required int FullStackSalesCount { get; init; }
    public required DateTime? UpdatedAt { get; init; }
    public required string Notes { get; init; }

    public string ScoreText => FormatGil(Score);
    public string LowestPriceText => FormatGil(LowestPrice);
    public string SaleAverageText => FormatGil(SaleAverage);
    public int FullStackSalesRank
    {
        get
        {
            if (SalesCount <= 0)
            {
                return 0;
            }

            var ratio = FullStackSalesCount / (double)SalesCount;
            return ratio switch
            {
                >= 0.3 => 3,
                >= 0.2 => 2,
                >= 0.1 => 1,
                _ => 0
            };
        }
    }

    public string FullStackSalesBrush => FullStackSalesRank switch
    {
        3 => "#F5C542",
        2 => "#C9D1DA",
        1 => "#C8894A",
        _ => "Transparent"
    };

    public string FullStackSalesTooltip => FullStackSalesRank switch
    {
        3 => "99個売り比率 30%以上",
        2 => "99個売り比率 20%以上",
        1 => "99個売り比率 10%以上",
        _ => "99個売り比率 10%未満"
    };

    public string UpdatedText => UpdatedAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture) ?? "-";

    public static MarketResult Failed(CandidateItem candidate, string message)
    {
        return new MarketResult
        {
            Score = 0,
            ItemId = candidate.ItemId,
            Name = candidate.Name,
            Category = candidate.Category,
            Level = candidate.Level,
            RequirementType = candidate.RequirementType,
            RequirementValue = candidate.RequirementValue,
            Quantity = Math.Max(1, candidate.QuantityPerVenture),
            IconPath = candidate.IconPath,
            LowestPrice = null,
            SaleAverage = null,
            SalesCount = 0,
            SalesQuantity = 0,
            FullStackSalesCount = 0,
            UpdatedAt = null,
            Notes = $"取得失敗: {message}"
        };
    }

    private static string FormatGil(int? value)
    {
        return value?.ToString("N0", CultureInfo.CurrentCulture) ?? "-";
    }
}
