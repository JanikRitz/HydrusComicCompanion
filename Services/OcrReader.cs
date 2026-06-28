using System.Text.Json;
using System.Text.Json.Serialization;

namespace HydrusComicCompanion.Services;

public class OcrReader : IOcrReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public OcrData ReadOcrDataForFile(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<OcrData>(json, JsonOptions)!;
    }

    public string ReadOcrPlaintextForFile(string path, IEnumerable<string>? textTypesToExclude = null)
    {
        var excludeSet = (textTypesToExclude ?? new[] { "WATERMARK" }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var data = ReadOcrDataForFile(path);

        // Order blocks by their "order" property, join segments with space, then join blocks with newline
        var lines = new List<string>();
        foreach (var block in data.Blocks.OrderBy(b => b.Order))
        {
            // Skip blocks with excluded text types (e.g. WATERMARK)
            if (!string.IsNullOrEmpty(block.TextType) && excludeSet.Contains(block.TextType))
            {
                continue;
            }

            var segmentIds = new List<string>();
            foreach (var uid in block.SegmentIds)
            {
                if (data.Segments.FirstOrDefault(s => s.Uid == uid) is { Text: not null and not "" } segment)
                {
                    segmentIds.Add(segment.Text);
                }
            }

            var line = string.Join(" ", segmentIds);
            if (!string.IsNullOrEmpty(line))
            {
                lines.Add(line);
            }
        }

        return string.Join("\n", lines);
    }
}
