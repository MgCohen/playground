using System.Text.Json;
using System.Text.RegularExpressions;

namespace Orchestrator.Supervisor;

/// <summary>The docs check: one model call of the provider reads the project docs and says if they answer a decision question.</summary>
public sealed partial class DocsChecker(IProvider provider) : IDocsChecker
{
    public async Task<DocsResult> Check(DocsRequest request, CancellationToken cancel)
    {
        var prompt = string.Join('\n',
            "You check if the project documentation already answers a question. Do not change any file.",
            $"Read only these paths in the working folder, if they exist: {string.Join(", ", request.Docs)}.",
            $"Question: {request.Question}",
            $"Options: {(request.Options.Count > 0 ? string.Join(", ", request.Options) : "none given")}",
            "If the documentation gives a clear answer, reply with only this JSON: {\"answered\": true, \"answer\": \"<the answer>\", \"source\": \"<file and section>\"}.",
            "If the answer is not clear in the documentation, reply with only: {\"answered\": false}.");
        var output = await provider.Ask(new AskRequest(request.Cwd, prompt, 12), cancel);
        return ParseReply(output.Text ?? "");
    }

    public static DocsResult ParseReply(string text)
    {
        var match = Braces().Match(text);
        if (!match.Success) return new DocsResult(false);
        try
        {
            var value = JsonSerializer.Deserialize<JsonElement>(match.Value);
            return value.TryGetProperty("answered", out var answered) && answered.ValueKind == JsonValueKind.True
                && value.TryGetProperty("answer", out var answer) && answer.ValueKind == JsonValueKind.String
                ? new DocsResult(true, answer.GetString(), value.TryGetProperty("source", out var source) ? source.GetString() : null)
                : new DocsResult(false);
        }
        catch (JsonException)
        {
            return new DocsResult(false);
        }
    }

    [GeneratedRegex(@"\{[\s\S]*\}")]
    private static partial Regex Braces();
}
