using System.Text.Json;
using HistoryVulcan.Core.Commands;

namespace Cet4LearningTool;

/// <summary>学习资料仍存于项目主树；运行包和 F 盘工作副本都不是用户数据目录。</summary>
internal sealed record LearningPaths(string Root)
{
    public string Words => Path.Combine(Root, "a1-四级单词");
    public string Output => Path.Combine(Root, "a2-四级翻译");
    public string Template => Path.Combine(Output, "四级翻译短篇抄写n.docx");

    public static async Task<LearningPaths> ResolveAsync(ICommandBus bus, CancellationToken cancellation)
    {
        var reply = await bus.InvokeAsync("vulcan.host.info", "", cancellation).ConfigureAwait(false);
        if (!reply.Success)
            throw new InvalidOperationException("无法读取学习资料所在的项目库：" + reply.Message);
        if (reply.Data is not JsonElement data ||
            !data.TryGetProperty("libraryRoot", out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()) ||
            !Path.IsPathFullyQualified(value.GetString()!))
            throw new InvalidOperationException("宿主没有提供有效的 libraryRoot。");
        return new LearningPaths(Path.Combine(value.GetString()!, "2026-008-英语四级学习"));
    }
}