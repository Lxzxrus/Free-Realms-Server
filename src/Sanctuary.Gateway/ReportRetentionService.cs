using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;

using Sanctuary.Game.Reports;

namespace Sanctuary.Gateway;

/// <summary>Deletes old closed reports once an hour (and once at startup), so the Reports table stays small.</summary>
public sealed class ReportRetentionService : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(1);

    private readonly ReportService _reportService;

    public ReportRetentionService(ReportService reportService)
    {
        _reportService = reportService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);

        try
        {
            do
            {
                // A short database call, never throws; run off the host's startup thread.
                await Task.Run(_reportService.PruneClosed, stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }
}
