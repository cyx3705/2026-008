using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using HistoryVulcan.Core.Commands;

namespace Cet4LearningTool;

internal sealed record Sentence(string Zh, string En);

internal sealed record Article(string Title, IReadOnlyList<Sentence> Sentences);

/// <summary>文档里的一段文字；Bold 为真时加粗。</summary>
internal sealed record Segment(string Text, bool Bold = false);

/// <summary>造文：拼 apollo.chat.send 指令、解析答复、排成抄写文档的行。</summary>
internal static class ArticleWriter
{
    private const string SystemPrompt =
        "你是大学英语四级翻译题的出题老师。只输出一个 JSON 对象，不要任何解释或代码块标记。";

    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static List<Dictionary<string, string>> StartMessages(WordUnit unit)
        => [Message("system", SystemPrompt), Message("user", BuildPrompt(unit))];

    public static Dictionary<string, string> Message(string role, string content)
        => new() { ["role"] = role, ["content"] = content };

    /// <summary>
    /// 发给总线的一行。上下文由本模块持有（Apollo 不存历史），修改轮把上一稿和缺词说明追加进来再发。
    /// messages 经 JSON 序列化后没有裸换行，再经 QuoteArg 编码。
    /// </summary>
    public static string SendCommand(IReadOnlyList<Dictionary<string, string>> messages)
        => "apollo.chat.send json=true maxtokens=4000 timeout=240 messages="
           + CommandParser.QuoteArg(JsonSerializer.Serialize(messages, Options));

    private static string BuildPrompt(WordUnit unit)
        => $"下面是四级单词书小单元 {unit.Id} 的 {unit.Words.Count} 个单词：{string.Join(", ", unit.Words)}。"
           + "请写四级翻译题风格的中文短文，并逐句给出低配简易的英文译文：用词不超过四级、句式简单、适合抄写。"
           + $"硬性要求：这 {unit.Words.Count} 个单词必须全部出现在英文译文里，一个都不能少。"
           + WordRule
           + "每句自然地用 1 到 3 个单元单词，不要在一句里堆砌同根词（例如不要写 assess this assessment）；"
           + "单词多时把文章写长，超过 20 个单词时优先分成两篇不同话题的短文，每篇一般 6 到 12 句。"
           + "话题贴近四级翻译真题（中国文化、社会生活、科技教育等），句子要通顺自然，不要硬凑。"
           + "按这个 JSON 输出：{\"articles\":[{\"title\":\"中文标题，2 到 6 个字\",\"sentences\":[{\"zh\":\"中文句子\",\"en\":\"English sentence\"}]}]}";

    /// <summary>与 <see cref="WordMatcher"/> 的判据一致：说给模型听的规则就是程序核对的规则。</summary>
    private const string WordRule =
        "单词只能用原形，或者只加 -s/-es/-ed/-ing/-er/-est 这类规则词尾；不要用不规则变形，"
        + "不要换成近义词或派生词（例如 success 不能用 successful 代替，act 不能用 action 代替）。";

    public static string MissingWordsPrompt(IReadOnlyList<string> missing)
        => $"还有 {missing.Count} 个单词没有出现在英文译文里：{string.Join(", ", missing)}。"
           + WordRule
           + "请在不丢掉已用上单词的前提下修改：可以改写句子、加句子，或者再加一篇。输出修改后的完整 JSON，格式不变。";

    public static string FormatPrompt(string error)
        => $"上次的输出不符合约定格式（{error}）。请按约定的 JSON 格式重新输出完整内容。";

