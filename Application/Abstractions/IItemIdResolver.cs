namespace RetainerMarketScout.Application.Abstractions;

public interface IItemIdResolver
{
    Task<ItemMetadata?> ResolveItemAsync(string itemName, CancellationToken cancellationToken);
}
