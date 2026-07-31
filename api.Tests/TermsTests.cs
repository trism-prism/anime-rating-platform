using api.Controllers;
using api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using static api.Tests.SocialTests;

namespace api.Tests;

public class TermsTests
{
    static AuthController Auth(TestHost h) =>
        new(h.Users, h.Email, h.Sessions, new CredentialCheck(h.Users), h.Cache, h.Config, NullLogger<AuthController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() } };

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Register_needs_both_the_age_and_the_terms_box(bool age, bool terms)
    {
        using var h = new TestHost();
        var r = await Auth(h).Register(new RegisterRequest("kai@a.com", "Passw0rd1!xyz", null, age, terms));
        Assert.Equal(400, Status(r));
        Assert.Empty(h.Users.Users);
    }

    [Fact]
    public async Task Register_stores_when_and_which_terms_were_accepted()
    {
        using var h = new TestHost();
        Assert.Equal(200, Status(await Auth(h).Register(new RegisterRequest("kai@a.com", "Passw0rd1!xyz", null, true, true))));
        var user = Assert.Single(h.Users.Users);
        Assert.Equal(Terms.Version, user.TermsVersion);
        Assert.InRange(user.TermsAcceptedAt!.Value, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }
}
