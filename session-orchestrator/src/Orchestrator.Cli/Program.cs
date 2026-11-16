using Orchestrator.Cli;
using Orchestrator.Cli.Git;
using Orchestrator.Store;

Console.OutputEncoding = System.Text.Encoding.UTF8;
return Cli.Run(args, Console.Out, Console.Error, Environment.CurrentDirectory);

namespace Orchestrator.Cli
{
    public static class Cli
    {
        public const string Usage = """
            orch: the command of the session orchestrator. Each command reads or writes the store (ORCH_STORE).

            Sessions
              orch new --title T --goal G [--cwd DIR] [--scope S] [--done-when D] [--skills a,b] [--agent A] [--worktree] [--parent ID]
              orch send <session> <text>
              orch tree
              orch status <session>
              orch stop <session> [--reason R]
              orch resume <session>
              orch archive <session> [--force]
              orch open <session> [--dry-run]
            Decisions
              orch decisions
              orch answer <decision> <text>
              orch review <decision> approve|change|refuse [text]
              orch decision start <decision>
              orch decision pr <decision>
              orch decision ready <decision>
              orch stack rebase <decision>
              orch stack close <decision> [--text T]
              orch merge <pr>|<branch> [--base B]
            Stream and tools
              orch events [--since 3h] [--kind a,b] [--session ID] [--from P] [--open] [--ledger] [--follow] [--json] [--body]
              orch template [<kind>] [--check FILE] [--render FILE] [--name N]
              orch doctor
            A <session> can be the first characters of its id.
            """;

        public static int Run(string[] raw, TextWriter output, TextWriter error, string cwd)
        {
            var args = new Args(raw);
            if (args.Positional.Count == 0 || args.Flag("help"))
            {
                output.WriteLine(Usage);
                return args.Positional.Count == 0 && !args.Flag("help") ? 2 : 0;
            }
            var store = new FileStore(Home.StoreRoot(), writer: "cli");
            var command = new CommandContext(store, args, output, cwd);
            try
            {
                return args.Positional[0] switch
                {
                    "new" => SessionCommands.New(command),
                    "send" => SessionCommands.Send(command),
                    "tree" => SessionCommands.Tree(command),
                    "status" => SessionCommands.Status(command),
                    "stop" => LifecycleCommands.Stop(command),
                    "resume" => LifecycleCommands.Resume(command),
                    "archive" => LifecycleCommands.Archive(command),
                    "open" => LifecycleCommands.Open(command),
                    "decisions" => DecisionCommands.List(command),
                    "answer" => DecisionCommands.Answer(command),
                    "review" => DecisionCommands.Review(command),
                    "decision" => DecisionCommands.Decision(command),
                    "stack" => DecisionCommands.Stack(command),
                    "merge" => DecisionCommands.Merge(command),
                    "events" => EventCommands.Events(command),
                    "template" => ToolCommands.Template(command),
                    "doctor" => ToolCommands.Doctor(command),
                    _ => throw new UsageException($"unknown command {args.Positional[0]}"),
                };
            }
            catch (UsageException e)
            {
                error.WriteLine($"orch: {e.Message}");
                error.WriteLine("Run orch --help for the commands.");
                return 2;
            }
            catch (Exception e) when (e is MessageException or ShellException or FormatException or IOException)
            {
                error.WriteLine($"orch: {e.Message}");
                return 1;
            }
        }
    }

    public sealed record CommandContext(FileStore Store, Args Args, TextWriter Output, string Cwd)
    {
        public Messages Messages => new(Store);
        public Shell Shell => new(Cwd);
        public StackOps Stack => new(Store, Shell, Output);
        public string Session(int index) => Messages.RequireSession(Args.Required(index, "session"));
    }
}
