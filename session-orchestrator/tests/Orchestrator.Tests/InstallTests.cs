using Orchestrator.Cli;
using Orchestrator.Store;
using Orchestrator.Store.Local;

namespace Orchestrator.Tests;

public sealed class InstallTests
{
    static Shell NewHome()
    {
        var shell = new Shell(Directory.CreateTempSubdirectory("orch-home-").FullName);
        shell.MustGit("init", "--quiet", "-b", "main");
        shell.MustGit("config", "user.email", "test@example.com");
        shell.MustGit("config", "user.name", "test");
        Commit(shell, "src/Orchestrator.Cli/Program.cs", "one");
        return shell;
    }

    static string Commit(Shell shell, string file, string text)
    {
        var path = Path.Combine(shell.Cwd, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        shell.MustGit("add", file);
        shell.MustGit("commit", "--quiet", "-m", $"write {file}");
        return shell.Must("git", "rev-parse", "HEAD");
    }

    [Test]
    public void A_build_of_the_newest_source_passes()
    {
        var home = NewHome();
        var built = home.Must("git", "rev-parse", "HEAD");
        Commit(home, "docs/plan.md", "a change outside the source");
        Assert.That(Install.Check(home, built).Ok, Is.True);
    }

    [Test]
    public void A_newer_change_in_the_source_fails_and_counts_the_commits()
    {
        var home = NewHome();
        var built = home.Must("git", "rev-parse", "HEAD");
        Commit(home, "src/Orchestrator.Core/Names.cs", "two");
        Commit(home, "src/Orchestrator.Store/Files.cs", "three");
        var (ok, detail) = Install.Check(home, built);
        Assert.That(ok, Is.False);
        Assert.That(detail, Does.Contain("2 newer commits"));
    }

    [Test]
    public void A_build_with_no_commit_or_an_unknown_commit_fails()
    {
        var home = NewHome();
        Assert.That(Install.Check(home, null).Ok, Is.False);
        Assert.That(Install.Check(home, new string('a', 40)).Ok, Is.False);
    }

    [Test]
    public void The_build_records_its_source_commit() => Assert.That(Install.Commit, Has.Length.EqualTo(40));

    static string App(string commit)
    {
        var root = Directory.CreateTempSubdirectory("orch-app-").FullName;
        Files.WriteJson(Path.Combine(root, "install.json"), new SupervisorHost.InstallRecord(commit, "C:/store", 7861, "2026-11-17T00:00:00Z"));
        return root;
    }

    [Test]
    public void No_installed_app_passes()
    {
        var (ok, detail) = Install.CheckApp(NewHome(), Directory.CreateTempSubdirectory("orch-app-").FullName);
        Assert.That(ok, Is.True);
        Assert.That(detail, Does.StartWith("not installed"));
    }

    [Test]
    public void An_installed_app_fails_after_a_newer_change_anywhere_in_src()
    {
        var home = NewHome();
        var app = App(home.Must("git", "rev-parse", "HEAD"));
        Assert.That(Install.CheckApp(home, app).Ok, Is.True);
        Commit(home, "src/Orchestrator.Tray/Program.cs", "tray");
        var (ok, detail) = Install.CheckApp(home, app);
        Assert.That(ok, Is.False);
        Assert.That(detail, Does.Contain("1 newer commits change src/"));
    }

    [Test]
    public void The_supervisor_command_takes_the_first_published_copy()
    {
        var empty = Directory.CreateTempSubdirectory("orch-sup-").FullName;
        var copy = Directory.CreateTempSubdirectory("orch-sup-").FullName;
        File.WriteAllText(Path.Combine(copy, SupervisorHost.Dll), "");
        var (file, args) = SupervisorHost.Command(empty, copy);
        Assert.That(file, Is.EqualTo("dotnet"));
        Assert.That(args, Is.EqualTo(new[] { Path.Combine(copy, SupervisorHost.Dll) }));
    }

    [Test]
    public void A_port_with_no_record_has_no_supervisor_record() => Assert.That(SupervisorHost.Record(1), Is.Null);
}
