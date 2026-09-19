using System.Text.Encodings.Web;
using System.Text.Json;

namespace Cet4LearningTool;

/// <summary>Aurora 页面协议 V1：一页，上面一块面板，下面一张小单元表。</summary>
internal static class Cet4Page
{
    /// <summary>
    /// 不是模块名：Aurora 按指令域反推 owner（cet4 → HistoryCet4），对不上整页被拒收。
    /// 场景 id 也随之叫 HistoryCet4。
    /// </summary>
    private const string Owner = "HistoryCet4";
    private const string PageId = "copy";
    private const string Channel = "cet4.unit";

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
                                        new { kind = "textbox", id = "unit", label = "小单元", follows = Channel + ".unit", flex = true },
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
                                new { key = "unit", title = "小单元", width = "70" },
                                new { key = "level", title = "关", width = "40" },
                                new { key = "count", title = "词数", width = "40" },
                                new { key = "words", title = "单词", width = "*" },
                                new { key = "output", title = "已生成", width = "170" },
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
                args = new { unit = "{unit}" },
                summary = "把选中的小单元交给 AI 造一篇短文，写进 a2-四级翻译。",
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

    public static string Rows(IReadOnlyList<WordUnit> units, IReadOnlyDictionary<string, string> existing)
        => JsonSerializer.Serialize(
            units.Select(unit => new Dictionary<string, string>
            {
                ["unit"] = unit.Id,
                ["level"] = unit.Level,
                ["count"] = unit.Words.Count.ToString(),
                ["words"] = string.Join(", ", unit.Words),
                ["output"] = existing.TryGetValue(unit.Id, out var file) ? file : string.Empty,
            }),
            Options);
}
