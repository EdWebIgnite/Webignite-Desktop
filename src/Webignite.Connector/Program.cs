using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Webignite.Connector;
using Webignite.Data;
using Webignite.Mail;

// The connector Claude Desktop starts to reach Webignite. It talks over stdin and stdout, so nothing
// else may be written to stdout: logs go to stderr.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(_ => LocalStore.OpenDefault());
builder.Services.AddSingleton<ISecretStore>(_ => OperatingSystem.IsWindows() ? new WindowsSecretStore() : new MemorySecretStore());
builder.Services.AddSingleton(sp => new MailService(sp.GetRequiredService<LocalStore>(), sp.GetRequiredService<ISecretStore>()));
builder.Services.AddSingleton(sp => new AssistantTools(sp.GetRequiredService<LocalStore>(), sp.GetRequiredService<MailService>()));

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "webignite", Title = "Webignite", Version = "1.0.0" };
        options.ServerInstructions = Instructions.Text;
    })
    .WithStdioServerTransport()
    .WithTools<MailTools>();

await builder.Build().RunAsync();
