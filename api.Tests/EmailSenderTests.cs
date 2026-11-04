using System.Net;
using System.Text.Json;
using api.Services;
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
