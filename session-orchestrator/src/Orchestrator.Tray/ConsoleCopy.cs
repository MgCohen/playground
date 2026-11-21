using System.Text;

namespace Orchestrator.Tray;

/// <summary>
/// I17: the tray is a Windows app, so its console output is lost when a script starts it with a redirect. A self-test also
/// writes its console output to &lt;folder&gt;/console.txt. Each run has its own folder, so no two runs write the same file.
/// </summary>
public sealed class ConsoleCopy : TextWriter
{
    readonly TextWriter first;
    readonly TextWriter copy;

    ConsoleCopy(TextWriter first, TextWriter copy)
    {
        this.first = first;
        this.copy = copy;
    }

    public override Encoding Encoding => first.Encoding;

    /// <summary>Starts the copy of the console output and the error output to the file.</summary>
    public static void Start(string file)
    {
        var writer = TextWriter.Synchronized(new StreamWriter(file, append: false, Encoding.UTF8) { AutoFlush = true });
        Console.SetOut(new ConsoleCopy(Console.Out, writer));
        Console.SetError(new ConsoleCopy(Console.Error, writer));
    }

    public override void Write(char value)
    {
        first.Write(value);
        copy.Write(value);
    }

    public override void Write(string? value)
    {
        first.Write(value);
        copy.Write(value);
    }

    public override void WriteLine(string? value)
    {
        first.WriteLine(value);
        copy.WriteLine(value);
    }
}
