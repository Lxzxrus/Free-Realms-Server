using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Launcher.Helpers;

namespace Launcher.Tests;

/// <summary>
/// The launcher contract in docs/webapi.md.
/// </summary>
[TestClass]
public class WebApiClientTests
{
    private const string BaseUrl = "https://play.example.com";

    private static HttpResponseMessage Json(HttpStatusCode status, string json, string mediaType = "application/json")
        => new(status) { Content = new StringContent(json, Encoding.UTF8, mediaType) };

    private static (HttpClient Client, StubHandler Stub) Client(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var stub = new StubHandler(respond);
        return (new HttpClient(new TransportPolicyHandler(stub)), stub);
    }

    [TestMethod]
    public async Task Login_posts_camel_case_json_to_login()
    {
        var (client, stub) = Client(_ => Json(HttpStatusCode.OK, """{"sessionId":"abc","launchArguments":null}"""));

        await WebApiClient.LoginAsync(client, BaseUrl, "nate", "hunter22");

        Assert.AreEqual(HttpMethod.Post, stub.LastRequest!.Method);
        Assert.AreEqual("https://play.example.com/login", stub.LastRequest.RequestUri!.ToString());
        Assert.AreEqual("""{"username":"nate","password":"hunter22"}""", stub.LastBody);
    }

