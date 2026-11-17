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
              orch new --title T --goal G [--cwd DIR] [--scope S] [--done-when D] [--skills a,b] [--agent A] [--worktree] [--allow-defaults] [--background] [--parent ID]
              orch enable [--title T] [--parent ID] [--id ID]
              orch send <session> <text> [--from P]
              orch tree
              orch status <session>
              orch stop <session> [--reason R]
              orch resume <session>
              orch archive <session> [--force] [--tree] [--store-only] [--wait <seconds>]
              orch open <session> [--dry-run]
            Decisions
              orch decisions [--session ID]
              orch answer <decision> <text> [--via P]
              orch chat-answer <session> <text>
              orch inbox [accept|refuse <id> [text]]
              orch review <decision> approve|change|refuse [text]
              orch decision start <decision>
              orch decision pr <decision>
              orch decision ready <decision>
              orch stack rebase <decision>
              orch stack close <decision> [--text T]
              orch merge <pr>|<branch> [--base B]
            Stream and tools (P: user, supervisor, session:<id> or the first characters of a session id)
              orch init
              orch post <kind> --to P [--from P] [--thread T] [--needs N] [--reply ID] [--via P] [--json FILE|-] [--<field> value]
              orch events [--view inbox|decisions|timeline|thread|ledger [ARG]] [--since 3h] [--until T] [--after ID] [--kind a,b] [--session ID] [--party P] [--from P] [--to P] [--thread T] [--needs [answer|review|ack]] [--open] [--ledger] [--forwards false] [--heads] [--newest] [--limit N] [--follow] [--json] [--body]
              orch read <id> [--for P] [--render NAME]
              orch watch --to P [--kind a,b] [--thread T] [--render NAME] [--once] [--timeout S] [--list]
              orch nudge <party> [id]
              orch template [<kind>] [--check FILE] [--render FILE] [--name N]
              orch doctor
              orch process (the id and start time of the process that runs orch: the plugin puts them in lease.json)
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
                if (args.Positional[0] is not ("doctor" or "template" or "process")) Home.CheckVersion(store.Root, store.Clock.GetUtcNow());
                return args.Positional[0] switch
                {
                    "new" => SessionCommands.New(command),
                    "enable" => SessionCommands.Enable(command),
                    "send" => SessionCommands.Send(command),
                    "tree" => SessionCommands.Tree(command),
                    "status" => SessionCommands.Status(command),
                    "stop" => LifecycleCommands.Stop(command),
                    "resume" => LifecycleCommands.Resume(command),
                    "archive" => LifecycleCommands.Archive(command),
                    "open" => LifecycleCommands.Open(command),
                    "decisions" => DecisionCommands.List(command),
                    "answer" => DecisionCommands.Answer(command),
                    "chat-answer" => DecisionCommands.ChatAnswer(command),
                    "inbox" => DecisionCommands.Inbox(command),
                    "init" => EventCommands.Init(command),
                    "post" => EventCommands.Post(command),
                    "read" => EventCommands.Read(command),
                    "watch" => EventCommands.Watch(command),
                    "nudge" => EventCommands.Nudge(command),
                    "review" => DecisionCommands.Review(command),
                    "decision" => DecisionCommands.Decision(command),
                    "stack" => DecisionCommands.Stack(command),
                    "merge" => DecisionCommands.Merge(command),
                    "events" => EventCommands.Events(command),
                    "template" => ToolCommands.Template(command),
                    "doctor" => ToolCommands.Doctor(command),
                    "process" => ToolCommands.Process(command),
                    _ => throw new UsageException($"unknown command {args.Positional[0]}"),
                };
            }
            catch (UsageException e)
            {
                error.WriteLine($"orch: {e.Message}");
                error.WriteLine("Run orch --help for the commands.");
                return 2;
            }
            catch (Exception e) when (e is MessageException or ShellException or FormatException or IOException or InvalidDataException)
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
