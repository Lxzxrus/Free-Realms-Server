using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Database.Entities;
using Sanctuary.Game.Reports;

namespace Sanctuary.Game.Tests;

/// <summary>The Discord webhook: the address check, the message, and the queue and retries behind it.</summary>
[TestClass]
public sealed class DiscordReportTests
{
    private const string GoodUrl = "https://discord.com/api/webhooks/123456/secret-token";

    private static ReportNotification Notification(string reporter = "Rita Reporter", string? target = "Bob Smith", string client = "he said hi", string server = "saw hi",
        ReportKind kind = ReportKind.Player, string summary = "") =>
        new(7, kind, reporter, target, summary, "Wugachug", "1.0 2.0 3.0", client, server);

    // ---- The address ----

    [TestMethod]
    [DataRow("https://discord.com/api/webhooks/1/abc")]
    [DataRow("https://discordapp.com/api/webhooks/1/abc")]
    [DataRow("HTTPS://DISCORD.COM/api/webhooks/1/abc")]
    [DataRow("  https://discord.com/api/webhooks/1/abc  ")]
    [DataRow("https://discord.com/api/webhooks/1/abc?wait=true")]
    public void Url_AcceptsADiscordWebhook(string url)
    {
        Assert.AreEqual(WebhookUrlStatus.Valid, DiscordWebhookUrl.Validate(url, out var uri));
        Assert.IsNotNull(uri);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Url_EmptyMeansOff(string? url)
    {
        Assert.AreEqual(WebhookUrlStatus.Empty, DiscordWebhookUrl.Validate(url, out var uri));
        Assert.IsNull(uri);
    }

    [TestMethod]
    [DataRow("http://discord.com/api/webhooks/1/abc")]
    [DataRow("https://evil.example/api/webhooks/1/abc")]
    [DataRow("https://discord.com.evil.example/api/webhooks/1/abc")]
    [DataRow("https://evildiscord.com/api/webhooks/1/abc")]
    [DataRow("https://cdn.discord.com/api/webhooks/1/abc")]
    [DataRow("https://discord.com@evil.example/api/webhooks/1/abc")]
    [DataRow("https://user:pass@discord.com/api/webhooks/1/abc")]
    [DataRow("https://discord.com:8443/api/webhooks/1/abc")]
    [DataRow("https://discord.com/api/webhooks/")]
    [DataRow("https://discord.com/api/webhooks")]
    [DataRow("https://discord.com/other/api/webhooks/1/abc")]
    [DataRow("https://discord.com/api/v10/channels/1/messages")]
    [DataRow("ftp://discord.com/api/webhooks/1/abc")]
    [DataRow("discord.com/api/webhooks/1/abc")]
    [DataRow("not a url")]
    public void Url_RefusesAnythingElse(string url)
    {
        Assert.AreEqual(WebhookUrlStatus.Invalid, DiscordWebhookUrl.Validate(url, out var uri));
        Assert.IsNull(uri);
    }

    // ---- The message ----

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [TestMethod]
    public void Payload_SaysNoMentionsMayPing()
    {
        var root = Parse(DiscordPayload.Build(Notification(client: "@everyone @here <@123> <@&456>")));

        var allowed = root.GetProperty("allowed_mentions");

        Assert.AreEqual(0, allowed.GetProperty("parse").GetArrayLength());
        Assert.IsFalse(allowed.TryGetProperty("users", out _));
        Assert.IsFalse(allowed.TryGetProperty("roles", out _));
    }

    [TestMethod]
    public void Payload_SuppressesEmbeds()
    {
        Assert.AreEqual(4, Parse(DiscordPayload.Build(Notification())).GetProperty("flags").GetInt32());
    }

    /// <summary>What Discord would render as markdown: the message with every code block and code span taken out.</summary>
    private static string OutsideCode(string content)
    {
        var withoutBlocks = Regex.Replace(content, "```.*?```", string.Empty, RegexOptions.Singleline);

        return Regex.Replace(withoutBlocks, "`[^`\n]*`", string.Empty);
    }

    private static readonly string[] Hostile =
    [
        "@everyone", "@here", "<@123456>", "<@&99>", "<#5>",
        "https://evil.example/x", "staff-help.evil-example.com/fix", "www.evil.example", "evil.example", "discord.gg/abc123", "discord.com/invite/abc123",
        "[click here](http://evil.example)", "**bold** _it_ ~~x~~ ||spoiler|| > quote # head :smile:"
    ];

    [TestMethod]
    public void Payload_PutsEveryPieceOfPlayerTextInsideCode_SoNoLinkInviteOrMentionIsLive()
    {
        foreach (var hostile in Hostile)
        {
            var notifications = new[]
            {
                Notification(reporter: hostile),
                Notification(target: hostile),
                Notification(summary: hostile),
                Notification(client: hostile),
                Notification(server: hostile),
                Notification(kind: ReportKind.Bug, target: null, summary: hostile, client: hostile, server: hostile) with { Zone = hostile, Position = hostile }
            };

            foreach (var notification in notifications)
            {
                var content = DiscordPayload.BuildContent(notification);

                StringAssert.Contains(content, hostile, "the text is still there for staff to read");

                var live = OutsideCode(content);

                foreach (var part in new[] { "evil", "discord", "@", "<", "[", "**bold", "||" })
                    Assert.IsFalse(live.Contains(part), $"\"{part}\" is live outside code for \"{hostile}\":\n{content}");
            }
        }
    }

    [TestMethod]
    public void Payload_ABacktickCannotLetPlayerTextOutOfItsBlock()
    {
        var attacks = new[]
        {
            "``` then see staff-help.evil-example.com/fix",
            "` and evil.example `",
            "```\nevil.example\n```\ndiscord.gg/x\n```",
            "\u0060\u0060\u0060 @everyone",
            "x`` ``y evil.example"
        };

        foreach (var attack in attacks)
        {
            foreach (var notification in new[] { Notification(reporter: attack), Notification(target: attack), Notification(client: attack), Notification(server: attack), Notification(summary: attack) })
            {
                var content = DiscordPayload.BuildContent(notification);
                var live = OutsideCode(content);

                Assert.IsFalse(live.Contains("evil"), content);
                Assert.IsFalse(live.Contains("discord"), content);
                Assert.IsFalse(live.Contains("@everyone"), content);
                Assert.AreEqual(0, Regex.Matches(content, "```").Count % 2, "every code block is closed:\n" + content);
            }
        }
    }

    [TestMethod]
    public void Payload_ContentStaysUnderTheLimitWhateverIsInIt()
    {
        var big = new string('*', 4000);
        var content = DiscordPayload.BuildContent(Notification(reporter: new string('@', 64), target: new string('_', 64), client: big, server: big, summary: big));

        Assert.IsTrue(content.Length <= DiscordPayload.MaxContentLength, content.Length.ToString());
        Assert.IsTrue(Parse(DiscordPayload.Build(Notification(client: big, server: big)))
            .GetProperty("content").GetString()!.Length <= 2000);

        // Even with every field at a length the database would never hold, the blocks are closed and the end is intact.
        var huge = new string('x', 100_000);
        var worst = DiscordPayload.BuildContent(Notification(reporter: huge, target: huge, client: huge, server: huge, summary: huge) with { Zone = huge, Position = huge });

        Assert.IsTrue(worst.Length <= DiscordPayload.MaxContentLength, worst.Length.ToString());
        Assert.AreEqual(0, Regex.Matches(worst, "```").Count % 2);
        StringAssert.EndsWith(worst, "!mod report 7");
    }

    [TestMethod]
    public void Payload_CutNeverSplitsASurrogatePair()
    {
        foreach (var text in new[] { new string('*', 3000), string.Concat(Enumerable.Repeat("\U0001F600", 1500)) })
        {
            for (var extra = 0; extra < 8; extra++)
            {
                var content = DiscordPayload.BuildContent(Notification(client: new string('a', extra) + text, server: string.Empty));
                var cutAt = content.IndexOf("...\n```", StringComparison.Ordinal);

                Assert.IsTrue(cutAt > 0, "the long text was cut");
                Assert.IsFalse(char.IsHighSurrogate(content[cutAt - 1]), "cut inside a surrogate pair");
            }
        }
    }

    [TestMethod]
    public void Payload_ForABugHasTheCategoryAndNoTarget()
    {
        var content = DiscordPayload.BuildContent(Notification(kind: ReportKind.Bug, target: null, summary: "Gameplay", server: string.Empty, client: "Category: Gameplay"));

        StringAssert.StartsWith(content, "**Bug report #7**");
        StringAssert.Contains(content, "Gameplay");
        Assert.IsFalse(content.Contains(" about "));
        StringAssert.Contains(content, "!mod report 7");
    }

    [TestMethod]
    public void Payload_NamesNoAddressesOrSessions()
    {
        var content = DiscordPayload.BuildContent(Notification());

        Assert.IsFalse(content.Contains("session", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(content.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    // ---- The queue and the post ----

    private sealed class FakeHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _next;

        public List<(Uri? Uri, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri, await request.Content!.ReadAsStringAsync(cancellationToken)));

            var respond = responses[Math.Min(_next++, responses.Length - 1)];

            return respond(request);
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    private static HttpResponseMessage TooMany(double seconds)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        return response;
    }

    private static (DiscordWebhookReportNotifier Notifier, List<TimeSpan> Waits) Notifier(FakeHandler handler, ILogger? logger = null)
    {
        var waits = new List<TimeSpan>();

        DiscordWebhookUrl.Validate(GoodUrl, out var uri);

        var notifier = new DiscordWebhookReportNotifier(uri, new HttpClient(handler), logger ?? NullLogger.Instance,
            (span, _) => { waits.Add(span); return Task.CompletedTask; }, TimeSpan.Zero);

        return (notifier, waits);
    }

    [TestMethod]
    public async Task Send_PostsTheJsonToTheWebhook()
    {
        var handler = new FakeHandler(_ => Status(HttpStatusCode.NoContent));
        var (notifier, _) = Notifier(handler);

        Assert.IsTrue(await notifier.SendAsync(Notification(), CancellationToken.None));

        var request = handler.Requests.Single();

        Assert.AreEqual(GoodUrl, request.Uri!.ToString());
        Assert.IsTrue(Parse(request.Body).TryGetProperty("allowed_mentions", out _));
    }

    [TestMethod]
    public async Task Send_ARateLimitIsWaitedForOnceThenRetried()
    {
        var handler = new FakeHandler(_ => TooMany(3), _ => Status(HttpStatusCode.NoContent));
        var (notifier, waits) = Notifier(handler);

        Assert.IsTrue(await notifier.SendAsync(Notification(), CancellationToken.None));
        Assert.AreEqual(2, handler.Requests.Count);
        CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(3) }, waits);
    }

    [TestMethod]
    public async Task Send_ALongRetryAfterIsNotWaitedForAndTheReportIsGivenUp()
    {
        var handler = new FakeHandler(_ => TooMany(600));
        var (notifier, waits) = Notifier(handler);

        Assert.IsFalse(await notifier.SendAsync(Notification(), CancellationToken.None));
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual(0, waits.Count);
    }

    [TestMethod]
    public async Task Send_ARetryAfterAtTheCapIsStillObeyed()
    {
        var handler = new FakeHandler(_ => TooMany(DiscordWebhookReportNotifier.MaxRetryWait.TotalSeconds), _ => Status(HttpStatusCode.NoContent));
        var (notifier, waits) = Notifier(handler);

        Assert.IsTrue(await notifier.SendAsync(Notification(), CancellationToken.None));
        Assert.AreEqual(DiscordWebhookReportNotifier.MaxRetryWait, waits.Single());
    }

    [TestMethod]
    public async Task Send_ServerErrorsAreRetriedOnceAndNoMore()
    {
        var handler = new FakeHandler(_ => Status(HttpStatusCode.BadGateway));
        var (notifier, waits) = Notifier(handler);

        Assert.IsFalse(await notifier.SendAsync(Notification(), CancellationToken.None));
        Assert.AreEqual(2, handler.Requests.Count, "one try and one retry");
        Assert.AreEqual(1, waits.Count);
    }

    [TestMethod]
    public async Task Send_ARefusalOrARedirectIsNotRetried()
    {
        foreach (var code in new[] { HttpStatusCode.BadRequest, HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.Redirect })
        {
            var handler = new FakeHandler(_ => Status(code));
            var (notifier, _) = Notifier(handler);

            Assert.IsFalse(await notifier.SendAsync(Notification(), CancellationToken.None));
            Assert.AreEqual(1, handler.Requests.Count, code.ToString());
        }
    }

    [TestMethod]
    public async Task Send_ATimeoutOrNetworkErrorIsRetriedOnce()
    {
        var calls = 0;
        var handler = new FakeHandler(_ =>
        {
            calls++;
            throw calls == 1 ? new TaskCanceledException("timeout") : new HttpRequestException("network");
        });

        var (notifier, waits) = Notifier(handler);

        Assert.IsFalse(await notifier.SendAsync(Notification(), CancellationToken.None));
        Assert.AreEqual(2, calls);
        Assert.AreEqual(1, waits.Count);
    }

    [TestMethod]
    public async Task Send_NeverLogsTheAddress()
    {
        var logger = new RecordingLogger();
        var (notifier, _) = Notifier(new FakeHandler(_ => Status(HttpStatusCode.BadGateway)), logger);

        await notifier.SendAsync(Notification(), CancellationToken.None);

        Assert.IsTrue(logger.Entries.Count > 0);
        Assert.IsFalse(logger.Entries.Any(entry => entry.Message.Contains("secret-token") || entry.Message.Contains("discord.com")));
    }

    [TestMethod]
    public async Task Notify_WhenOffDoesNothingAndRunReturns()
    {
        var handler = new FakeHandler(_ => Status(HttpStatusCode.NoContent));
        var notifier = DiscordWebhookReportNotifier.Create(string.Empty, new HttpClient(handler), NullLogger.Instance);

        Assert.IsFalse(notifier.IsEnabled);

        notifier.Notify(Notification());
        await notifier.RunAsync(CancellationToken.None);

        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public void Create_LogsAnErrorForABadAddressWithoutTheAddress_AndStaysOff()
    {
        var logger = new RecordingLogger();
        var notifier = DiscordWebhookReportNotifier.Create("https://evil.example/api/webhooks/1/secret-token", new HttpClient(new FakeHandler()), logger);

        Assert.IsFalse(notifier.IsEnabled);
        Assert.AreEqual(1, logger.Entries.Count(entry => entry.Level == LogLevel.Error));
        Assert.IsFalse(logger.Entries.Any(entry => entry.Message.Contains("secret-token") || entry.Message.Contains("evil.example")));
    }

    [TestMethod]
    public void Create_AGoodAddressIsOn()
    {
        Assert.IsTrue(DiscordWebhookReportNotifier.Create(GoodUrl, new HttpClient(new FakeHandler()), NullLogger.Instance).IsEnabled);
    }

    [TestMethod]
    public void Notify_ADeepQueueDropsWithAWarningAndNeverBlocks()
    {
        var logger = new RecordingLogger();
        var (notifier, _) = Notifier(new FakeHandler(_ => Status(HttpStatusCode.NoContent)), logger);

        for (var i = 0; i < DiscordWebhookReportNotifier.QueueCapacity + 5; i++)
            notifier.Notify(Notification());

        Assert.AreEqual(5, logger.Entries.Count(entry => entry.Level == LogLevel.Warning));
    }

    [TestMethod]
    public async Task Run_PostsQueuedReportsInOrderAndStopsWhenCancelled()
    {
        var handler = new FakeHandler(_ => Status(HttpStatusCode.NoContent));
        var (notifier, _) = Notifier(handler);

        for (ulong id = 1; id <= 3; id++)
            notifier.Notify(Notification() with { Id = id });

        using var cancellation = new CancellationTokenSource();
        var run = notifier.RunAsync(cancellation.Token);

        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (handler.Requests.Count < 3 && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        cancellation.Cancel();
        await run;

        Assert.AreEqual(3, handler.Requests.Count);
        CollectionAssert.AreEqual(new[] { "1", "2", "3" }, handler.Requests.Select(request => System.Text.RegularExpressions.Regex.Match(request.Body, @"Report #(\d)").Groups[1].Value).ToList());
    }

    [TestMethod]
    public async Task Run_AFailingPostDoesNotStopTheQueue()
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException("boom"), _ => Status(HttpStatusCode.NoContent));
        var (notifier, _) = Notifier(handler);

        notifier.Notify(Notification() with { Id = 1 });
        notifier.Notify(Notification() with { Id = 2 });

        using var cancellation = new CancellationTokenSource();
        var run = notifier.RunAsync(cancellation.Token);

        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (handler.Requests.Count < 2 && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        cancellation.Cancel();
        await run;

        Assert.AreEqual(2, handler.Requests.Count);
    }
}
