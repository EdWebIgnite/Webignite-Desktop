using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Webignite.Core.Accounts;
using Webignite.Server;

// The Webignite sync server: accounts, workspaces and (later) the encrypted records the apps sync. It runs behind
// a web server that does HTTPS (Caddy, see docs), so it only listens on the PC it runs on.
//
// Settings, from the environment:
//   WEBIGNITE_SERVER_SECRET  32+ random bytes, Base64. Required outside Development. Keep it out of the repository.
//   WEBIGNITE_SERVER_DATA    the folder for the database (default ./data).
//   WEBIGNITE_LICENCE_KEY    the key licences are signed with (Base64 PKCS#8). Make one with: make-licence-key
//
// Commands, instead of running the server (see docs/server-setup.md):
//   make-licence-key                              prints a new licence key pair
//   set-plan <owner email> <trial|active|complimentary> [paid until, yyyy-mm-dd]
if (args is ["make-licence-key"])
{
    var (privateKey, publicKey) = Licences.NewKeys();
    Console.WriteLine("Private key, for WEBIGNITE_LICENCE_KEY in .env (keep it secret):");
    Console.WriteLine(privateKey);
    Console.WriteLine();
    Console.WriteLine("Public key, for the app (safe to share):");
    Console.WriteLine(publicKey);
    return;
}

var builder = WebApplication.CreateBuilder(args);

var secretText = Environment.GetEnvironmentVariable("WEBIGNITE_SERVER_SECRET");
byte[] secret;
if (!string.IsNullOrWhiteSpace(secretText))
    secret = Convert.FromBase64String(secretText);
else if (builder.Environment.IsDevelopment() || builder.Environment.EnvironmentName == "Testing")
    secret = RandomNumberGenerator.GetBytes(32);
else
    throw new InvalidOperationException("Set WEBIGNITE_SERVER_SECRET (32 random bytes, Base64) before starting the server.");
if (secret.Length < 32)
    throw new InvalidOperationException("WEBIGNITE_SERVER_SECRET must be at least 32 bytes.");

var dataFolder = Environment.GetEnvironmentVariable("WEBIGNITE_SERVER_DATA") is { Length: > 0 } folder ? folder : Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dataFolder);

builder.Services.AddSingleton(sp => new ServerStore(sp.GetRequiredService<IConfiguration>()["Webignite:Database"] ?? Path.Combine(dataFolder, "webignite-server.db")));
builder.Services.AddSingleton(new ServerKeys(secret));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AccountService>();
builder.Services.AddSingleton<SyncService>();
builder.Services.AddSingleton(sp => new LicenceService(sp.GetRequiredService<ServerStore>(), sp.GetRequiredService<TimeProvider>(),
    LicenceService.KeyFrom(sp.GetRequiredService<IConfiguration>()["Webignite:LicenceKey"] ?? Environment.GetEnvironmentVariable("WEBIGNITE_LICENCE_KEY"))));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // Caddy on the same machine passes on the caller's address, for the limits below.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    o.KnownProxies.Add(System.Net.IPAddress.Loopback);
});
var signInPerMinute = builder.Configuration.GetValue("Webignite:SignInPerMinute", 20);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Signing in and up: 20 tries a minute from one address, so passwords can't be guessed quickly.
    o.AddPolicy("sign-in", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = signInPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("signed-in", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

if (args is ["set-plan", var email, var plan, ..] && LicencePlans.All.Contains(plan))
{
    DateTimeOffset? paidUntil = args.Length > 3 && DateOnly.TryParseExact(args[3], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
        ? new DateTimeOffset(day.ToDateTime(new TimeOnly(23, 59)), TimeSpan.Zero)
        : null;
    var done = app.Services.GetRequiredService<ServerStore>().SetPlan(AccountRules.Normal(email), plan, paidUntil);
    Console.WriteLine(done ? $"The workspace {email} owns is now on {plan}{(paidUntil is { } p ? $", paid until {p:yyyy-MM-dd}" : "")}." : $"No workspace owned by {email}.");
    return;
}
app.UseForwardedHeaders();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
AccountEndpoints.Map(app);

app.Run();

/// <summary>For the tests, which start the server in memory.</summary>
public partial class Program;
