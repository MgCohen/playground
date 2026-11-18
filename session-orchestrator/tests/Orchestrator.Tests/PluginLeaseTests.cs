using System.Diagnostics;
using Orchestrator.Store;

namespace Orchestrator.Tests;

/// <summary>
/// The plugin hooks in node with a fake engine ($). Review of PR #14, item 1: the brief went two times, with two writers,
/// when the user opened the app session while a supervisor turn ran.
/// </summary>
public sealed class PluginLeaseTests
{
    static readonly string Hooks = Path.Combine(Home.Root(), "plugin", "hooks");

    static string Url(string file) => new Uri(Path.Combine(Hooks, file)).AbsoluteUri;

    static string? Node(string script)
    {
        try
        {
            var info = new ProcessStartInfo("node", ["--input-type=module", "-e", script]) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return output.Trim();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [Test]
    public void The_live_plugin_does_not_take_the_lease_or_deliver_while_a_supervisor_turn_runs()
    {
        var home = Home.Root().Replace('\\', '/');
        var script = $$"""
            import { S } from '{{Url("state.ts")}}';
            import { deliver } from '{{Url("deliver.ts")}}';
            import { beat, mayTakeLease } from '{{Url("lease.ts")}}';
            import { readFileSync } from 'node:fs';
            const files = {};
            const dir = 'store/sessions/s1';
            let now = Date.parse('2026-11-17T10:00:40.000Z');
            let runs = 0, submits = 0;
            for (const kind of ['ack']) for (const name of ['meta', 'schema'])
              files[`{{home}}/templates/${kind}/${name}.json`] = readFileSync(`{{home}}/templates/${kind}/${name}.json`, 'utf8');
            const list = JSON.stringify({ id: '1-aaaaaaaa-cli', kind: 'brief', from: 'user', to: 'session:s1', text: 'Do the work.' });
            const $ = {
              fs: { exists: async p => p in files, read: async p => files[p], write: async (p, t) => { files[p] = String(t) } },
              clock: { now: async () => now },
              process: {
                run: async argv => { runs++; return { exitCode: 0, stdout: argv.includes('--list') ? list + '\n' : '', stderr: '' } },
                spawn: () => ({ [Symbol.asyncIterator]() { return { next: async () => ({ done: true }) } } }),
              },
              prompt: { submit: async () => { submits++; return {} } },
              env: { get: async () => undefined },
              session: { id: async () => 's1' },
            };
            Object.assign(S, { id: 's1', dir, store: 'store', home: '{{home}}', holder: 'live', idle: true, wanted: true, busy: false, dropped: '', timers: true, watching: true });
            files[`${dir}/enabled`] = '';
            // The supervisor turn started 40 s ago and runs; its claude -p process holds the lease.
            files[`${dir}/turn.json`] = JSON.stringify({ event: 't1', from: 'user', at: '2026-11-17T10:00:00.000Z' });
            files[`${dir}/lease.json`] = JSON.stringify({ holder: 'supervisor', since: '2026-11-17T10:00:01.000Z' });
            const out = [];
            out.push(mayTakeLease('live', JSON.parse(files[`${dir}/turn.json`]), JSON.parse(files[`${dir}/lease.json`]), now));
            await deliver($);
            out.push(runs, submits);
            // The claude -p process ends and releases the lease, but turn.json has no end yet: still no take, no delivery.
            files[`${dir}/lease.json`] = JSON.stringify({ holder: 'none', since: '2026-11-17T10:00:50.000Z' });
            now += 10000;
            await beat($);
            out.push(JSON.parse(files[`${dir}/lease.json`]).holder);
            S.wanted = true; await deliver($);
            out.push(runs, submits);
            // The supervisor writes the end of the turn: the live process takes the lease and delivers.
            files[`${dir}/turn.json`] = JSON.stringify({ event: 't1', from: 'user', at: '2026-11-17T10:00:00.000Z', ended: '2026-11-17T10:00:55.000Z' });
            await beat($);
            out.push(JSON.parse(files[`${dir}/lease.json`]).holder);
            S.wanted = true; await deliver($);
            out.push(runs, submits);
            console.log(out.join(','));
            """;
        var output = Node(script);
        if (output is null) Assert.Ignore("node is not on the PATH");
        Assert.That(output, Is.EqualTo("false,0,0,none,0,0,live,1,1"));
    }

    [Test]
    public void The_plugin_writes_no_meta_of_another_session_and_names_the_target_of_a_remote_control_call()
    {
        // Review of PR #14, item 2: the parent plugin wrote the meta.json of the child. Now it posts meta.update, and the registry writes session.json.
        var register = File.ReadAllText(Path.Combine(Hooks, "register.ts"));
        Assert.That(System.Text.RegularExpressions.Regex.Matches(register, @"writeSession\(\$, 'session\.json'").Count, Is.EqualTo(2), "only /orch-enable writes the entry of its own session: the create and a new title");
        Assert.That(register, Does.Contain("postMetaUpdate($, S.id!, 'appId', host)"));
        var output = Node($$"""
            import { remoteControlTarget } from '{{Url("register.ts")}}';
            console.log([remoteControlTarget('self', 'a'), remoteControlTarget(undefined, 'a'), remoteControlTarget('local_a', 'a'), remoteControlTarget('local_b', 'a'), remoteControlTarget('other', 'a')].join(','));
            """);
        if (output is null) Assert.Ignore("node is not on the PATH");
        Assert.That(output, Is.EqualTo("a,a,a,b,"));
    }

    [Test]
    public void The_node_hook_tests_pass_and_only_the_backend_has_store_io()
    {
        // H3: plugin/hooks/test/ holds the backend tests and the check that only backend.ts has store IO.
        try
        {
            var info = new ProcessStartInfo("node", ["--test", Path.Combine(Hooks, "test") + Path.DirectorySeparatorChar]) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.That(process.ExitCode, Is.EqualTo(0), output);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.Ignore("node is not on the PATH");
        }
    }

    [Test]
    public void A_turn_marker_with_no_end_blocks_the_live_lease_only_for_a_short_time_with_no_supervisor_holder()
    {
        var script = $$"""
            import { supervisorTurnRuns } from '{{Url("lease.ts")}}';
            const at = Date.parse('2026-11-17T10:00:00.000Z');
            const turn = { event: 't1', at: '2026-11-17T10:00:00.000Z' };
            console.log([
              supervisorTurnRuns(turn, { holder: 'supervisor' }, at + 3600000),
              supervisorTurnRuns(turn, { holder: 'none' }, at + 60000),
              supervisorTurnRuns(turn, { holder: 'none' }, at + 300000),
              supervisorTurnRuns({ ...turn, ended: 'x' }, { holder: 'supervisor' }, at),
              supervisorTurnRuns(undefined, { holder: 'none' }, at),
            ].join(','));
            """;
        var output = Node(script);
        if (output is null) Assert.Ignore("node is not on the PATH");
        Assert.That(output, Is.EqualTo("true,true,false,false,false"));
    }
}
