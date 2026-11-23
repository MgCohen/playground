using System.Net;
using System.Security.Cryptography;
using System.Text;
using Orchestrator.Store.Remote;

namespace Orchestrator.Web;

public sealed record SignIn(string? Token)
{
    public const string Cookie = "orch_token";
    public const string Header = HttpRepositories.TokenHeader;
    public const string Query = "token";

    /// <summary>The headers of a proxy or a tunnel (for example cloudflared). A request with one of them is never local.</summary>
    public static readonly IReadOnlyList<string> ForwardHeaders = ["X-Forwarded-For", "Forwarded", "X-Real-IP", "Cf-Connecting-Ip", "Cf-Access-Jwt-Assertion"];

    public bool IsOpen => string.IsNullOrEmpty(Token);

    public bool Accepts(string? value) =>
        !IsOpen && value is { Length: > 0 } && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(value), Encoding.UTF8.GetBytes(Token!));

    /// <summary>
    /// A local caller: a loopback address and no proxy header. It needs no token, as before H5 with no token. A tunnel
    /// connects from 127.0.0.1 too, so its proxy headers make the request remote (D1).
    /// </summary>
    public static bool IsLocal(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address)
        && !ForwardHeaders.Any(h => context.Request.Headers.ContainsKey(h));

    /// <summary>The token of a request: the header <see cref="Header"/>, a bearer token, or the sign-in cookie.</summary>
    public bool Signed(HttpContext context)
    {
        var bearer = context.Request.Headers.Authorization.ToString();
        return Accepts(context.Request.Headers[Header].ToString())
            || (bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && Accepts(bearer["Bearer ".Length..].Trim()))
            || Accepts(context.Request.Cookies[Cookie]);
    }
}

/// <summary>
/// The token check (H5). A local caller passes. A remote caller needs <c>ORCH_TOKEN</c>. With no token set, a remote
/// caller gets the pages and the API as before, but never the store API: that API needs a token.
/// </summary>
public sealed class SignInMiddleware(RequestDelegate next, SignIn signIn)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (SignIn.IsLocal(context))
        {
            await next(context);
            return;
        }
        if (signIn.IsOpen)
        {
            if (context.Request.Path.StartsWithSegments(StoreWire.Prefix))
            {
                await Refuse(context, StatusCodes.Status403Forbidden, "The store API needs a token for a remote caller. Set ORCH_TOKEN in the supervisor start and in the caller.");
                return;
            }
            await next(context);
            return;
        }
        if (context.Request.Query.TryGetValue(SignIn.Query, out var query) && signIn.Accepts(query))
        {
            context.Response.Cookies.Append(SignIn.Cookie, query!, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps, MaxAge = TimeSpan.FromDays(30) });
            context.Response.Redirect(context.Request.Path);
            return;
        }
        if (signIn.Signed(context))
        {
            await next(context);
            return;
        }
        await Refuse(context, StatusCodes.Status401Unauthorized, "Sign in: open this page once with ?token=<ORCH_TOKEN>. API calls send the header X-Orch-Token or Authorization: Bearer.");
    }

    static async Task Refuse(HttpContext context, int status, string text)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(text);
    }
}
