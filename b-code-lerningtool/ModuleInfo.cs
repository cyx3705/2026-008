using System.Reflection;
using BaseVariable;

namespace Cet4LearningTool;

/// <summary>模块身份。宿主按全名识别 ModuleInfoBase 子类，并比对 manifest 版本。</summary>
public sealed class ModuleInfo : ModuleInfoBase
{
    /// <summary>显式给出，免得跟着程序集名漂移。注意页面 owner 另有规则，见 Cet4Page.Owner。</summary>
    public override string ModuleName => "Cet4LearningTool";

    public override string Description => "四级单词造文：选一个小单元交给 AI 生成短篇抄写";

    public override string Author => "OneHistory";

    /// <summary>取程序集版本（csproj 的 Version），与 manifest 不一致时宿主会跳过本模块。</summary>
    public override string Version { get; } =
        typeof(ModuleInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    public override Type? MainClassType => null;
}
