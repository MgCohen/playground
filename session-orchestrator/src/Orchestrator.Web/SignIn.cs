using System.Security.Cryptography;
using System.Text;

namespace Orchestrator.Web;

public sealed record SignIn(string? Token)
{
    public const string Cookie = "orch_token";
    public const string Header = "X-Orch-Token";
    public const string Query = "token";

    public bool IsOpen => string.IsNullOrEmpty(Token);

    public bool Accepts(string? value) =>
        IsOpen || (value is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(value), Encoding.UTF8.GetBytes(Token!)));
}

public sealed class SignInMiddleware(RequestDelegate next, SignIn signIn)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (signIn.IsOpen)
        {
            await next(context);
            return;
        }
        if (context.Request.Query.TryGetValue(SignIn.Query, out var query) && signIn.Accepts(query))
        {
            context.Response.Cookies.Append(SignIn.Cookie, query!, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps, MaxAge = TimeSpan.FromDays(30) });
            context.Response.Redirect(context.Request.Path);
            return;
        }
        var header = context.Request.Headers[SignIn.Header].ToString();
        var cookie = context.Request.Cookies[SignIn.Cookie];
        if (signIn.Accepts(header) || signIn.Accepts(cookie))
        {
            await next(context);
            return;
        }
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("Sign in: open this page once with ?token=<ORCH_TOKEN>. API calls send the header X-Orch-Token.");
    }
}