    [TestMethod]
    public async Task Register_posts_to_register_under_a_base_url_with_a_path()
    {
        var (client, stub) = Client(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await WebApiClient.RegisterAsync(client, "https://play.example.com/api/", "nate", "hunter22");

        Assert.AreEqual(WebApiStatus.Ok, result.Status);
        Assert.AreEqual("https://play.example.com/api/register", stub.LastRequest!.RequestUri!.ToString());
    }

    [TestMethod]
    public async Task Login_success_returns_the_launch_arguments_unchanged()
    {
        const string launchArguments = "AssetDelivery:IndirectServerAddress=http://assets.example.com/assets Portrait:UploadUrl=https://play.example.com/image/0123abcd";

        var (client, _) = Client(_ => Json(HttpStatusCode.OK, $$"""{"sessionId":"0123456789abcdef","launchArguments":"{{launchArguments}}"}"""));

        var result = await WebApiClient.LoginAsync(client, BaseUrl, "nate", "hunter22");

        Assert.AreEqual(WebApiStatus.Ok, result.Status);
        Assert.AreEqual("0123456789abcdef", result.Login!.SessionId);
        Assert.AreEqual(launchArguments, result.Login.LaunchArguments);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("""{"sessionId":""}""")]
    [DataRow("not json")]
    public async Task Login_success_without_a_session_is_unexpected(string body)
    {
        var (client, _) = Client(_ => Json(HttpStatusCode.OK, body));

        var result = await WebApiClient.LoginAsync(client, BaseUrl, "nate", "hunter22");

        Assert.AreEqual(WebApiStatus.Unexpected, result.Status);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.Unauthorized, WebApiStatus.WrongCredentials)]
    [DataRow(HttpStatusCode.Forbidden, WebApiStatus.Banned)]
    [DataRow(HttpStatusCode.InternalServerError, WebApiStatus.ServerDown)]
    [DataRow(HttpStatusCode.BadGateway, WebApiStatus.ServerDown)]
    [DataRow(HttpStatusCode.ServiceUnavailable, WebApiStatus.ServerDown)]
    [DataRow(HttpStatusCode.NotFound, WebApiStatus.Unexpected)]
    [DataRow(HttpStatusCode.Conflict, WebApiStatus.Unexpected)]
    public async Task Login_status_codes(HttpStatusCode status, WebApiStatus expected)
    {
        var (client, _) = Client(_ => new HttpResponseMessage(status));

        var result = await WebApiClient.LoginAsync(client, BaseUrl, "nate", "hunter22");

        Assert.AreEqual(expected, result.Status);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.Conflict, WebApiStatus.NameTaken)]
    [DataRow(HttpStatusCode.Unauthorized, WebApiStatus.Unexpected)]
    [DataRow(HttpStatusCode.Forbidden, WebApiStatus.Unexpected)]
    [DataRow(HttpStatusCode.ServiceUnavailable, WebApiStatus.ServerDown)]
    public async Task Register_status_codes(HttpStatusCode status, WebApiStatus expected)
    {
        var (client, _) = Client(_ => new HttpResponseMessage(status));

        var result = await WebApiClient.RegisterAsync(client, BaseUrl, "nate", "hunter22");

        Assert.AreEqual(expected, result.Status);
    }

    [TestMethod]
    public async Task Too_many_attempts_reads_retry_after_seconds()
    {
        var (client, stub) = Client(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
            return response;
        });

        var result = await WebApiClient.LoginAsync(client, BaseUrl, "nate", "wrong");

        Assert.AreEqual(WebApiStatus.TooManyAttempts, result.Status);
        Assert.AreEqual(TimeSpan.FromSeconds(60), result.RetryAfter);

        // Never retried: every retry would count as another failure.
        Assert.AreEqual(1, stub.Calls);
    }

    [TestMethod]
    public void Retry_after_as_a_date_counts_from_now()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddMinutes(15));

        Assert.AreEqual(TimeSpan.FromMinutes(15), WebApiClient.GetRetryAfter(response, now));
    }

    [TestMethod]
    public async Task Too_many_attempts_without_retry_after()
    {
        var (client, _) = Client(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var result = await WebApiClient.RegisterAsync(client, BaseUrl, "nate", "hunter22");

        Assert.AreEqual(WebApiStatus.TooManyAttempts, result.Status);
        Assert.IsNull(result.RetryAfter);
    }

    [TestMethod]
    public async Task Validation_problem_messages_are_returned()
    {
        const string problem = """
            {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,
             "errors":{"Username":["Username must be between 3 and 50 characters long."],
                       "Password":["Password must be between 6 and 100 characters long.","Password can only contain ASCII characters."]}}
            """;

        var (client, _) = Client(_ => Json(HttpStatusCode.BadRequest, problem, "application/problem+json"));

        var result = await WebApiClient.RegisterAsync(client, BaseUrl, "n", "é");

        Assert.AreEqual(WebApiStatus.Invalid, result.Status);
        CollectionAssert.AreEqual(new[]
        {
            "Username must be between 3 and 50 characters long.",
            "Password must be between 6 and 100 characters long.",
            "Password can only contain ASCII characters."
        }, result.Errors.ToArray());
    }

    [TestMethod]
    [DataRow(HttpStatusCode.BadRequest, "not json")]
    [DataRow(HttpStatusCode.RequestEntityTooLarge, "")]
    public async Task Invalid_without_readable_errors(HttpStatusCode status, string body)
    {
        var (client, _) = Client(_ => Json(status, body));

        var result = await WebApiClient.LoginAsync(client, BaseUrl, "nate", "hunter22");

        Assert.AreEqual(WebApiStatus.Invalid, result.Status);
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public async Task No_answer_is_server_down()
    {
        var (client, _) = Client(_ => throw new HttpRequestException("Connection refused"));

        var result = await WebApiClient.LoginAsync(client, BaseUrl, "nate", "hunter22");

        Assert.AreEqual(WebApiStatus.ServerDown, result.Status);
    }

    [TestMethod]
    public async Task Timeout_is_server_down()
    {
        var (client, _) = Client(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var result = await WebApiClient.LoginAsync(client, BaseUrl, "nate", "hunter22");

        Assert.AreEqual(WebApiStatus.ServerDown, result.Status);
    }

    [TestMethod]
    public async Task Password_is_never_sent_over_plain_http_to_the_internet()
    {
        var (client, stub) = Client(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await WebApiClient.LoginAsync(client, "http://play.example.com", "nate", "hunter22");

        Assert.AreEqual(WebApiStatus.Insecure, result.Status);
        Assert.AreEqual(0, stub.Calls);
    }

    [TestMethod]
    [DataRow(null, "a few minutes")]
    [DataRow(0, "a few minutes")]
    [DataRow(1, "1 second")]
    [DataRow(45, "45 seconds")]
    [DataRow(60, "1 minute")]
    [DataRow(61, "2 minutes")]
    [DataRow(900, "15 minutes")]
    public void Format_wait(int? seconds, string expected)
    {
        TimeSpan? wait = seconds is null ? null : TimeSpan.FromSeconds(seconds.Value);

        Assert.AreEqual(expected, WebApiClient.FormatWait(wait));
    }
}
