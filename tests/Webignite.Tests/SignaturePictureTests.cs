using Webignite.Core.Writing;

namespace Webignite.Tests;

public sealed class SignaturePictureTests
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, 0, 0, 0, 0, 0, 0, 0, 200, 0, 0, 0, 60];

    [Fact]
    public void A_mail_written_as_text_with_the_signature_typed_out_gets_the_signature_picture_after_it()
    {
        var mail = RichText.FromText("Hi Ronald,\n\nJust following up.\n\nEddie Teichert\nDirector\n\nPS. See you Tuesday.");
        var signature = RichText.Signature("Eddie Teichert\nDirector", Png, "image/png");

        Assert.True(RichText.AddSignatureIfMissing(mail, signature));
        var at = mail.Blocks.FindIndex(b => b is PictureBlock);
        Assert.True(at > 0);
        Assert.Equal("Director", ((TextBlock)mail.Blocks[at - 1]).Text);
        Assert.Single(mail.Blocks.OfType<PictureBlock>());
        Assert.False(RichText.AddSignatureIfMissing(mail, signature));
    }

    [Fact]
    public void Without_a_signature_picture_nothing_is_added_to_a_typed_out_signature()
    {
        var mail = RichText.FromText("Hi,\n\nEddie Teichert\nDirector");
        var before = mail.Blocks.Count;
        Assert.False(RichText.AddSignatureIfMissing(mail, RichText.Signature("Eddie Teichert\nDirector", null, null)));
        Assert.Equal(before, mail.Blocks.Count);
    }
}
