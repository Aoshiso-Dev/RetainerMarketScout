using RetainerMarketScout.Domain.Entities;

namespace RetainerMarketScout.Application.Abstractions;

public interface IItemCandidateRepository
{
    string ItemsPath { get; }

    IReadOnlyList<CandidateItem> Load();
}
