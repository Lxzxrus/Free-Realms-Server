using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;

using Sanctuary.Game.Reports;

namespace Sanctuary.Gateway;

/// <summary>
/// Runs the Discord webhook's queue in the background, so a slow or unreachable Discord never holds up the game. Building
/// the notifier here also checks the configured address at startup (a bad one is logged and the webhook stays off).
/// </summary>
public sealed class ReportNotifierService : BackgroundService
{
    private readonly DiscordWebhookReportNotifier _notifier;

    public ReportNotifierService(DiscordWebhookReportNotifier notifier)
    {
        _notifier = notifier;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => _notifier.RunAsync(stoppingToken);
}
