namespace Orchestrator.Web.Plugins.Js.Infrastructure;

using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using Orchestrator.Web.Plugins.Js.Contracts;

internal static class JsNames
{
    static readonly ConcurrentDictionary<Type, PropertyInfo[]> Parameters = new();

    public static string NameOf(JsCommand command) => command.Function ?? NameOf(command.GetType());

    public static string NameOf(Type type)
    {
        var words = Words(type.Name);
        if (words.Length < 2)
        {
            return type.Name;
        }
        var rest = string.Concat(words.Skip(1));
        return $"orch{words[0]}.{char.ToLowerInvariant(rest[0])}{rest[1..]}";
    }

    public static object?[] ArgumentsOf(JsCommand command) =>
        [.. Parameters.GetOrAdd(command.GetType(), ParametersOf).Select(p => p.GetValue(command))];

    public static string Show(object?[] arguments) => string.Join(", ", arguments.Select(Show));

    static string Show(object? argument) => argument switch
    {
        null => "null",
        string text => $"\"{text}\"",
        Microsoft.AspNetCore.Components.ElementReference => "element",
        _ => $"{argument}",
    };

    static PropertyInfo[] ParametersOf(Type type)
    {
        var constructor = type.GetConstructors()
            .Where(c => c.GetParameters() is not [{ } only] || only.ParameterType != type)
            .MaxBy(c => c.GetParameters().Length);
        return constructor is null
            ? []
            : [.. constructor.GetParameters().Select(p => type.GetProperty(p.Name!)!)];
    }

    static string[] Words(string name)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
            word.Append(c);
        }
        if (word.Length > 0)
        {
            words.Add(word.ToString());
        }
        return [.. words];
    }
}
