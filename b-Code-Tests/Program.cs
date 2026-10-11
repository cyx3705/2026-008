using System.IO.Compression;
using System.Text.Json;
using Cet4LearningTool;
using HistoryVulcan.Core.Commands;
using HistoryVulcan.Core.Modules;

await Checks.LearningRootAndFailure();
await Checks.PageAndLegacyCompatibility();
Checks.PackageIdentity();
Console.WriteLine("PASS: LearningRootAndFailure, PageAndLegacyCompatibility, PackageIdentity");

static class Checks
{
    public static async Task LearningRootAndFailure()
    {
        var bus = new FakeBus();
        var paths = await LearningPaths.ResolveAsync(bus, default);
        Require(paths.Root == Path.Combine(bus.Library, "2026-008-英语四级学习"), "Must use host library, not machine path");
        Require(paths.Template == Path.Combine(paths.Root, "a2-四级翻译", "四级翻译短篇抄写n.docx"), "Template stays in study project");
        bus.Reply = CommandResult.Fail("offline");
        await Reject(() => LearningPaths.ResolveAsync(bus, default));
        bus.Reply = CommandResult.Ok("", JsonSerializer.SerializeToElement(new { libraryRoot = "relative" }));
        await Reject(() => LearningPaths.ResolveAsync(bus, default));
        bus.Reply = CommandResult.Ok("", JsonSerializer.SerializeToElement(new { other = "missing" }));
        await Reject(() => LearningPaths.ResolveAsync(bus, default));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await LearningPaths.ResolveAsync(bus, cancelled.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { }
    }

    public static async Task PageAndLegacyCompatibility()
    {
        // Entirely synthetic files in an isolated temporary project; never touches real study documents.
        var library = Path.Combine(Path.GetTempPath(), "cet4-contract-" + Guid.NewGuid().ToString("N"));
        var words = Path.Combine(library, "2026-008-英语四级学习", "a1-四级单词");
        Directory.CreateDirectory(words);
        var output = Path.Combine(library, "2026-008-英语四级学习", "a2-四级翻译");
        Directory.CreateDirectory(output);
        using (var zip = ZipFile.Open(Path.Combine(words, "U1.docx"), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
            writer.Write("""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>L1 1-1</w:t></w:r></w:p><w:p><w:r><w:t>apple</w:t></w:r></w:p><w:p><w:r><w:t>book</w:t></w:r></w:p></w:body></w:document>""");
        var bus = new FakeBus(library);
        var host = new FakeHost(bus);
        new Cet4Module().Attach(host);
        Require(bus.Calls == 0, "Attach must only register, not call other modules");
        Require(host.Commands.Count == 10, "Canonical commands and all five legacy aliases required");
        foreach (var suffix in new[] { "ui.describe", "ui.actions", "ui.data" })
        {
            var canonical = host.Commands["cet4learningtool." + suffix];
            var legacy = host.Commands["cet4." + suffix];
            Require(canonical.Readonly && legacy.Readonly && canonical.HiddenReason != null && legacy.HiddenReason != null, "UI commands hidden and readonly");
            var result = await canonical.Handler(Context(canonical));
            var old = await legacy.Handler(Context(legacy));
            Require(result.Success && result.Message == (string?)result.Data && result.Message == old.Message, "UI Data/Message and aliases must agree");
            using var json = JsonDocument.Parse(result.Message);
            if (suffix == "ui.data")
            {
                Require(json.RootElement.GetArrayLength() == 1, "Must read fixture from host library");
                Require(json.RootElement[0].GetProperty("level").GetString() == "U1-L1", "Level grouping preserved");
                Require(json.RootElement[0].GetProperty("count").GetString() == "2", "Words preserved");
            }
            else
            {
                Require(json.RootElement.GetProperty("owner").GetString() == "Cet4LearningTool", "Owner is actual module identity");
                if (suffix == "ui.describe")
                    Require(json.RootElement.GetProperty("pages")[0].GetProperty("scene").GetString() == "Cet4LearningTool", "Scene matches owner");
            }
        }
        foreach (var name in new[] { "cet4.copy.generate", "cet4.copy.open", "cet4learningtool.copy.generate", "cet4learningtool.copy.open" })
            Require(!host.Commands[name].Readonly, "Business actions must remain writes");
        Require(Directory.GetFiles(output).Length == 0, "Readonly data must not write learning documents");
        // Keep isolated fixture under OS temp; no recursive deletion in test runner.
    }

    public static void PackageIdentity()
    {
        var root = Directory.GetCurrentDirectory();
        using var project = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "project.manifest.json")));
        using var module = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "b-code-lerningtool", "module.manifest.json")));
        var identity = new ModuleInfo();
        Require(project.RootElement.GetProperty("project").GetProperty("name").GetString() == identity.ModuleName, "Project identity");
        Require(module.RootElement.GetProperty("name").GetString() == identity.ModuleName, "Package identity");
        Require(project.RootElement.GetProperty("project").GetProperty("version").GetString() == identity.Version &&
            module.RootElement.GetProperty("version").GetString() == identity.Version, "Three versions agree");
        var files = project.RootElement.GetProperty("publish").GetProperty("package").GetProperty("files").EnumerateArray().Select(x => x.GetString()).ToArray();
        Require(files.SequenceEqual(new[] { "Cet4LearningTool.dll", "Cet4LearningTool.xml", "module.manifest.json" }), "Package excludes study documents and host DLL");
    }

    private static CommandContext Context(CommandDescriptor command) => new(command, new Dictionary<string, string>(), "test", null, default);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Reject(Func<Task<LearningPaths>> action)
    {
        try { await action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Invalid library must fail without guessing a path");
    }
}

sealed class FakeHost(ICommandBus bus) : IModuleContext, ICommandRegistrar
{
    public Dictionary<string, CommandDescriptor> Commands { get; } = new();
    public ICommandBus Bus => bus;
    public void RegisterCommands(Action<ICommandRegistrar> configure) => configure(this);
    public void Register(CommandDescriptor descriptor) => Commands.Add(descriptor.Name, descriptor);
}

sealed class FakeBus : ICommandBus
{
    public string Library { get; }
    public int Calls { get; private set; }
    public CommandResult Reply { get; set; }
    public FakeBus(string? library = null)
    {
        Library = library ?? Path.Combine(Path.GetTempPath(), "cet4-library");
        Reply = CommandResult.Ok("", JsonSerializer.SerializeToElement(new { libraryRoot = Library }));
    }
    public Task<CommandResult> InvokeAsync(string text, string source, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (text != "vulcan.host.info") throw new Exception("Unexpected cross-module call " + text);
        Calls++;
        return Task.FromResult(Reply);
    }
    public Task<CommandResult> ExecuteAsync(string text, string source, CancellationToken cancellation = default) => throw new Exception("Tests must not invoke AI or UI");
    public bool RequestConfirmation(string prompt) => throw new Exception("Unexpected confirmation");
}