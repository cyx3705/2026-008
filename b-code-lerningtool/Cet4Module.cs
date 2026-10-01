using System.Diagnostics;
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
    private const string Hidden = "界面内部协议，对模型无意义";
    /// <summary>首稿之后最多再让 AI 改几轮。</summary>
    private const int MaxFixRounds = 3;
    private static int _running;

    private static void Register(ICommandRegistrar registry, ICommandBus bus)
    {
        registry.Register(Internal("cet4.ui.describe", "返回四级抄写页的页面描述。",
            _ => Json(Cet4Page.Describe())));

        registry.Register(Internal("cet4.ui.actions", "返回四级抄写页的动作声明。",
            _ => Json(Cet4Page.Actions())));

        registry.Register(Internal("cet4.ui.data", "返回单词书按关的表格行。",
            _ => Json(Cet4Page.Rows(WordBook.Levels(Paths.Words), CopyDocument.Existing(Paths.Output)))));

        registry.Register(new CommandDescriptor
        {
            Name = "cet4.copy.generate",
            Domain = Domain,
            CommandClass = "copy",
            Summary = "把单词书的一关（如 U1-L1 = U1-1~U1-4）逐个小单元交给 AI 造文，合成 a2-四级翻译 里的一份短篇抄写。",
            Example = "cet4.copy.generate level=U1-L1",
            Level = CommandLevel.Run,
            Parameters =
            [
                new ParameterSpec
                {
                    Name = "level",
                    Description = "关编号：书号加关，如 U1-L1（也接受 1-L1、U1L1）。",
                    Required = true,
                    Position = 0,
                },
            ],
            Handler = context => GenerateAsync(bus, context),
        });

        registry.Register(new CommandDescriptor
        {
            Name = "cet4.copy.open",
            Domain = Domain,
            CommandClass = "copy",
            Summary = "用系统默认程序打开 a2-四级翻译 里的一份短篇抄写。",
            Example = "cet4.copy.open file=四级翻译短篇抄写13-U1-1.docx",
            Level = CommandLevel.Run,
            Parameters =
            [
                new ParameterSpec
                {
                    Name = "file",
                    Description = "抄写文件名（只认 a2-四级翻译 里的「四级翻译短篇抄写*.docx」）。",
                    Required = true,
                    Position = 0,
                },
            ],
            Handler = CommandDescriptor.Sync(Open),
        });
    }

    private static CommandResult Open(CommandContext context)
    {
        var file = context.RequireString("file");
        var path = CopyDocument.Resolve(Paths.Output, file);
        if (path is null)
            return CommandResult.Fail($"a2-四级翻译 里没有抄写文件 {file}");

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        return CommandResult.Ok($"已打开 {Path.GetFileName(path)}", path);
    }

    private static async Task<CommandResult> GenerateAsync(ICommandBus bus, CommandContext context)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return CommandResult.Fail("上一份还在生成，等它结束再点。");

        try
        {
            var id = context.RequireString("level");
            var level = WordBook.Levels(Paths.Words).FirstOrDefault(l => l.Matches(id));
            if (level is null)
                return CommandResult.Fail($"单词书里没有关 {id}");

            // 同一关可以反复生成，每次都是新编号的新文件；把旧标题交给 AI，让它换话题。
            // 0.4.0 以前按小单元生成的旧文件也算进去。
            var existing = CopyDocument.Existing(Paths.Output);
            var previous = existing.TryGetValue(level.Id, out var files) ? files : [];
            var older = level.Units.SelectMany(u => existing.TryGetValue(u.Id, out var f) ? f : []);
            var usedTitles = CopyDocument.Titles(Paths.Output, older.Concat(previous)).ToList();
            context.Progress?.Report(
                $"{level.Id}（{level.Range}）：{level.Units.Count} 个小单元 {level.Words.Count} 个单词，逐个小单元交给 AI 造文"
                + (previous.Count == 0 ? string.Empty : $"，第 {previous.Count + 1} 次生成，避开已写过的 {usedTitles.Count} 个话题"));

            // 每个小单元单独调用，调用轻、查缺准；全部合格后合成这一关的一份文档。
            var articles = new List<Article>();
            foreach (var unit in level.Units)
            {
                var (written, error) = await WriteUnitAsync(bus, context, unit, usedTitles).ConfigureAwait(false);
                if (written is null)
                    return CommandResult.Fail($"{error}，{level.Id} 没有生成文件。");
                articles.AddRange(written);
                usedTitles.AddRange(written.Select(a => a.Title));
            }

            var path = CopyDocument.NextPath(Paths.Output, level.Id);
            try
            {
                CopyDocument.Write(Paths.Template, path, ArticleWriter.Lines(level, articles));
            }
            catch (IOException ex)
            {
                return CommandResult.Fail($"写入抄写文档失败：{ex.Message}");
            }

            await RefreshPageAsync(bus).ConfigureAwait(false);

            return CommandResult.Ok(
                $"已生成 {Path.GetFileName(path)}：{articles.Count} 篇 {articles.Sum(a => a.Sentences.Count)} 句"
                + $"（{string.Join("、", articles.Select(a => "《" + a.Title + "》"))}），"
                + $"{level.Words.Count} 个单词全部用上并加粗",
                path);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>
    /// 一个小单元：写 → 查缺 → 把缺的词交回去改。单词不许漏：改到 MaxFixRounds 轮仍不齐就算失败。
    /// </summary>
    private static async Task<(IReadOnlyList<Article>? Articles, string? Error)> WriteUnitAsync(
        ICommandBus bus, CommandContext context, WordUnit unit, IReadOnlyList<string> usedTitles)
    {
        context.Progress?.Report($"{unit.Id}：{unit.Words.Count} 个单词，要求全部用上");
        var messages = ArticleWriter.StartMessages(unit, usedTitles);
        for (var round = 0; ; round++)
        {
            var reply = await bus.ExecuteAsync(ArticleWriter.SendCommand(messages), "", context.Cancellation)
                .ConfigureAwait(false);
            if (!reply.Success)
                return (null, $"{unit.Id} AI 调用失败：{reply.Message}");

            var content = ReadContent(reply);
            string problem;
            string feedback;
            try
            {
                var articles = ArticleWriter.Parse(content);
                var missing = WordMatcher.Missing(unit.Words, articles);
                if (missing.Count == 0)
                    return (articles, null);
                problem = $"还差 {missing.Count} 个单词：{string.Join(", ", missing)}";
                feedback = ArticleWriter.MissingWordsPrompt(missing);
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                problem = $"不是约定格式：{ex.Message}";
                feedback = ArticleWriter.FormatPrompt(ex.Message);
            }

            if (round == MaxFixRounds)
                return (null, $"{unit.Id} 改了 {MaxFixRounds} 轮仍不合格（{problem}）");

            context.Progress?.Report($"{unit.Id} 第 {round + 1} 稿{problem}，让 AI 修改");
            messages.Add(ArticleWriter.Message("assistant", content));
            messages.Add(ArticleWriter.Message("user", feedback));
        }
    }

    /// <summary>让「已生成」「次数」立刻变。页面没开着或没装 Aurora 时刷不到，不影响生成结果。</summary>
    private static async Task RefreshPageAsync(ICommandBus bus)
    {
        try
        {
            await bus.ExecuteAsync($"aurora.ui.refreshdata page={Cet4Page.PageId}", "").ConfigureAwait(false);
        }
        catch (Exception)
        {
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
