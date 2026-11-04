using System.Net.Sockets;
using MailKit;
using Webignite.Mail;

namespace Webignite.Tests;

/// <summary>When a mail server job is tried again after the connection dropped.</summary>
public sealed class MailRetryTests
{
    [Fact]
    public void A_dropped_connection_is_tried_once_more()
    {
        Assert.True(MailRetry.TryAgain(0, jobStarted: true, repeatable: true, new IOException("Connection reset")));
        Assert.True(MailRetry.TryAgain(0, jobStarted: false, repeatable: true, new SocketException()));
        Assert.True(MailRetry.TryAgain(0, jobStarted: false, repeatable: true, new ServiceNotConnectedException()));
        Assert.False(MailRetry.TryAgain(1, jobStarted: false, repeatable: true, new IOException()));
    }

    [Fact]
    public void Saving_a_copy_is_only_tried_again_when_it_never_started()
    {
        // The copy to Sent or Drafts may have landed before the line dropped: trying again could leave two.
        Assert.False(MailRetry.TryAgain(0, jobStarted: true, repeatable: false, new IOException()));
        Assert.True(MailRetry.TryAgain(0, jobStarted: false, repeatable: false, new IOException()));
    }

    [Fact]
    public void Other_problems_are_not_tried_again() =>
        Assert.False(MailRetry.TryAgain(0, jobStarted: false, repeatable: true, new InvalidOperationException()));
}
