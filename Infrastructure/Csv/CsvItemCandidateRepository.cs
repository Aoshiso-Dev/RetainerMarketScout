using System.Globalization;
using System.IO;
using System.Text;
using RetainerMarketScout.Application.Abstractions;
using RetainerMarketScout.Domain.Entities;

namespace RetainerMarketScout.Infrastructure.Csv;

public sealed class CsvItemCandidateRepository : IItemCandidateRepository
{
    public string ItemsPath { get; } = FindItemsPath();

    public IReadOnlyList<CandidateItem> Load()
    {
        var items = new List<CandidateItem>();
        var lines = File.ReadAllLines(ItemsPath, Encoding.UTF8);
        if (lines.Length == 0)
        {
            return items;
        }

        var headers = SplitCsvLine(lines[0]);
        var isRetainerMaterialList = headers.Contains("素材名");

        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = SplitCsvLine(line);
            if (isRetainerMaterialList)
            {
                AddRetainerMaterial(items, columns);
            }
            else
            {
                AddLegacyItem(items, columns);
            }
        }

        return items;
    }

    private static void AddRetainerMaterial(List<CandidateItem> items, List<string> columns)
    {
        if (columns.Count < 5)
        {
            return;
        }

        _ = int.TryParse(columns[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var level);
        _ = int.TryParse(columns[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var requirementValue);
        _ = int.TryParse(GetColumn(columns, 5), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity);
        var yieldMemo = GetColumn(columns, 6);
        var requirementNote = $"{columns[3]} {columns[4]}";

        items.Add(new CandidateItem
        {
            ItemId = null,
            Category = columns[0],
            Level = level,
            Name = columns[2],
            RequirementType = columns[3],
            RequirementValue = requirementValue,
            QuantityPerVenture = Math.Max(1, quantity),
            IconPath = null,
            Notes = string.IsNullOrWhiteSpace(yieldMemo)
                ? requirementNote
                : $"{requirementNote} / {yieldMemo}"
        });
    }

    private static void AddLegacyItem(List<CandidateItem> items, List<string> columns)
    {
        if (columns.Count < 5 ||
            !int.TryParse(columns[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId))
        {
            return;
        }

        _ = int.TryParse(columns[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity);
        items.Add(new CandidateItem
        {
            ItemId = itemId,
            Name = columns[1],
            Category = columns[2],
            Level = 0,
            RequirementType = string.Empty,
            RequirementValue = 0,
            QuantityPerVenture = Math.Max(1, quantity),
            IconPath = null,
            Notes = columns[4]
        });
    }

    private static string FindItemsPath()
    {
        var appDirectoryCsv = Path.Combine(AppContext.BaseDirectory, "retainer_items.csv");
        if (File.Exists(appDirectoryCsv))
        {
            return appDirectoryCsv;
        }

        var currentDirectoryCsv = Path.Combine(Environment.CurrentDirectory, "retainer_items.csv");
        if (File.Exists(currentDirectoryCsv))
        {
            return currentDirectoryCsv;
        }

        throw new FileNotFoundException("retainer_items.csv が見つかりません。", currentDirectoryCsv);
    }

    private static List<string> SplitCsvLine(string line)
    {
        var values = new List<string>();
        var current = new List<char>();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];
            if (character == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Add('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (character == ',' && !inQuotes)
            {
                values.Add(new string(current.ToArray()));
                current.Clear();
            }
            else
            {
                current.Add(character);
            }
        }

        values.Add(new string(current.ToArray()));
        return values;
    }

    private static string GetColumn(IReadOnlyList<string> columns, int index)
    {
        return index < columns.Count ? columns[index] : string.Empty;
    }
}