    public static IReadOnlyList<Article> Parse(string content)
    {
        var text = content.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var start = text.IndexOf('\n');
            var end = text.LastIndexOf("```", StringComparison.Ordinal);
            if (start > 0 && end > start)
                text = text[(start + 1)..end];
        }

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var articles = new List<Article>();
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("articles", out var list)
            && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
                AddArticle(item);
        }
        else
        {
            AddArticle(root);
        }

        if (articles.Count == 0)
            throw new FormatException("没有可用的 articles");
        return articles;

        void AddArticle(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return;
            var sentences = new List<Sentence>();
            if (element.TryGetProperty("sentences", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    var zh = Text(item, "zh");
                    var en = Text(item, "en");
                    if (!string.IsNullOrEmpty(zh) && !string.IsNullOrEmpty(en))
                        sentences.Add(new Sentence(zh, en));
                }
            }

            if (sentences.Count > 0)
                articles.Add(new Article(Text(element, "title") is { Length: > 0 } title ? title : "短文", sentences));
        }
    }

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    /// <summary>
    /// 沿用现有抄写文档的纯文本排法：# 标题、## 第N篇、「序号. 中文」下一行英文、句间留空行。
    /// 英文里的单元单词加粗；末尾列出本单元单词。
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Segment>> Lines(WordUnit unit, IReadOnlyList<Article> articles)
    {
        var lines = new List<IReadOnlyList<Segment>>
        {
            Plain($"# 四级翻译短篇抄写（{unit.Id} 单词造文，低配简易译法，逐句附译文，单元单词加粗，直接抄写）"),
        };
        for (var a = 0; a < articles.Count; a++)
        {
            lines.Add(Plain($"## 第{Ordinal(a + 1)}篇 {articles[a].Title}"));
            var sentences = articles[a].Sentences;
            for (var i = 0; i < sentences.Count; i++)
            {
                lines.Add(Plain($"{i + 1}. {sentences[i].Zh}"));
                lines.Add(WordMatcher.Highlight(sentences[i].En, unit.Words));
                lines.Add(Plain(string.Empty));
                lines.Add(Plain(string.Empty));
            }
        }

        lines.Add(Plain($"### {unit.Id} 单词（{unit.Words.Count} 个，已全部用上）"));
        lines.Add(Plain(string.Join(", ", unit.Words)));
        return lines;
    }

    private static IReadOnlyList<Segment> Plain(string text) => [new Segment(text)];

    private static string Ordinal(int number)
        => number is >= 1 and <= 10 ? "一二三四五六七八九十"[number - 1].ToString() : number.ToString();
}

/// <summary>
/// 判定英文里用到了哪些单元单词，查缺与加粗共用这一个判据。
/// </summary>
/// <remarks>
/// 只认原形与规则词尾（-s/-es/-d/-ed/-ing/-r/-er/-st/-est，含去 e、y 变 i、双写末辅音）。
/// 一个词本身就是单元里的另一个词时只算那个词：单元同时有 act 与 acting，写 acting 不算 act 用过。
/// 带空格的词组按整段匹配。不规则变形认不出——提示词里要求模型别用。
/// </remarks>
internal static partial class WordMatcher
{
    [GeneratedRegex(@"[A-Za-z]+(?:-[A-Za-z]+)*")]
    private static partial Regex Token();

    private readonly record struct Hit(string Word, int Start, int End);

    public static IReadOnlyList<string> Missing(IReadOnlyList<string> words, IReadOnlyList<Article> articles)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sentence in articles.SelectMany(article => article.Sentences))
        {
            foreach (var hit in Hits(sentence.En, words))
                used.Add(hit.Word);
        }

        return words.Where(word => !used.Contains(word)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<Segment> Highlight(string text, IReadOnlyList<string> words)
    {
        var segments = new List<Segment>();
        var position = 0;
        foreach (var hit in Hits(text, words).OrderBy(hit => hit.Start))
        {
            if (hit.End <= position)
                continue;
            var start = Math.Max(hit.Start, position);
            if (start > position)
                segments.Add(new Segment(text[position..start]));
            segments.Add(new Segment(text[start..hit.End], Bold: true));
            position = hit.End;
        }

        if (position < text.Length)
            segments.Add(new Segment(text[position..]));
        return segments;
    }

    private static IEnumerable<Hit> Hits(string text, IReadOnlyList<string> words)
    {
        var singles = words.Where(word => !word.Contains(' ')).ToList();
        var exact = new HashSet<string>(singles, StringComparer.OrdinalIgnoreCase);
        var forms = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in singles)
        {
            foreach (var form in Forms(word.ToLowerInvariant()))
            {
                if (!forms.TryGetValue(form, out var owners))
                    forms[form] = owners = [];
                if (!owners.Contains(word, StringComparer.OrdinalIgnoreCase))
                    owners.Add(word);
            }
        }

        foreach (Match token in Token().Matches(text))
        {
            if (exact.TryGetValue(token.Value, out var word))
                yield return new Hit(word, token.Index, token.Index + token.Length);
            else if (forms.TryGetValue(token.Value, out var owners))
            {
                foreach (var owner in owners)
                    yield return new Hit(owner, token.Index, token.Index + token.Length);
            }
        }

        foreach (var phrase in words.Where(word => word.Contains(' ')))
        {
            var pattern = @"\b" + string.Join(@"\s+", phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape)) + @"\b";
            foreach (Match match in Regex.Matches(text, pattern, RegexOptions.IgnoreCase))
                yield return new Hit(phrase, match.Index, match.Index + match.Length);
        }
    }

    private static IEnumerable<string> Forms(string word)
    {
        foreach (var ending in new[] { "s", "es", "d", "ed", "ing", "r", "er", "st", "est" })
            yield return word + ending;

        if (word.Length > 2 && word[^1] == 'e')
            yield return word[..^1] + "ing";

        if (word.Length > 2 && word[^1] == 'y' && !IsVowel(word[^2]))
        {
            foreach (var ending in new[] { "ies", "ied", "ier", "iest" })
                yield return word[..^1] + ending;
        }

        if (word.Length >= 3 && !IsVowel(word[^1]) && IsVowel(word[^2]) && !IsVowel(word[^3]) && "wxy".IndexOf(word[^1]) < 0)
        {
            foreach (var ending in new[] { "ed", "ing", "er", "est" })
                yield return word + word[^1] + ending;
        }
    }

    private static bool IsVowel(char c) => "aeiou".IndexOf(c) >= 0;
}

