using api.Controllers;
using Microsoft.Extensions.Logging.Abstractions;
using static api.Tests.SocialTests;

namespace api.Tests;

public class SupportTests
{
    static SupportController Controller(TestHost h) =>
        h.As(new SupportController(h.Users, h.Email, NullLogger<SupportController>.Instance), null); // signed out

    static async Task<TestHost> WithAdmins()
    {
        var h = new TestHost();
        await h.AddUser("boss", admin: true);
        await h.AddUser("boss2", admin: true);
        await h.AddUser("plain");
        return h;
    }

    [Fact]
    public async Task A_message_is_mailed_to_every_admin_and_only_admins()
    {
        using var h = await WithAdmins();

        var r = await Controller(h).Send(new SupportRequest(" Kai ", "kai@a.com", "  my review vanished  ", null));

        Assert.Equal(200, Status(r));
        Assert.Equal(new[] { "boss2@example.com", "boss@example.com" }.Order(), h.Mails.Select(m => m.To).Order());
        Assert.All(h.Mails, m => Assert.Contains("Kai <kai@a.com>", m.Body));
        Assert.All(h.Mails, m => Assert.Contains("my review vanished", m.Body));
    }

    [Fact]
    public async Task Honeypot_answers_ok_but_sends_nothing()
    {
        using var h = await WithAdmins();
        var r = await Controller(h).Send(new SupportRequest(null, "bot@a.com", "buy now", "http://spam.example"));
        Assert.Equal(200, Status(r));
        Assert.Empty(h.Mails);
    }

    [Theory]
    [InlineData("not-an-email", "hello")]
    [InlineData("", "hello")]
    [InlineData("a@b.com\r\nBcc: x@y.com", "hello")]
    [InlineData("a@b.com", "")]
    [InlineData("a@b.com", "   ")]
    public async Task Bad_email_or_empty_message_is_refused(string email, string message)
    {
        using var h = await WithAdmins();
        Assert.Equal(400, Status(await Controller(h).Send(new SupportRequest(null, email, message, null))));
        Assert.Empty(h.Mails);
    }

    [Fact]
    public async Task Message_and_name_have_length_limits()
    {
        using var h = await WithAdmins();
        Assert.Equal(200, Status(await Controller(h).Send(new SupportRequest(null, "a@b.com", new string('x', 2000), null))));
        Assert.Equal(400, Status(await Controller(h).Send(new SupportRequest(null, "a@b.com", new string('x', 2001), null))));
        Assert.Equal(400, Status(await Controller(h).Send(new SupportRequest(new string('n', 101), "a@b.com", "hi", null))));
    }

    [Fact]
    public async Task A_line_break_in_the_name_cannot_forge_a_header_line()
    {
        using var h = await WithAdmins();
        await Controller(h).Send(new SupportRequest("Kai\nFrom: ceo@x.com", "a@b.com", "hi", null));
        Assert.All(h.Mails, m => Assert.StartsWith("From: Kai From: ceo@x.com <a@b.com>", m.Body));
    }

    [Fact]
    public async Task Failing_mail_or_no_admin_is_a_503()
    {
        using var h = await WithAdmins();
        h.MailFails = true;
        Assert.Equal(503, Status(await Controller(h).Send(new SupportRequest(null, "a@b.com", "hi", null))));

        using var none = new TestHost();
        await none.AddUser("plain");
        Assert.Equal(503, Status(await Controller(none).Send(new SupportRequest(null, "a@b.com", "hi", null))));
    }
}
