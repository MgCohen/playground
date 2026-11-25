using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Orchestrator.Store.Remote.Notion;

/// <summary>
/// The few Notion REST calls of the Notion store (N1 spike). One request at a time, at most 3 each second (the Notion
/// limit). A 429, 409 or 5xx answer, or a network error, waits and tries again (5 attempts). Version <see cref="ApiVersion"/>: databases, not data sources.
/// </summary>
public sealed class NotionClient
{
    public const string ApiVersion = "2022-06-28";

    /// <summary>The Notion limit for one text item of a rich text value.</summary>
    const int Chunk = 2000;

    /// <summary>Notion returns at most this many items of a rich text value in a page or a query result.</summary>
    const int Inline = 25;

    static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(340);

    /// <summary><c>ORCH_NOTION_TRACE=1</c> writes one line to stderr for each request. The spike counts the requests of a tick with it.</summary>
    static readonly bool Trace = Environment.GetEnvironmentVariable("ORCH_NOTION_TRACE") == "1";

    readonly HttpClient http;
    readonly Lock gate = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    TimeSpan last = TimeSpan.FromHours(-1);

    public NotionClient(string token, HttpClient? client = null)
    {
        http = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.BaseAddress ??= new Uri("https://api.notion.com/v1/");
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        http.DefaultRequestHeaders.Add("Notion-Version", ApiVersion);
    }

    /// <summary>The number of requests that this client sent. The spike measures it.</summary>
    public int Requests { get; private set; }

    /// <summary>The number of requests that this client sent again after a 429, a 409, a 5xx or a network error.</summary>
    public int Retries { get; private set; }

    /// <summary>The server time of the last answer (the Date header).</summary>
    public DateTimeOffset? ServerTime { get; private set; }

