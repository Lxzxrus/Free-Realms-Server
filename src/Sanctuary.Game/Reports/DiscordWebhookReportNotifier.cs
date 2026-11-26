using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace Sanctuary.Game.Reports;

/// <summary>
/// Posts each new report to a Discord webhook. The game never waits for Discord: <see cref="Notify"/> only puts the report
/// in a bounded queue (a full queue drops the report with a warning), and <see cref="RunAsync"/>, run by a hosted
/// service, posts them one at a time, at most one every <see cref="DefaultMinInterval"/> (Discord allows a webhook about
/// 30 messages a minute). A post is tried up to three times: a 429 waits the time Discord asks (up to
/// <see cref="MaxRetryWait"/>, since nothing else waits on this queue), a server error or timeout is tried once more
/// after a short while, anything else is given up. The address is a secret and is never logged.
/// </summary>
public sealed class DiscordWebhookReportNotifier : IReportNotifier
{
    public const int QueueCapacity = 100;

    /// <summary>The longest a 429's Retry-After is obeyed; a longer one gives the report up.</summary>
    public static readonly TimeSpan MaxRetryWait = TimeSpan.FromMinutes(5);

    /// <summary>The least time between two posts: 30 a minute, Discord's own limit for a webhook.</summary>
    public static readonly TimeSpan DefaultMinInterval = TimeSpan.FromSeconds(2);

    private const int MaxAttempts = 3;

    private static readonly TimeSpan ErrorRetryWait = TimeSpan.FromSeconds(2);

    private readonly Uri? _webhook;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _minInterval;
    private readonly Channel<ReportNotification> _queue = Channel.CreateBounded<ReportNotification>(
        new BoundedChannelOptions(QueueCapacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    /// <param name="webhook">The validated address, or null for off.</param>
    /// <param name="delay">How to wait; a test passes its own. Default is <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
    /// <param name="minInterval">The least time between two posts, so a burst stays under Discord's own limit.</param>
    public DiscordWebhookReportNotifier(Uri? webhook, HttpClient httpClient, ILogger logger,
        Func<TimeSpan, CancellationToken, Task>? delay = null, TimeSpan? minInterval = null)
    {
        _webhook = webhook;
        _httpClient = httpClient;
        _logger = logger;
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
        _minInterval = minInterval ?? DefaultMinInterval;
    }

    /// <summary>
    /// Builds the notifier from the setting. An address that is not a Discord webhook is logged as an error (without the
    /// address) and leaves the webhook off.
    /// </summary>
    public static DiscordWebhookReportNotifier Create(string? url, HttpClient httpClient, ILogger logger)
    {
        var status = DiscordWebhookUrl.Validate(url, out var uri);

        switch (status)
        {
            case WebhookUrlStatus.Valid:
                logger.LogInformation("Reports are posted to a Discord webhook.");
                break;
            case WebhookUrlStatus.Invalid:
                logger.LogError("Reports:DiscordWebhookUrl is not a Discord webhook address (https, discord.com or discordapp.com, /api/webhooks/...). Reports are not posted to Discord.");
                break;
        }

        return new DiscordWebhookReportNotifier(uri, httpClient, logger);
    }

    public bool IsEnabled => _webhook is not null;

    public void Notify(ReportNotification notification)
    {
        if (_webhook is null)
            return;

        if (!_queue.Writer.TryWrite(notification))
            _logger.LogWarning("The Discord report queue is full; report {Id} was not posted.", notification.Id);
    }

    /// <summary>Posts queued reports until cancelled. Never throws except for the cancellation.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_webhook is null)
            return;

        try
        {
            await foreach (var notification in _queue.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await SendAsync(notification, cancellationToken);
                    await _delay(_minInterval, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning("Posting report {Id} to Discord failed: {Type}.", notification.Id, exception.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }

    /// <summary>Posts one report: a rate limit is waited out and retried, anything else gets one retry. Returns whether Discord took it.</summary>
    internal async Task<bool> SendAsync(ReportNotification notification, CancellationToken cancellationToken)
    {
        if (_webhook is null)
            return false;

        var json = DiscordPayload.Build(notification);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            TimeSpan wait;
            var rateLimited = false;

            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(_webhook, content, cancellationToken);

                if (response.IsSuccessStatusCode)
                    return true;

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var asked = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);

                    if (asked > MaxRetryWait)
                    {
                        _logger.LogWarning("Discord asked to wait {Seconds} s; report {Id} was not posted.", (int)asked.TotalSeconds, notification.Id);
                        return false;
                    }

                    wait = asked;
                    rateLimited = true;
                }
                else if ((int)response.StatusCode >= 500)
                {
                    wait = ErrorRetryWait;
                }
                else
                {
                    // A rejected address or payload will not get better by asking again.
                    _logger.LogWarning("Discord refused report {Id}: status {Status}.", notification.Id, (int)response.StatusCode);
                    return false;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                // A timeout (the client's 5 s) or a network error; the message is left out, it could name the address.
                wait = ErrorRetryWait;
                _logger.LogDebug("Posting report {Id} to Discord failed: {Type}.", notification.Id, exception.GetType().Name);
            }

            // Only a rate limit is worth more than one retry: it says exactly when to come back.
            if (attempt == MaxAttempts || (attempt == 2 && !rateLimited))
                break;

            await _delay(wait, cancellationToken);
        }

        _logger.LogWarning("Report {Id} was not posted to Discord after retrying.", notification.Id);
        return false;
    }
}