/// <summary>抄写文档：以 a2 里的「四级翻译短篇抄写n.docx」为模板，只换正文段落。</summary>
internal static partial class CopyDocument
{
    [GeneratedRegex(@"^四级翻译短篇抄写(\d+)(?:-(U\d+-\d+)(?=-|$))?")]
    private static partial Regex CopyName();

    /// <summary>下一个编号，文件名带小单元，如「四级翻译短篇抄写13-U1-1.docx」。</summary>
    public static string NextPath(string directory, string unitId)
    {
        var next = Scan(directory).Select(item => item.number).DefaultIfEmpty(0).Max() + 1;
        return Path.Combine(directory, $"四级翻译短篇抄写{next}-{unitId}.docx");
    }

    /// <summary>每个小单元最近一次生成的文件名。</summary>
    public static IReadOnlyDictionary<string, string> Existing(string directory)
        => Scan(directory)
            .Where(item => item.unit is not null)
            .GroupBy(item => item.unit!)
            .ToDictionary(group => group.Key, group => group.MaxBy(item => item.number).file);

    private static IEnumerable<(int number, string? unit, string file)> Scan(string directory)
        => Directory.EnumerateFiles(directory, "四级翻译短篇抄写*.docx")
            .Select(Path.GetFileName)
            .Select(file => (file: file!, match: CopyName().Match(Path.GetFileNameWithoutExtension(file!))))
            .Where(item => item.match.Success)
            .Select(item => (
                int.Parse(item.match.Groups[1].Value),
                item.match.Groups[2].Success ? item.match.Groups[2].Value : null,
                item.file));

    public static void Write(string templatePath, string outputPath, IReadOnlyList<IReadOnlyList<Segment>> lines)
    {
        File.Copy(templatePath, outputPath, overwrite: false);
        try
        {
            using var zip = ZipFile.Open(outputPath, ZipArchiveMode.Update);
            var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("模板里没有 word/document.xml");
            XDocument document;
            using (var input = entry.Open())
                document = XDocument.Load(input);

            var w = DocxText.W;
            var body = document.Root!.Element(w + "body") ?? throw new InvalidDataException("模板没有 body");
            // 段落与字体格式照抄模板第一个有字的段落，页面设置 sectPr 原样保留。
            var sample = body.Elements(w + "p").First(p => p.Descendants(w + "t").Any());
            var paragraphProperties = sample.Element(w + "pPr");
            var runProperties = sample.Descendants(w + "r").First().Element(w + "rPr");
            var section = body.Element(w + "sectPr");

            body.RemoveNodes();
            foreach (var line in lines)
            {
                var paragraph = new XElement(w + "p",
                    paragraphProperties is null ? null : new XElement(paragraphProperties));
                foreach (var segment in line.Where(segment => segment.Text.Length > 0))
                {
                    paragraph.Add(new XElement(w + "r",
                        RunProperties(runProperties, segment.Bold),
                        new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), segment.Text)));
                }

                body.Add(paragraph);
            }

            if (section is not null)
                body.Add(section);

            entry.Delete();
            using var output = zip.CreateEntry("word/document.xml").Open();
            using var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
            document.Save(writer);
        }
        catch
        {
            File.Delete(outputPath);
            throw;
        }
    }

    /// <summary>加粗段在模板字体格式上补 b/bCs；按 OOXML 顺序放在 rStyle/rFonts 之后。</summary>
    private static XElement? RunProperties(XElement? template, bool bold)
    {
        if (!bold)
            return template is null ? null : new XElement(template);

        var w = DocxText.W;
        var properties = template is null ? new XElement(w + "rPr") : new XElement(template);
        var marks = new[] { new XElement(w + "b"), new XElement(w + "bCs") };
        var anchor = properties.Element(w + "rFonts") ?? properties.Element(w + "rStyle");
        if (anchor is null)
            properties.AddFirst(marks);
        else
            anchor.AddAfterSelf(marks);
        return properties;
    }
}
