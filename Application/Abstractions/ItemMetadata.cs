namespace RetainerMarketScout.Application.Abstractions;

public sealed class ItemMetadata
{
    public required int ItemId { get; init; }
    public string? IconPath { get; init; }
}
