using System.Text.Json;
using HistoryVulcan.Core.Commands;
using HistoryVulcan.Core.Modules;

namespace Cet4LearningTool;

/// <summary>模块入口：登记页面协议三条指令与一条生成指令。</summary>
public sealed class Cet4Module : IModuleContextAware
{
    public void Attach(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.RegisterCommands(registry => Register(registry, context.Bus));
    }

    /// <summary>
    /// 数据就在本仓里，模块却装在 AppData 运行区，所以这里只能写绝对路径。
    /// </summary>
    internal static class Paths
    {
        public const string Root = @"C:\OneHistory\HistoryClio\2026-008-英语四级学习";
        public static readonly string Words = Path.Combine(Root, "a1-四级单词");
        public static readonly string Output = Path.Combine(Root, "a2-四级翻译");
        public static readonly string Template = Path.Combine(Output, "四级翻译短篇抄写n.docx");
    }

    private const string Domain = "cet4";
    private const string Source = "module:Cet4LearningTool";
    private const string Hidden = "界面内部协议，对模型无意义";
    /// <summary>首稿之后最多再让 AI 改几轮。</summary>
    private const int MaxFixRounds = 3;
    private static int _running;

    private static void Register(CommandRegistry registry, CommandBus bus)
    {
        registry.Register(Internal("cet4.ui.describe", "返回四级抄写页的页面描述。",
            _ => Json(Cet4Page.Describe())), Source);

        registry.Register(Internal("cet4.ui.actions", "返回四级抄写页的动作声明。",
            _ => Json(Cet4Page.Actions())), Source);

        registry.Register(Internal("cet4.ui.data", "返回单词书小单元表格行。",
            _ => Json(Cet4Page.Rows(WordBook.Load(Paths.Words), CopyDocument.Existing(Paths.Output)))), Source);

        registry.Register(new CommandDescriptor
        {
            Name = "cet4.copy.generate",
            Domain = Domain,
            CommandClass = "copy",
            Summary = "把单词书的一个小单元交给 AI 造一篇短文，写成 a2-四级翻译 里的一份短篇抄写。",
            Example = "cet4.copy.generate unit=U1-1",
            Level = CommandLevel.Run,
            Parameters =
            [
                new ParameterSpec
                {
                    Name = "unit",
                    Description = "小单元编号，如 U1-1（也接受 1-1）。",
                    Required = true,
                    Position = 0,
                },
            ],
            Handler = context => GenerateAsync(bus, context),
        }, Source);
    }

    private static async Task<CommandResult> GenerateAsync(CommandBus bus, CommandContext context)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return CommandResult.Fail("上一篇还在生成，等它结束再点。");

        try
        {
            var id = context.RequireString("unit");
            var unit = WordBook.Load(Paths.Words).FirstOrDefault(u => u.Matches(id));
            if (unit is null)
                return CommandResult.Fail($"单词书里没有小单元 {id}");

            context.Progress?.Report($"{unit.Id}：{unit.Words.Count} 个单词，交给 AI 造文，要求全部用上");

            // 写 → 查缺 → 把缺的词交回去改。单词不许漏：改到 MaxFixRounds 轮仍不齐就不出文件。
            var messages = ArticleWriter.StartMessages(unit);
            IReadOnlyList<Article> articles;
            for (var round = 0; ; round++)
            {
                var reply = await bus.ExecuteAsync(ArticleWriter.SendCommand(messages), Source, context.Cancellation)
                    .ConfigureAwait(false);
                if (!reply.Success)
                    return CommandResult.Fail($"AI 调用失败：{reply.Message}");

                var content = ReadContent(reply);
                string problem;
                string feedback;
                try
                {
                    articles = ArticleWriter.Parse(content);
                    var missing = WordMatcher.Missing(unit.Words, articles);
                    if (missing.Count == 0)
                        break;
                    problem = $"还差 {missing.Count} 个单词：{string.Join(", ", missing)}";
                    feedback = ArticleWriter.MissingWordsPrompt(missing);
                }
                catch (Exception ex) when (ex is JsonException or FormatException)
                {
                    problem = $"不是约定格式：{ex.Message}";
                    feedback = ArticleWriter.FormatPrompt(ex.Message);
                }

                if (round == MaxFixRounds)
                    return CommandResult.Fail($"{unit.Id} 改了 {MaxFixRounds} 轮仍不合格，没有生成文件。{problem}");

                context.Progress?.Report($"{unit.Id} 第 {round + 1} 稿{problem}，让 AI 修改");
                messages.Add(ArticleWriter.Message("assistant", content));
                messages.Add(ArticleWriter.Message("user", feedback));
            }

            var path = CopyDocument.NextPath(Paths.Output, unit.Id);
            try
            {
                CopyDocument.Write(Paths.Template, path, ArticleWriter.Lines(unit, articles));
            }
            catch (IOException ex)
            {
                return CommandResult.Fail($"写入抄写文档失败：{ex.Message}");
            }

            return CommandResult.Ok(
                $"已生成 {Path.GetFileName(path)}：{articles.Count} 篇 {articles.Sum(a => a.Sentences.Count)} 句"
                + $"（{string.Join("、", articles.Select(a => "《" + a.Title + "》"))}），"
                + $"{unit.Words.Count} 个单词全部用上并加粗",
                path);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>Apollo 的结构化载荷是 JSON 文本，答复正文在 content。</summary>
    private static string ReadContent(CommandResult reply)
    {
        var data = reply.Data switch
        {
            string text => text,
            null => throw new FormatException("Apollo 没有返回结构化载荷"),
            var other => JsonSerializer.Serialize(other),
        };
        using var document = JsonDocument.Parse(data);
        return document.RootElement.TryGetProperty("content", out var content)
            ? content.GetString() ?? string.Empty
            : throw new FormatException("Apollo 载荷里没有 content");
    }

    private static CommandResult Json(string json) => CommandResult.Ok(json, json);

    private static CommandDescriptor Internal(string name, string summary, Func<CommandContext, CommandResult> handler)
        => new()
        {
            Name = name,
            Domain = Domain,
            CommandClass = "ui",
            Summary = summary,
            Readonly = true,
            HiddenReason = Hidden,
            Handler = CommandDescriptor.Sync(handler),
        };
}
