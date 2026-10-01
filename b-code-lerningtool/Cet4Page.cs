using System.Text.Encodings.Web;
using System.Text.Json;

namespace Cet4LearningTool;

/// <summary>Aurora 页面协议 V1：一页，上面一块面板，下面一张按关的表（一关一行）。</summary>
internal static class Cet4Page
{
    /// <summary>
    /// 不是模块名：Aurora 按指令域反推 owner（cet4 → HistoryCet4），对不上整页被拒收。
    /// 场景 id 也随之叫 HistoryCet4。
    /// </summary>
    private const string Owner = "HistoryCet4";
    public const string PageId = "copy";
    private const string Channel = "cet4.level";

    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Describe() => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        owner = Owner,
        pages = new object[]
        {
            new
            {
                id = PageId,
                title = "四级抄写",
                scene = Owner,
                placement = new { side = "center", visible = true, singleton = true },
                content = new
                {
                    type = "stack",
                    orientation = "vertical",
                    gap = "tight",
                    children = new object[]
                    {
                        new
                        {
                            type = "panel",
                            id = "copy-controls",
                            text = "单词造文",
                            rows = new object[]
                            {
                                new
                                {
                                    mode = "flex",
                                    widgets = new object[]
                                    {
                                        new { kind = "textbox", id = "level", label = "关", follows = Channel + ".level", flex = true },
                                        new { kind = "button", text = "生成抄写", action = "cet4.copy.generate", enabledWhen = new { selected = Channel } },
                                        new { kind = "button", text = "刷新", icon = "refresh-cw", action = "cet4.refresh" },
                                    },
                                },
                            },
                        },
                        new
                        {
                            type = "table",
                            id = "units",
                            channel = Channel,
                            dataSource = new { command = "cet4.ui.data" },
                            columns = new object[]
                            {
                                new { key = "level", title = "关", width = "60" },
                                new { key = "units", title = "小单元", width = "90" },
                                new { key = "count", title = "词数", width = "40" },
                                new { key = "words", title = "单词", width = "*" },
                                new { key = "times", title = "次数", width = "40" },
                                new { key = "output", title = "已生成", width = "170", cellAction = "cet4.copy.open" },
                            },
                        },
                    },
                },
            },
        },
    }, Options);

    public static string Actions() => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        owner = Owner,
        actions = new object[]
        {
            new
            {
                id = "cet4.copy.generate",
                title = "生成抄写",
                command = "cet4.copy.generate",
                args = new { level = "{level}" },
                summary = "把选中的一关逐个小单元交给 AI 造文，合成一份写进 a2-四级翻译。",
                danger = false,
            },
            new
            {
                id = "cet4.copy.open",
                title = "打开抄写",
                command = "cet4.copy.open",
                args = new { file = "{output}" },
                summary = "用 Word 打开这一关最近一次生成的抄写文档。",
                danger = false,
            },
            new
            {
                id = "cet4.refresh",
                title = "刷新",
                command = "aurora.ui.refreshdata",
                args = new { page = PageId },
                summary = "重新读取单词书与已生成的抄写。",
                danger = false,
            },
        },
    }, Options);

    /// <summary>「已生成」显示最近一份，点它打开；更早的几份只计数。</summary>
    public static string Rows(IReadOnlyList<WordLevel> levels, IReadOnlyDictionary<string, IReadOnlyList<string>> existing)
        => JsonSerializer.Serialize(
            levels.Select(level => new Dictionary<string, string>
            {
                ["level"] = level.Id,
                ["units"] = level.Range,
                ["count"] = level.Words.Count.ToString(),
                ["words"] = string.Join(", ", level.Words),
                ["times"] = existing.TryGetValue(level.Id, out var files) ? files.Count.ToString() : string.Empty,
                ["output"] = files is { Count: > 0 } ? files[^1] : string.Empty,
            }),
            Options);
}
