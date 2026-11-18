using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Orchestrator.Tests;

// Runs the node tests of the plugin hooks (plugin/hooks/test), so that dotnet test covers them.
public sealed class PluginHookTests
{
    [Test]
    public void The_node_tests_of_the_plugin_hooks_pass()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "..", ".."));
        var tests = Path.Combine(root, "plugin", "hooks", "test");
        Assume.That(Directory.Exists(tests), Is.True, $"no plugin hook tests in {tests}");
        var info = new ProcessStartInfo("node") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add("--test");
        foreach (var file in Directory.EnumerateFiles(tests, "*.test.ts")) info.ArgumentList.Add(file);
        Process process;
        try { process = Process.Start(info)!; }
        catch (System.ComponentModel.Win32Exception) { Assert.Ignore("node is not installed"); return; }
        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            Assert.That(process.WaitForExit(120_000), Is.True, "node --test did not end in 120 s");
            Assert.That(process.ExitCode, Is.Zero, $"{output.Result}\n{error.Result}");
        }
    }

    static string SourcePath([CallerFilePath] string path = "") => path;
}
