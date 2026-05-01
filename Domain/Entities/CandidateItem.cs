namespace RetainerMarketScout.Domain.Entities;

public sealed class CandidateItem
{
    public int? ItemId { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required int Level { get; init; }
    public required string RequirementType { get; init; }
    public required int RequirementValue { get; init; }
    public required int QuantityPerVenture { get; init; }
    public string? IconPath { get; init; }
    public required string Notes { get; init; }
}
