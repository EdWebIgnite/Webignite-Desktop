using Webignite.Core.Mail;

namespace Webignite.Tests;

/// <summary>Setting up ChatGPT's tunnel: what OpenAI's tunnel ids and keys look like, and which download fits this PC.</summary>
public sealed class ChatGptTunnelTests
{
    [Fact]
    public void A_tunnel_id_is_taken_as_OpenAI_shows_it_and_anything_else_is_explained()
    {
        Assert.Equal("tunnel_0123456789abcdef0123456789abcdef", ChatGptTunnel.TunnelId("  tunnel_0123456789abcdef0123456789abcdef ").Id);
        Assert.Equal("tunnel_abcd_0123456789abcdef0123456789abcdef", ChatGptTunnel.TunnelId("tunnel_abcd_0123456789abcdef0123456789abcdef").Id);
        Assert.Contains("starts with tunnel_", ChatGptTunnel.TunnelId("").Problem, StringComparison.Ordinal);
        Assert.NotNull(ChatGptTunnel.TunnelId("tunnel_ABC").Problem);
        Assert.NotNull(ChatGptTunnel.TunnelId("sk-proj-0123456789abcdef0123456789abcdef").Problem);
    }

    [Fact]
    public void A_key_must_look_like_an_OpenAI_key()
    {
        Assert.Null(ChatGptTunnel.KeyProblem(" sk-proj-abcdefghijklmnopqrstuvwxyz0123 "));
        Assert.NotNull(ChatGptTunnel.KeyProblem(""));
        Assert.NotNull(ChatGptTunnel.KeyProblem("tunnel_0123456789abcdef0123456789abcdef"));
        Assert.NotNull(ChatGptTunnel.KeyProblem("sk-short"));
    }

    [Fact]
    public void The_plain_Windows_client_for_this_processor_is_downloaded()
    {
        string[] assets =
        [
            "SHA256SUMS.txt",
            "tunnel-client-runtime-v0.0.12-windows-amd64.zip",
            "tunnel-client-v0.0.12-linux-amd64.zip",
            "tunnel-client-v0.0.12-windows-amd64.zip",
            "tunnel-client-v0.0.12-windows-amd64.spdx.json",
            "tunnel-client-v0.0.12-windows-arm64.zip",
        ];
        Assert.Equal("tunnel-client-v0.0.12-windows-amd64.zip", ChatGptTunnel.PickDownload(assets, arm64: false));
        Assert.Equal("tunnel-client-v0.0.12-windows-arm64.zip", ChatGptTunnel.PickDownload(assets, arm64: true));
        Assert.Null(ChatGptTunnel.PickDownload(["tunnel-client-v0.0.12-linux-amd64.zip"], arm64: false));
    }

    [Fact]
    public void The_checksum_comes_from_the_releases_own_list()
    {
        var hash = new string('a', 64);
        var sums = $"{new string('b', 64)}  tunnel-client-v0.0.12-linux-amd64.zip\n{hash.ToUpperInvariant()} *tunnel-client-v0.0.12-windows-amd64.zip\n";
        Assert.Equal(hash, ChatGptTunnel.Checksum(sums, "tunnel-client-v0.0.12-windows-amd64.zip"));
        Assert.Null(ChatGptTunnel.Checksum(sums, "tunnel-client-v0.0.12-windows-arm64.zip"));
        Assert.Equal("\"C:\\Users\\Ed\\AppData\\Local\\Webignite\\Webignite.Connector.exe\"",
            ChatGptTunnel.Command(@"C:\Users\Ed\AppData\Local\Webignite\Webignite.Connector.exe"));
    }
}
