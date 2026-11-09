using System.Net;
using System.Text;

namespace Webignite.Xero.Auth;

/// <summary>
/// Waits on http://localhost:{port}/callback for Xero to send the sign-in code back, the standard
/// way for desktop apps (RFC 8252). It listens on this PC only and stops after one answer.
/// </summary>
public sealed class LoopbackReceiver : IDisposable
{
    private readonly HttpListener listener = new();

    public LoopbackReceiver(int port)
    {
        listener.Prefixes.Add($"http://localhost:{port}/callback/");
        listener.Start();
    }

    public async Task<string> WaitForCodeAsync(string expectedState, CancellationToken cancel)
    {
        while (true)
        {
            var context = await listener.GetContextAsync().WaitAsync(cancel);
            var query = context.Request.QueryString;
            // Anything without this sign-in's own state is ignored, an error included: another page on this PC
            // could open this address, and mustn't be able to cancel the sign-in or slip in a code.
            if (query["state"] != expectedState)
            {
                await Reply(context, query["code"] is null && query["error"] is null ? "Waiting for Xero." : "This sign-in link doesn't match. Start again from Webignite.",
                    query["code"] is null && query["error"] is null ? 404 : 400);
                continue;
            }
            if (query["error"] is { } error)
            {
                await Reply(context, "Xero sign-in was cancelled. You can close this tab.");
                throw new XeroSignInException(error == "access_denied" ? "Sign-in was cancelled in Xero." : $"Xero refused the sign-in: {error}.");
            }
            if (query["code"] is not { Length: > 0 } code)
            {
                await Reply(context, "Waiting for Xero.", 404);
                continue;
            }
            await Reply(context, "Signed in to Xero. You can close this tab and go back to Webignite.");
            return code;
        }
    }

    public void Dispose() => listener.Close();

    private static async Task Reply(HttpListenerContext context, string message, int status = 200)
    {
        var html = $"<!doctype html><meta charset=utf-8><title>Webignite</title><body style=\"font:16px system-ui;margin:3rem\">{WebUtility.HtmlEncode(message)}</body>";
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }
}

public sealed class XeroSignInException(string message) : Webignite.Xero.XeroException(message);
