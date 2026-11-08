using Webignite.Core.Accounts;

namespace Webignite.Server;

/// <summary>The account endpoints, under /api.</summary>
public static class AccountEndpoints
{
    public static void Map(WebApplication app)
    {
        var open = app.MapGroup("/api").RequireRateLimiting("sign-in");
        open.MapPost("/kdf", (EmailRequest request, AccountService accounts) => Results.Ok(accounts.Kdf(request.Email ?? "")));
        open.MapPost("/accounts", (SignUpRequest request, AccountService accounts) => Reply(accounts.SignUp(request)));
        open.MapPost("/sessions", (SignInRequest request, AccountService accounts) => Reply(accounts.SignIn(request)));
        open.MapPost("/sessions/refresh", (RefreshRequest request, AccountService accounts) => Reply(accounts.Refresh(request)));

        var signedIn = app.MapGroup("/api").RequireRateLimiting("signed-in");
        signedIn.MapDelete("/sessions/current", (HttpContext http, AccountService accounts) =>
            SignedIn(http, accounts, session => { accounts.SignOut(session); return Results.NoContent(); }));
        signedIn.MapGet("/account", (HttpContext http, AccountService accounts) =>
            SignedIn(http, accounts, session => Results.Ok(accounts.Info(session.AccountId))));
        signedIn.MapPost("/two-factor", (HttpContext http, AccountService accounts) =>
            SignedIn(http, accounts, session => Reply(accounts.StartTwoFactor(session.AccountId))));
        signedIn.MapPost("/two-factor/confirm", (HttpContext http, CodeRequest request, AccountService accounts) =>
            SignedIn(http, accounts, session => Reply(accounts.ConfirmTwoFactor(session.AccountId, request.Code ?? ""))));
        signedIn.MapPost("/two-factor/off", (HttpContext http, CodeRequest request, AccountService accounts) =>
            SignedIn(http, accounts, session => Reply(accounts.StopTwoFactor(session, request.Code ?? ""), noContent: true)));
        signedIn.MapPost("/workspace/invites", (HttpContext http, AccountService accounts) =>
            SignedIn(http, accounts, session => Reply(accounts.Invite(session.AccountId))));
    }

    public sealed record EmailRequest(string? Email);

    private static IResult Reply<T>(Result<T> result, bool noContent = false) =>
        result.Problem is { } problem ? Results.Json(problem, statusCode: result.Status)
        : noContent ? Results.NoContent()
        : Results.Ok(result.Value);

    private static IResult SignedIn(HttpContext http, AccountService accounts, Func<ServerStore.Session, IResult> then)
    {
        var header = http.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.Ordinal) ? header["Bearer ".Length..].Trim() : null;
        return accounts.Authenticate(token) is { } session
            ? then(session)
            : Results.Json(new Problem("Sign in again."), statusCode: StatusCodes.Status401Unauthorized);
    }
}