    public JsonObject Send(HttpMethod method, string path, JsonObject? body = null)
    {
        lock (gate)
        {
            for (var attempt = 1; ; attempt++)
            {
                var wait = Gap - (clock.Elapsed - last);
                if (wait > TimeSpan.Zero) Thread.Sleep(wait);
                last = clock.Elapsed;
                Requests++;
                if (Trace) Console.Error.WriteLine($"notion {DateTimeOffset.UtcNow:HH:mm:ss.fff} {method} {path.Split('?')[0]}");
                using var request = new HttpRequestMessage(method, path);
                if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                HttpResponseMessage sent;
                try
                {
                    sent = http.Send(request);
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException && attempt < 5)
                {
                    Retries++;
                    Thread.Sleep(TimeSpan.FromSeconds(attempt));
                    continue;
                }
                using var response = sent;
                ServerTime = response.Headers.Date ?? ServerTime;
                var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode) return JsonNode.Parse(text) as JsonObject ?? [];
                var again = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Conflict || (int)response.StatusCode >= 500;
                if (!again || attempt >= 5)
                    throw new IOException($"Notion refused {method} {path} ({(int)response.StatusCode}): {text}");
                Retries++;
                var after = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt);
                Thread.Sleep(after);
            }
        }
    }

    public string CreateDatabase(string parentPage, string title, JsonObject properties) => IdOf(CreateDatabaseOf(parentPage, title, properties));

    /// <summary>Makes a database and gives the result: the properties with the names of the dual relations.</summary>
    public JsonObject CreateDatabaseOf(string parentPage, string title, JsonObject properties) =>
        Send(HttpMethod.Post, "databases", new JsonObject
        {
            ["parent"] = new JsonObject { ["type"] = "page_id", ["page_id"] = parentPage },
            ["title"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = new JsonObject { ["content"] = title } }),
            ["is_inline"] = true,
            ["properties"] = properties,
        });

    /// <summary>Adds, renames or changes properties of a database.</summary>
    public JsonObject UpdateDatabase(string database, JsonObject properties) =>
        Send(HttpMethod.Patch, $"databases/{database}", new JsonObject { ["properties"] = properties });

    public string CreatePage(string parentPage, string title) =>
        IdOf(Send(HttpMethod.Post, "pages", new JsonObject
        {
            ["parent"] = new JsonObject { ["page_id"] = parentPage },
            ["properties"] = new JsonObject { ["title"] = Title(title) },
        }));

    public string CreateRow(string database, JsonObject properties, JsonArray? children = null)
    {
        var body = new JsonObject { ["parent"] = new JsonObject { ["database_id"] = database }, ["properties"] = properties };
        if (children is { Count: > 0 }) body["children"] = children;
        return IdOf(Send(HttpMethod.Post, "pages", body));
    }

    public string UpdateRow(string pageId, JsonObject properties) =>
        IdOf(Send(HttpMethod.Patch, $"pages/{pageId}", new JsonObject { ["properties"] = properties }));

    /// <summary>Adds blocks to the end of a page, at most 100 for each request.</summary>
    public void AppendBlocks(string pageId, JsonArray blocks)
    {
        var all = blocks.Select(b => b!.DeepClone()).ToList();
        for (var i = 0; i < all.Count; i += 100)
            Send(HttpMethod.Patch, $"blocks/{pageId}/children", new JsonObject { ["children"] = new JsonArray(all.Skip(i).Take(100).ToArray()) });
    }

    public void DeleteBlock(string blockId) => Send(HttpMethod.Delete, $"blocks/{blockId}");

    /// <summary>A block with a code text: the exact text in items of at most 2,000 characters, and a caption.</summary>
    public static JsonObject Code(string text, string language, string caption) => new()
    {
        ["object"] = "block",
        ["type"] = "code",
        ["code"] = new JsonObject { ["language"] = language, ["rich_text"] = Items(text), ["caption"] = Items(caption) },
    };

    /// <summary>A block of one type with a text: heading_3, paragraph, bulleted_list_item, numbered_list_item, quote.</summary>
    public static JsonObject Block(string type, string text) =>
        new() { ["object"] = "block", ["type"] = type, [type] = new JsonObject { ["rich_text"] = Items(text) } };

    public static JsonObject Divider() => new() { ["object"] = "block", ["type"] = "divider", ["divider"] = new JsonObject() };

    public static string Plain(JsonNode? items) => Join(items);

    public void Archive(string pageId) => Send(HttpMethod.Patch, $"pages/{pageId}", new JsonObject { ["archived"] = true });

    public void AppendParagraph(string pageId, string line) =>
        Send(HttpMethod.Patch, $"blocks/{pageId}/children", new JsonObject
        {
            ["children"] = new JsonArray(new JsonObject
            {
                ["object"] = "block",
                ["type"] = "paragraph",
                ["paragraph"] = new JsonObject { ["rich_text"] = RichText(line)["rich_text"]!.DeepClone() },
            }),
        });

    /// <summary>The child blocks of a page, all pages of the result.</summary>
    public List<JsonObject> Children(string pageId)
    {
        var all = new List<JsonObject>();
        string? cursor = null;
        do
        {
            var result = Send(HttpMethod.Get, $"blocks/{pageId}/children?page_size=100{(cursor is null ? "" : $"&start_cursor={cursor}")}");
            all.AddRange(result["results"]!.AsArray().OfType<JsonObject>());
            cursor = result["has_more"]?.GetValue<bool>() == true ? result["next_cursor"]?.GetValue<string>() : null;
        }
        while (cursor is not null);
        return all;
    }

    /// <summary>The rows of a database that match the filter, all pages of the result. A long text is read in full.</summary>
    public List<JsonObject> Query(string database, JsonObject? filter, JsonArray? sorts)
    {
        var all = new List<JsonObject>();
        string? cursor = null;
        do
        {
            var body = new JsonObject { ["page_size"] = 100 };
            if (filter is not null) body["filter"] = filter.DeepClone();
            if (sorts is not null) body["sorts"] = sorts.DeepClone();
            if (cursor is not null) body["start_cursor"] = cursor;
            var result = Send(HttpMethod.Post, $"databases/{database}/query", body);
            all.AddRange(result["results"]!.AsArray().OfType<JsonObject>());
            cursor = result["has_more"]?.GetValue<bool>() == true ? result["next_cursor"]?.GetValue<string>() : null;
        }
        while (cursor is not null);
        foreach (var row in all) CompleteText(row, "Text");
        return all;
    }

    /// <summary>A page or a query gives at most 25 items of a rich text. For a longer text, read the property item in full.</summary>
    void CompleteText(JsonObject row, string name)
    {
        if (row["properties"]?[name] is not JsonObject property || property["rich_text"] is not JsonArray items || items.Count < Inline) return;
        var propertyId = property["id"]!.GetValue<string>();
        var full = new JsonArray();
        string? cursor = null;
        do
        {
            var result = Send(HttpMethod.Get, $"pages/{IdOf(row)}/properties/{Uri.EscapeDataString(propertyId)}?page_size=100{(cursor is null ? "" : $"&start_cursor={cursor}")}");
            foreach (var item in result["results"]!.AsArray().OfType<JsonObject>())
                if (item["rich_text"] is JsonObject text) full.Add(text.DeepClone());
            cursor = result["has_more"]?.GetValue<bool>() == true ? result["next_cursor"]?.GetValue<string>() : null;
        }
        while (cursor is not null);
        property["rich_text"] = full;
    }

    // ---- values ----

    public static JsonObject Title(string text) => new() { ["title"] = Items(text) };

    public static JsonObject RichText(string text) => new() { ["rich_text"] = Items(text) };

    /// <summary>A relation value: the pages with these ids. No id clears the relation.</summary>
    public static JsonObject Relation(params string?[] pages) =>
        new() { ["relation"] = new JsonArray(pages.Where(p => p is { Length: > 0 }).Distinct().Select(p => (JsonNode)new JsonObject { ["id"] = p }).ToArray()) };

    /// <summary>A paragraph block. A bold label comes first when it is given.</summary>
    public static JsonObject Paragraph(string? label, string text)
    {
        var items = Items(text);
        if (label is { Length: > 0 })
            items.Insert(0, new JsonObject { ["type"] = "text", ["text"] = new JsonObject { ["content"] = label + ": " }, ["annotations"] = new JsonObject { ["bold"] = true } });
        return new() { ["object"] = "block", ["type"] = "paragraph", ["paragraph"] = new JsonObject { ["rich_text"] = items } };
    }

    public static JsonObject Select(string name) => new() { ["select"] = new JsonObject { ["name"] = name.Replace(',', ' ') } };

    /// <summary>A text as items of at most 2000 characters. Notion takes at most 100 items: a longer text fails.</summary>
    static JsonArray Items(string text)
    {
        var items = new JsonArray();
        for (var i = 0; i < text.Length; i += Chunk)
        {
            var end = Math.Min(text.Length, i + Chunk);
            if (end < text.Length && char.IsHighSurrogate(text[end - 1])) end--;
            items.Add(new JsonObject { ["type"] = "text", ["text"] = new JsonObject { ["content"] = text[i..end] } });
            i = end - Chunk;
        }
        if (items.Count > 100)
            throw new InvalidDataException($"the text has {text.Length} characters. A Notion text property takes at most 200,000.");
        return items;
    }

    public static string IdOf(JsonObject node) => node["id"]!.GetValue<string>().Replace("-", "");

    public static DateTimeOffset CreatedOf(JsonObject row) => DateTimeOffset.Parse(row["created_time"]!.GetValue<string>());

    public static string ReadTitle(JsonObject row, string name) => Join(row["properties"]?[name]?["title"]);

    public static string ReadText(JsonObject row, string name) => Join(row["properties"]?[name]?["rich_text"]);

    public static string ReadSelect(JsonObject row, string name) => row["properties"]?[name]?["select"]?["name"]?.GetValue<string>() ?? "";

    public static double ReadNumber(JsonObject row, string name) => row["properties"]?[name]?["number"]?.GetValue<double>() ?? 0;

    public static bool ReadCheck(JsonObject row, string name) => row["properties"]?[name]?["checkbox"]?.GetValue<bool>() ?? false;

    static string Join(JsonNode? items) =>
        items is JsonArray array ? string.Concat(array.Select(i => i?["plain_text"]?.GetValue<string>() ?? i?["text"]?["content"]?.GetValue<string>() ?? "")) : "";
}
