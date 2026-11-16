using Orchestrator.Cli;
using Orchestrator.Store;

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
}
