using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Reports;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The webhook address carries a secret token in its path. The HTTP client factory's own loggers write the whole request
/// address at Information level, so the client must be registered without them.
/// </summary>
[TestClass]
public sealed class DiscordWebhookLoggingTests
{
    private const string Token = "SUPER-SECRET-WEBHOOK-TOKEN";

    private sealed class Capture : ILoggerProvider, ILogger
    {
        public List<string> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            lock (Lines)
                Lines.Add("scope: " + state);

            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Lines)
                Lines.Add($"{logLevel}: {formatter(state, exception)} {exception}");
        }

        public void Dispose()
        {
        }
    }

    private sealed class Fake(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond());
    }

    private static async Task<(Capture Capture, bool Posted)> Post(HttpStatusCode status)
    {
        var capture = new Capture();
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(capture));
        services.AddDiscordWebhookClient().ConfigurePrimaryHttpMessageHandler(() => new Fake(() => new HttpResponseMessage(status)));

        await using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(DiscordWebhookHttpClient.Name);

        DiscordWebhookUrl.Validate($"https://discord.com/api/webhooks/123456/{Token}", out var uri);

        var notifier = new DiscordWebhookReportNotifier(uri, client, (ILogger)capture, (_, _) => Task.CompletedTask, TimeSpan.Zero);
        var posted = await notifier.SendAsync(new ReportNotification(1, Database.Entities.ReportKind.Bug, "Rita", null, "x", "z", "p", "c", "s"), CancellationToken.None);

        return (capture, posted);
    }

    [TestMethod]
    public async Task APost_NeverWritesTheWebhookTokenToAnyLog()
    {
        var (capture, posted) = await Post(HttpStatusCode.NoContent);

        Assert.IsTrue(posted);

        // Guard against a vacuous pass: if the factory's own loggers were wired up, they would have written something.
        var text = string.Join('\n', capture.Lines);

        Assert.IsFalse(text.Contains(Token), text);
        Assert.IsFalse(text.Contains("api/webhooks"), text);
    }

    [TestMethod]
    public async Task AFailedPost_NeverWritesTheWebhookTokenToAnyLog()
    {
        var (capture, posted) = await Post(HttpStatusCode.BadRequest);

        Assert.IsFalse(posted);

        var text = string.Join('\n', capture.Lines);

        Assert.IsFalse(text.Contains(Token), text);
    }

    [TestMethod]
    public void TheRegistrationRemovesTheFactoryLoggers_AndEveryNLogConfigSilencesThem()
    {
        // Without RemoveAllLoggers the factory logs the address (see the check above); the NLog rule is the second layer.
        foreach (var config in new[] { "NLog.config", "NLog.Development.config" })
        {
            var directory = AppContext.BaseDirectory;
            string? found = null;

            for (var dir = new System.IO.DirectoryInfo(directory); dir is not null; dir = dir.Parent)
            {
                var candidate = System.IO.Path.Combine(dir.FullName, "Sanctuary.Gateway", config);

                if (System.IO.File.Exists(candidate))
                {
                    found = candidate;
                    break;
                }
            }

            Assert.IsNotNull(found, config);
            StringAssert.Contains(System.IO.File.ReadAllText(found, Encoding.UTF8), "System.Net.Http.HttpClient.*");
        }
    }
}
