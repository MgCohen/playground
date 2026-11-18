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
              orch new --title T --goal G [--cwd DIR] [--scope S] [--done-when D] [--skills a,b] [--type A] [--stream S] [--workflow W] [--worktree] [--allow-defaults] [--background] [--where local|cloud] [--parent ID] [--app-start]
                                                        --app-start: a caller in the desktop app gets a start file and the app call that starts the child there
              orch enable [--title T] [--parent ID] [--id ID]
              orch join <start file> [--id ID]         the first command of a child that the app started: the store entry, the brief, the parent link
              orch send <session> <text> [--from P]
              orch tree [--stream S]
              orch status <session> | --stream S
              orch stop <session> [--reason R]
              orch resume <session>
              orch archive <session> [--force] [--tree] [--store-only] [--wait <seconds>]
              orch open <session> [--dry-run]
            Workflow runs (a <run> can be the first characters of its id)
              orch run start <workflow> [--owner SESSION] [--goal G] [--input-<name> text]
              orch run show <run> [--json]
              orch run list [--owner SESSION] [--open]
              orch run step <run> [--holds "c1; c2"] [--output-<name> text] [--pick STEP] [--answer T] [--verdict approve|change|refuse] [--skip-approval] [--done]
              orch run stop <run> [--reason R]
            Questions (old name: decisions; the command names stay)
              orch questions [--session ID] [--stream S]           (old name, kept: orch decisions)
              orch answer <question> <text> [--via P]
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
              orch post <kind> --to P [--from P] [--thread T] [--stream S] [--needs N] [--reply ID] [--via P] [--json FILE|-] [--<field> value]
              orch events [--view inbox|decisions|timeline|thread|ledger [ARG]] [--since 3h] [--until T] [--after ID] [--kind a,b] [--session ID] [--party P] [--from P] [--to P] [--thread T] [--needs [answer|review|ack]] [--open] [--ledger] [--forwards false] [--heads] [--newest] [--limit N] [--follow] [--json] [--body]
              orch read <id> [--for P] [--render NAME]
              orch watch --to P [--kind a,b] [--thread T] [--render NAME] [--once] [--timeout S] [--list]
              orch nudge <party> [id]
              orch template [list]                      the templates: core, or store (a store template can replace a core one)
              orch template <kind> [--check FILE] [--render FILE] [--name N]
              orch template check [<kind>] [--folder DIR]   check against templates/_spec (no kind: also the modes and workflows)
              orch template modes | workflows           the session modes and the workflow definitions: core, or store
              orch template add <folder> [--replace]    check, then copy to <store>/templates/<folder name>
              orch template remove <kind>               move a store template to <store>/templates/.removed
              orch doctor
              orch store [root]                          the store root: ORCH_STORE, else %LOCALAPPDATA%\orch\store
            HTTP mode: with ORCH_URL (or with no store folder and no ORCH_STORE), each store command calls the store API of the supervisor. ORCH_TOKEN is the token
              orch store migrate [--from DIR] [--to DIR] [--dry-run] [--force]   copy the old store (<home>/store) to the store root and check the copy. Deletes nothing
              orch store upgrade [--dry-run] [--force]   upgrade a store of version 2 to version 3: the session registry (session.json). Keeps meta.json and control.json
              orch supervisor start|stop|status [--port P] (the supervisor of ORCH_STORE: a published copy in ORCH_APP, else dotnet run of the source)
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
            var word = args.Positional[0];
            try
            {
                var store = new FileStore(Home.CliStoreRoot(local: word is "supervisor" or "process" or "store", fallback: word is not ("init" or "doctor" or "template")), writer: "cli");
                var command = new CommandContext(store, args, output, cwd);
                if (args.Positional[0] is not ("doctor" or "template" or "process" or "supervisor" or "store")) Home.CheckVersion(store.Repositories, store.Clock.GetUtcNow());
                return args.Positional[0] switch
                {
                    "new" => SessionCommands.New(command),
                    "enable" => SessionCommands.Enable(command),
                    "join" => SessionCommands.Join(command),
                    "send" => SessionCommands.Send(command),
                    "tree" => SessionCommands.Tree(command),
                    "status" => SessionCommands.Status(command),
                    "stop" => LifecycleCommands.Stop(command),
                    "resume" => LifecycleCommands.Resume(command),
                    "archive" => LifecycleCommands.Archive(command),
                    "open" => LifecycleCommands.Open(command),
                    "decisions" or "questions" => DecisionCommands.List(command),
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
                    "run" => RunCommands.Run(command),
                    "store" => StoreCommands.Store(command),
                    "supervisor" => ToolCommands.Supervisor(command),
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
            catch (Exception e) when (e is MessageException or RegistryException or ShellException or FormatException or IOException or InvalidDataException or Core.Templates.TemplateException or TimeoutException or Core.Workflows.WorkflowException)
            {
                error.WriteLine($"orch: {e.Message}");
                return 1;
            }
        }
    }

    public sealed record CommandContext(FileStore Store, Args Args, TextWriter Output, string Cwd)
    {
        /// <summary>The engine of the sessions: the caller session id, the app open and the doctor checks.</summary>
        public IProvider Provider { get; init; } = ProviderFactory.Default;
        public Messages Messages => new(Store);
        public Shell Shell => new(Cwd);
        public StackOps Stack => new(Store, Shell, Output);
        public string Session(int index) => Messages.RequireSession(Args.Required(index, "session"));
    }
}
