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
//   WEBIGNITE_SERVER_SECRET  32+ random bytes, Base64. Keep it out of the repository. When it isn't set, the server
//                            makes one and keeps it in the data folder (ServerFiles), as on the Synology NAS.
//   WEBIGNITE_SERVER_DATA    the folder for the database (default ./data).
//   WEBIGNITE_LICENCE_KEY    the key licences are signed with (Base64 PKCS#8). Make one with: make-licence-key.
//                            When it isn't set, the server makes one and keeps it in the data folder too.
//   WEBIGNITE_NIGHTLY_BACKUP true for a copy of the database every night in the data folder's backups folder.
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

var dataFolder = Environment.GetEnvironmentVariable("WEBIGNITE_SERVER_DATA") is { Length: > 0 } folder ? folder : Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dataFolder);
// The tests and a developer's PC make throwaway keys; a real server keeps its own in the data folder unless given.
var throwaway = builder.Environment.IsDevelopment() || builder.Environment.EnvironmentName == "Testing";

var secretText = Environment.GetEnvironmentVariable("WEBIGNITE_SERVER_SECRET");
byte[] secret;
if (!string.IsNullOrWhiteSpace(secretText))
    secret = Convert.FromBase64String(secretText);
else if (throwaway)
    secret = RandomNumberGenerator.GetBytes(32);
else
    secret = Convert.FromBase64String(ServerFiles.Kept(dataFolder, ServerFiles.Secret, () => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
if (secret.Length < 32)
    throw new InvalidOperationException("WEBIGNITE_SERVER_SECRET must be at least 32 bytes.");

// Read when first needed, as the tests' settings only arrive once the app is built.
string? LicenceKeyText(IConfiguration settings)
{
    var text = settings["Webignite:LicenceKey"] ?? Environment.GetEnvironmentVariable("WEBIGNITE_LICENCE_KEY");
    return string.IsNullOrWhiteSpace(text) && !throwaway ? ServerFiles.Kept(dataFolder, ServerFiles.LicenceKey, () => Licences.NewKeys().PrivateKey) : text;
}

builder.Services.AddSingleton(sp => new ServerStore(sp.GetRequiredService<IConfiguration>()["Webignite:Database"] ?? Path.Combine(dataFolder, "webignite-server.db")));
builder.Services.AddSingleton(new ServerKeys(secret));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new AccountService(sp.GetRequiredService<ServerStore>(), sp.GetRequiredService<ServerKeys>(),
    sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IConfiguration>().GetValue("Webignite:SignUpsPerDay", 20)));
// Requests are small: the largest is a push of locked records, held to SyncLimits.
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = Webignite.Core.Sync.SyncLimits.LargestRequest);
builder.Services.AddSingleton<SyncService>();
builder.Services.AddSingleton(sp => new LicenceService(sp.GetRequiredService<ServerStore>(), sp.GetRequiredService<TimeProvider>(),
    LicenceService.KeyFrom(LicenceKeyText(sp.GetRequiredService<IConfiguration>()))));
if (Environment.GetEnvironmentVariable("WEBIGNITE_NIGHTLY_BACKUP") is "true" or "1")
{
    builder.Services.AddHostedService(sp => new NightlyBackup(sp.GetRequiredService<ServerStore>(), Path.Combine(dataFolder, "backups"),
        sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<NightlyBackup>>()));
}
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
// The public half of the licence key, to copy into the app (LicenceKey.Public). Safe to show anyone.
app.MapGet("/licence-public-key", (LicenceService licences) =>
    licences.PublicKey is { } key ? Results.Text(key) : Results.NotFound());
AccountEndpoints.Map(app);

app.Run();

/// <summary>For the tests, which start the server in memory.</summary>
public partial class Program;
