using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Cet4LearningTool;

/// <summary>单词书里的一个小单元，如 U1-1。</summary>
internal sealed record WordUnit(int Book, int Section, string Level, IReadOnlyList<string> Words)
{
    public string Id => $"U{Book}-{Section}";

    /// <summary>接受 U1-1 与 1-1 两种写法。</summary>
    public bool Matches(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith('U') || trimmed.StartsWith('u'))
            trimmed = trimmed[1..];
        return string.Equals(trimmed.Replace(" ", string.Empty), $"{Book}-{Section}", StringComparison.Ordinal);
    }
}

/// <summary>
/// 一关：同一本单词书里同一个 L 标记下的全部小单元，如 U1-L1 = U1-1~U1-4。生成按关进行。
/// L 标记只在本书内有意义（每本都有 L1），所以编号带书号。
/// </summary>
internal sealed record WordLevel(int Book, string Level, IReadOnlyList<WordUnit> Units)
{
    public string Id => $"U{Book}-{Level}";

    /// <summary>各小单元单词按顺序合并；相邻小单元常有重复词，只留一个。</summary>
    public IReadOnlyList<string> Words { get; } =
        Units.SelectMany(unit => unit.Words).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public string Range => Units.Count == 1 ? Units[0].Id : $"{Units[0].Id}~{Units[^1].Id}";

    /// <summary>接受 U1-L1、1-L1、U1L1 几种写法，不分大小写。</summary>
    public bool Matches(string text)
    {
        var trimmed = text.Trim().Replace(" ", string.Empty).ToUpperInvariant();
        if (trimmed.StartsWith('U'))
            trimmed = trimmed[1..];
        return trimmed == $"{Book}-{Level}" || trimmed == $"{Book}{Level}";
    }
}

/// <summary>
/// 读 a1-四级单词/U*.docx：每段一行，形如「L1 1-1」「1-2」的段落开一个小单元，其余非空段是单词。
/// 关的标记也可能单独占一行（U2 的「L1」「L2」「L4」）。
/// </summary>
internal static partial class WordBook
{
    [GeneratedRegex(@"^(?:(L\d+)\s+)?(\d+)\s*-\s*(\d+)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^L\d+$")]
    private static partial Regex LevelMark();

    /// <summary>相邻且同书同关的小单元归成一关；没有关标记的小单元记作 L0。</summary>
    public static IReadOnlyList<WordLevel> Levels(string directory)
    {
        var levels = new List<WordLevel>();
        foreach (var unit in Load(directory))
        {
            var level = unit.Level.Length == 0 ? "L0" : unit.Level;
            if (levels.Count > 0 && levels[^1].Book == unit.Book && levels[^1].Level == level)
                levels[^1] = new WordLevel(unit.Book, level, [.. levels[^1].Units, unit]); // 不用 with：Words 不会重算
            else
                levels.Add(new WordLevel(unit.Book, level, [unit]));
        }

        return levels;
    }

    [GeneratedRegex(@"^U(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex BookFile();

    public static IReadOnlyList<WordUnit> Load(string directory)
    {
        var units = new List<WordUnit>();
        var books = Directory.EnumerateFiles(directory, "U*.docx")
            .Select(path => (path, match: BookFile().Match(Path.GetFileNameWithoutExtension(path))))
            .Where(item => item.match.Success)
            .OrderBy(item => int.Parse(item.match.Groups[1].Value));

        foreach (var (path, _) in books)
        {
            var level = string.Empty;
            (int book, int section, string level, List<string> words)? current = null;
            foreach (var line in DocxText.Paragraphs(path))
            {
                var text = line.Trim();
                if (text.Length == 0)
                    continue;

                if (LevelMark().IsMatch(text))
                {
                    Flush();
                    level = text;
                    continue;
                }

                var heading = Heading().Match(text);
                if (heading.Success)
                {
                    Flush();
                    if (heading.Groups[1].Success)
                        level = heading.Groups[1].Value;
                    current = (int.Parse(heading.Groups[2].Value), int.Parse(heading.Groups[3].Value), level, new List<string>());
                }
                else
                {
                    current?.words.Add(text);
                }
            }

            Flush();

            void Flush()
            {
                if (current is { words.Count: > 0 } unit)
                    units.Add(new WordUnit(unit.book, unit.section, unit.level, unit.words));
                current = null;
            }
        }

        return units;
    }
}

/// <summary>docx 的最小读取：只取段落纯文本。</summary>
internal static class DocxText
{
    public static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static IEnumerable<string> Paragraphs(string path)
    {
        // Word 开着文档时仍允许读。
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException($"{path} 不是 Word 文档");
        using var xml = entry.Open();
        var document = XDocument.Load(xml);
        return document.Descendants(W + "p")
            .Select(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value)))
            .ToList();
    }
}
