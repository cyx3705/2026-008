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

/// <summary>读 a1-四级单词/U*.docx：每段一行，形如「L1 1-1」「1-2」的段落开一个小单元，其余非空段是单词。</summary>
internal static partial class WordBook
{
    [GeneratedRegex(@"^(?:(L\d+)\s+)?(\d+)\s*-\s*(\d+)$")]
    private static partial Regex Heading();

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
