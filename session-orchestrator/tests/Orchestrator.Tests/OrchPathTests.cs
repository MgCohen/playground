using Orchestrator.Providers;
using Orchestrator.Providers.ClaudeCode;
using Orchestrator.Store;

namespace Orchestrator.Tests;

/// <summary>C3-F5: the supervisor puts its own orch first on the PATH of each turn.</summary>
public sealed class OrchPathTests
{
    [Test]
    public void A_turn_gets_the_orch_folder_first_on_the_path()
    {
        var environment = ClaudeCodeProvider.TurnEnvironment(new ProviderOptions("C:/store", "C:/home") { OrchDir = "C:/orch" }, "C:/a" + Path.PathSeparator + "C:/b");
        Assert.That(environment["PATH"], Is.EqualTo("C:/orch" + Path.PathSeparator + "C:/a" + Path.PathSeparator + "C:/b"));
        Assert.That(environment["ORCH_STORE"], Is.EqualTo("C:/store"));
    }

    [Test]
    public void With_no_orch_folder_the_turn_keeps_the_path()
    {
        Assert.That(ClaudeCodeProvider.TurnEnvironment(new ProviderOptions("C:/store"), "C:/a").ContainsKey("PATH"), Is.False);
    }

    [Test]
    public void The_orch_folder_is_ORCH_BIN_else_the_program_folder_when_it_holds_orch()
    {
        var folder = Directory.CreateTempSubdirectory("orch-bin-").FullName;
        Assert.That(Home.OrchDir("C:/bin", folder), Is.EqualTo(Path.GetFullPath("C:/bin")));
        Assert.That(Home.OrchDir("", folder), Is.Null, "no orch in the folder");
        File.WriteAllText(Path.Combine(folder, "orch.exe"), "");
        Assert.That(Home.OrchDir("", folder), Is.EqualTo(Path.GetFullPath(folder)));
    }
}
