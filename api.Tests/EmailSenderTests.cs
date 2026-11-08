using System.Net;
using System.Text;
using System.Text.Json;
using api.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace api.Tests;

public class EmailSenderTests
{
    class FakeHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status);
        }
    }

    static EmailSender Make(FakeHandler handler)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Resend:ApiKey"] = "re_test_key",
            ["Resend:From"] = "noreply@example.com"
        }).Build();
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = Environments.Production };
        return new EmailSender(config, NullLogger<EmailSender>.Instance, env, new HttpClient(handler));
    }

    [Fact]
    public async Task Send_posts_json_to_resend_with_bearer_key()
    {
        var handler = new FakeHandler(HttpStatusCode.OK);
        await Make(handler).Send("kai@a.com", "Hi", "Body text");

        Assert.Equal("https://api.resend.com/emails", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer re_test_key", handler.Request.Headers.Authorization!.ToString());
        var json = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Equal("noreply@example.com", json.GetProperty("from").GetString());
        Assert.Equal("kai@a.com", json.GetProperty("to")[0].GetString());
        Assert.Equal("Hi", json.GetProperty("subject").GetString());
        Assert.Equal("Body text", json.GetProperty("text").GetString());
    }

    class GmailHandler(HttpStatusCode sendStatus) : HttpMessageHandler
    {
        public List<(string Url, string? Auth, string Body)> Calls = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), await request.Content!.ReadAsStringAsync(ct)));
            if (request.RequestUri.Host == "oauth2.googleapis.com")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"ya29.test\",\"expires_in\":3599}")
                };
            return new HttpResponseMessage(sendStatus);
        }
    }

    // each test gets its own refresh token because the access token cache is static
    static EmailSender MakeGmail(HttpMessageHandler handler, string refresh, bool withResend = false)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Gmail:ClientId"] = "cid",
            ["Gmail:ClientSecret"] = "csecret",
            ["Gmail:RefreshToken"] = refresh,
            ["Gmail:From"] = "GengaRate <me@gmail.com>"
        };
        if (withResend) settings["Resend:ApiKey"] = "re_test_key";
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = Environments.Production };
        return new EmailSender(config, NullLogger<EmailSender>.Instance, env, new HttpClient(handler));
    }

    [Fact]
    public async Task Gmail_gets_token_then_sends_raw_message_with_bearer()
    {
        var handler = new GmailHandler(HttpStatusCode.OK);
        await MakeGmail(handler, "refresh-1", withResend: true).Send("kai@a.com", "Hi", "Confirm: https://x.test/c?t=1");

        Assert.Equal(2, handler.Calls.Count);
        Assert.Equal("https://oauth2.googleapis.com/token", handler.Calls[0].Url);
        Assert.Contains("grant_type=refresh_token", handler.Calls[0].Body);
        Assert.Contains("refresh_token=refresh-1", handler.Calls[0].Body);
        Assert.Equal("https://gmail.googleapis.com/gmail/v1/users/me/messages/send", handler.Calls[1].Url);
        Assert.Equal("Bearer ya29.test", handler.Calls[1].Auth);

        var raw = JsonDocument.Parse(handler.Calls[1].Body).RootElement.GetProperty("raw").GetString()!;
        var message = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(raw));
        Assert.Contains("To: kai@a.com\r\n", message);
        Assert.Contains("From: GengaRate <me@gmail.com>\r\n", message);
        var b64 = message.Split("\r\n\r\n", 2)[1].Replace("\r\n", "");
        Assert.Equal("Confirm: https://x.test/c?t=1", Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
    }

    [Fact]
    public async Task Gmail_4xx_throws_without_leaking_secrets()
    {
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => MakeGmail(new GmailHandler(HttpStatusCode.Forbidden), "refresh-2").Send("kai@a.com", "Hi", "secret-link"));
        Assert.Contains("403", ex.Message);
        foreach (var secret in new[] { "refresh-2", "csecret", "ya29.test", "secret-link" })
            Assert.DoesNotContain(secret, ex.Message);
    }

    [Theory]
    [InlineData("kai@a.com\r\nBcc: x@y.com", "Hi")]
    [InlineData("kai@a.com", "Hi\nBcc: x@y.com")]
    public async Task Gmail_rejects_header_injection(string to, string subject)
    {
        var handler = new GmailHandler(HttpStatusCode.OK);
        await Assert.ThrowsAsync<ArgumentException>(() => MakeGmail(handler, "refresh-3").Send(to, subject, "b"));
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Resend_is_used_when_no_gmail_refresh_token()
    {
        var handler = new FakeHandler(HttpStatusCode.OK);
        await Make(handler).Send("kai@a.com", "Hi", "b");
        Assert.Equal("https://api.resend.com/emails", handler.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Send_throws_on_4xx_without_leaking_key_or_body()
    {
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => Make(new FakeHandler(HttpStatusCode.Forbidden)).Send("kai@a.com", "Hi", "secret-link"));
        Assert.Contains("403", ex.Message);
        Assert.DoesNotContain("re_test_key", ex.Message);
        Assert.DoesNotContain("secret-link", ex.Message);
    }
}
