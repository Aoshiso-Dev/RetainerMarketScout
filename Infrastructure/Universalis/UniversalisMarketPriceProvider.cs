using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using RetainerMarketScout.Application.Abstractions;
using RetainerMarketScout.Domain.Entities;

namespace RetainerMarketScout.Infrastructure.Universalis;

public sealed class UniversalisMarketPriceProvider(HttpClient httpClient) : IMarketPriceProvider
{
    public async Task<MarketResult> GetMarketResultAsync(
        string worldOrDc,
        CandidateItem candidate,
        int recentSalesDays,
        CancellationToken cancellationToken)
    {
        if (candidate.ItemId is not > 0)
        {
            throw new InvalidOperationException($"アイテムIDがありません: {candidate.Name}");
        }

        var escapedWorld = Uri.EscapeDataString(worldOrDc);
        var uri = $"{escapedWorld}/{candidate.ItemId}?listings=20&entries=100";

        await using var stream = await httpClient.GetStreamAsync(uri, cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        var minPrice = FirstPositiveNumber(root, "minPriceNQ", "minPriceHQ", "minPrice", "currentAveragePriceNQ", "currentAveragePriceHQ", "currentAveragePrice");
        var sales = ReadRecentSales(root, recentSalesDays);
        var saleAverage = sales.AverageUnitPrice;
        var recency = CalculateRecentSaleWeight(sales.LatestSale);
        var quantity = Math.Max(1, candidate.QuantityPerVenture);
        var unitBasis = saleAverage ?? 0;
        var effectiveSalesCount = sales.Count + (sales.FullStackCount * 2);
        var transactionBase = effectiveSalesCount / (effectiveSalesCount + 20.0);
        var transactionLiquidity = Math.Pow(transactionBase, 2);
        var quantityLiquidity = sales.Quantity / (sales.Quantity + 100.0);
        var fullStackBonus = 1.0 + (Math.Min(sales.FullStackCount, 10) / 10.0);
        var liquidity = transactionLiquidity * quantityLiquidity * fullStackBonus;
        var score = (int)Math.Round(unitBasis * quantity * liquidity * recency);

        return new MarketResult
        {
            Score = score,
            ItemId = candidate.ItemId,
            Name = candidate.Name,
            Category = candidate.Category,
            Level = candidate.Level,
            RequirementType = candidate.RequirementType,
            RequirementValue = candidate.RequirementValue,
            Quantity = quantity,
            IconPath = candidate.IconPath,
            LowestPrice = ToNullableInt(minPrice),
            SaleAverage = ToNullableInt(saleAverage),
            SalesCount = sales.Count,
            SalesQuantity = sales.Quantity,
            FullStackSalesCount = sales.FullStackCount,
            UpdatedAt = ReadUploadTime(root),
            Notes = candidate.Notes
        };
    }

    private static int? ToNullableInt(double? value)
    {
        return value is null ? null : (int)Math.Round(value.Value);
    }

    private static (int Count, int Quantity, int FullStackCount, double? AverageUnitPrice, DateTimeOffset? LatestSale) ReadRecentSales(
        JsonElement root,
        int recentSalesDays)
    {
        if (!root.TryGetProperty("recentHistory", out var history) || history.ValueKind != JsonValueKind.Array)
        {
            return (0, 0, 0, null, null);
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-recentSalesDays);
        var count = 0;
        var quantity = 0;
        var fullStackCount = 0;
        var totalValue = 0.0;
        DateTimeOffset? latestSale = null;
        foreach (var entry in history.EnumerateArray())
        {
            var price = ReadNumber(entry, "pricePerUnit");
            var timestamp = ReadSaleTimestamp(entry);
            if (price is not > 0 || timestamp is null || timestamp.Value < cutoff)
            {
                continue;
            }

            var entryQuantity = Math.Max(1, (int)Math.Round(ReadNumber(entry, "quantity") ?? 1));
            count++;
            quantity += entryQuantity;
            totalValue += price.Value * entryQuantity;
            if (entryQuantity >= 99)
            {
                fullStackCount++;
            }

            if (latestSale is null || timestamp.Value > latestSale.Value)
            {
                latestSale = timestamp;
            }
        }

        double? averageUnitPrice = quantity > 0
            ? totalValue / quantity
            : null;

        return (count, quantity, fullStackCount, averageUnitPrice, latestSale);
    }

    private static DateTime? ReadUploadTime(JsonElement root)
    {
        var millis = ReadNumber(root, "lastUploadTime");
        if (millis is null or <= 0)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds((long)millis.Value).LocalDateTime;
    }

    private static double CalculateRecentSaleWeight(DateTimeOffset? latestSale)
    {
        if (latestSale is null)
        {
            return 0;
        }

        var age = DateTimeOffset.UtcNow - latestSale.Value;
        var ageDays = Math.Max(1, Math.Ceiling(age.TotalDays));
        return 1.0 / ageDays;
    }

    private static DateTimeOffset? ReadSaleTimestamp(JsonElement entry)
    {
        var saleTime = ReadNumber(entry, "timestamp") ?? ReadNumber(entry, "saleTime");
        if (saleTime is null or <= 0)
        {
            return null;
        }

        return saleTime.Value > 9_999_999_999
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)saleTime.Value)
            : DateTimeOffset.FromUnixTimeSeconds((long)saleTime.Value);
    }

    private static double? FirstPositiveNumber(JsonElement root, params string[] paths)
    {
        foreach (var path in paths)
        {
            var number = path.Contains('[')
                ? ReadIndexedNumber(root, path)
                : ReadNumber(root, path);

            if (number is > 0)
            {
                return number;
            }
        }

        return null;
    }

    private static double? ReadIndexedNumber(JsonElement root, string path)
    {
        if (path != "recentHistory[0].pricePerUnit")
        {
            return null;
        }

        if (!root.TryGetProperty("recentHistory", out var history) ||
            history.ValueKind != JsonValueKind.Array ||
            history.GetArrayLength() == 0)
        {
            return null;
        }

        return ReadNumber(history[0], "pricePerUnit");
    }

    private static double? ReadNumber(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }
}
