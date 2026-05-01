using System.IO;
using System.Net.Http;
using System.Text.Json;
using RetainerMarketScout.Application.Abstractions;

namespace RetainerMarketScout.Infrastructure.XivApi;

public sealed class XivApiItemIdResolver(HttpClient httpClient) : IItemIdResolver
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ItemMetadata?> _cache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> KnownItemAliases = new(StringComparer.Ordinal)
    {
        ["ヤースラガーリック"] = 43985,
        ["ヤースラニガーリック"] = 43985,
        ["ウォームトマト"] = 43978,
        ["ウオームトマト"] = 43978,
        ["ウトォームトマト"] = 43978
    };

    public async Task<ItemMetadata?> ResolveItemAsync(string itemName, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(itemName, out var cachedItem))
        {
            return cachedItem;
        }

        if (KnownItemAliases.TryGetValue(itemName, out var knownItemId))
        {
            var knownMetadata = await ResolveKnownItemAsync(knownItemId, cancellationToken);
            _cache[itemName] = knownMetadata;
            return knownMetadata;
        }

        var query = Uri.EscapeDataString($"Name@ja=\"{itemName}\"");
        var requestUri = $"search?sheets=Item&fields=Name,Icon&limit=1&query={query}";

        await using var stream = await httpClient.GetStreamAsync(requestUri, cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        ItemMetadata? metadata = null;
        if (root.TryGetProperty("results", out var results) &&
            results.ValueKind == JsonValueKind.Array &&
            results.GetArrayLength() > 0 &&
            results[0].TryGetProperty("row_id", out var rowId) &&
            rowId.TryGetInt32(out var itemId))
        {
            metadata = new ItemMetadata
            {
                ItemId = itemId,
                IconPath = await CacheIconAsync(results[0], cancellationToken)
            };
        }

        _cache[itemName] = metadata;
        return metadata;
    }

    private async Task<ItemMetadata?> ResolveKnownItemAsync(int itemId, CancellationToken cancellationToken)
    {
        var requestUri = $"sheet/Item/{itemId}?fields=Name,Icon";

        await using var stream = await httpClient.GetStreamAsync(requestUri, cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        return new ItemMetadata
        {
            ItemId = itemId,
            IconPath = await CacheIconAsync(root, cancellationToken)
        };
    }

    private async Task<string?> CacheIconAsync(JsonElement searchResult, CancellationToken cancellationToken)
    {
        if (!searchResult.TryGetProperty("fields", out var fields) ||
            !fields.TryGetProperty("Icon", out var icon) ||
            icon.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var iconId = icon.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var parsedId)
            ? parsedId
            : 0;
        var iconPath = ReadIconPath(icon);
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return null;
        }

        var cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RetainerMarketScout",
            "ItemIcons");
        Directory.CreateDirectory(cacheDirectory);

        var fileName = iconId > 0
            ? $"{iconId}.png"
            : $"{Uri.EscapeDataString(iconPath).Replace("%", string.Empty)}.png";
        var cachedPath = Path.Combine(cacheDirectory, fileName);
        if (File.Exists(cachedPath))
        {
            return cachedPath;
        }

        var assetUri = $"asset?path={Uri.EscapeDataString(iconPath)}&format=png";
        var bytes = await httpClient.GetByteArrayAsync(assetUri, cancellationToken);
        await File.WriteAllBytesAsync(cachedPath, bytes, cancellationToken);
        return cachedPath;
    }

    private static string? ReadIconPath(JsonElement icon)
    {
        if (icon.TryGetProperty("path_hr1", out var highResolutionPath))
        {
            return highResolutionPath.GetString();
        }

        return icon.TryGetProperty("path", out var path)
            ? path.GetString()
            : null;
    }
}
