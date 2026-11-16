using System.Diagnostics;

namespace Orchestrator.Store;

public sealed record ShellResult(int Exit, string Output, string Error)
{
    public bool Ok => Exit == 0;
    public string Text => Output.Trim();
}

public sealed class ShellException(string message) : Exception(message);

public sealed class Shell(string cwd)
{
    public string Cwd => cwd;

    public ShellResult Run(string program, params string[] arguments)
    {
        var info = new ProcessStartInfo(program)
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new ShellException($"cannot start {program}");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return new ShellResult(process.ExitCode, output, error.Result);
    }

    public string Must(string program, params string[] arguments)
    {
        var result = Run(program, arguments);
        if (!result.Ok) throw new ShellException($"{program} {string.Join(' ', arguments)} failed: {(result.Error + result.Output).Trim()}");
        return result.Text;
    }

    public ShellResult Git(params string[] arguments) => Run("git", arguments);

    public string MustGit(params string[] arguments) => Must("git", arguments);

    public string CurrentBranch() => MustGit("rev-parse", "--abbrev-ref", "HEAD");

    public bool BranchExists(string branch) => Git("rev-parse", "--verify", "--quiet", $"refs/heads/{branch}").Ok;

    public bool HasGitHubRemote() => Git("remote", "get-url", "origin") is { Ok: true } r && r.Text.Contains("github.com", StringComparison.OrdinalIgnoreCase);

    public void Checkout(string branch) => MustGit("checkout", "--quiet", branch);

    public void Push(string branch) => MustGit("push", "--quiet", "--force-with-lease", "-u", "origin", branch);
}
