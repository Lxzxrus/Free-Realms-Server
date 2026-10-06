using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

namespace Sanctuary.LoadTest;

public sealed record StepResult(
    string Layout,
    int Bots,
    int BotsInWorld,
    DateTimeOffset Start,
    DateTimeOffset End,
    ProcessCpuSampler.Usage? GatewayCpu,
    double GatewayPeakPercent,
    double PacketsInPerSecond,
    double PacketsOutPerSecond,
    double KiBInPerSecond,
    double KiBOutPerSecond,
    double MovesSentPerSecond,
    double MovesSeenPerSecond,
    double OthersSeenPerBot,
    long LatencySamples,
    double LatencyP50,
    double LatencyP95,
    double LatencyP99,
    double LatencyMax,
    double BotCpuPercent,
    double BotLoopMaxMs,
    long? GatewayUdpDrops,
    long Errors);

public sealed class LoadRunner
{
    // FabledRealms.json: where new characters appear, and a box around its area definitions.
    private static readonly Vector2 Spawn = new(-1904.883f, 412.6024f);
    private static readonly Vector2 PlayableMin = new(-2800f, -2000f);
    private static readonly Vector2 PlayableMax = new(1800f, 3000f);

    // Homes within 10 units of the spawn point, so clustered bots walk inside a 70-unit circle and all see each
    // other (players see 2 tiles of 64 units around their own). Both layouts walk the same radius, so they send
    // the same number of moves and only the crowding differs.
    private const float ClusterRadius = 10f;
    private const float WalkRadius = 25f;

    private readonly LoadTestOptions _options;
    private readonly MoveTracker _moveTracker = new();
    private readonly RunCounters _counters = new();
    private readonly List<Bot> _bots = [];
    private readonly List<Thread> _workers = [];
    private readonly long[] _workerLoopMaxTicks;

    private volatile bool _stopping;
    private int _active;

    public LoadRunner(LoadTestOptions options)
    {
        _options = options;
        _workerLoopMaxTicks = new long[options.Threads];
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var gateway = ResolveGateway();

        Console.WriteLine($"WebAPI {_options.WebApi}, Login {_options.Host}:{_options.LoginPort}, " +
            $"Gateway {(_options.GatewayAddress ?? "as Login reports it")}.");
        Console.WriteLine(gateway is null
            ? "Not sampling Gateway CPU here. Run `Sanctuary.LoadTest cpu` on the server and match the step times below."
            : $"Sampling Gateway CPU from process {gateway.ProcessId}.");
        Console.WriteLine($"Steps {string.Join(", ", _options.Steps)} bots; layouts {string.Join(", ", _options.Layouts)}; " +
            $"{_options.WarmupSeconds} s warmup and {_options.DurationSeconds} s measured each.");

        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        for (var i = 0; i < _options.Steps[^1]; i++)
            _bots.Add(new Bot(i + 1, _options, serviceProvider, _moveTracker, _counters));

        for (var worker = 0; worker < _options.Threads; worker++)
        {
            var index = worker;
            var thread = new Thread(() => WorkerLoop(index)) { IsBackground = true, Name = $"Bot worker {index}" };
            _workers.Add(thread);
            thread.Start();
        }

        var results = new List<StepResult>();

        using var httpClient = new HttpClient { BaseAddress = new Uri(_options.WebApi), Timeout = TimeSpan.FromSeconds(30) };
        var webApi = new WebApiClient(httpClient);

        try
        {
            foreach (var step in _options.Steps)
            {
                // Zero bots measures the idle server, once.
                if (step == 0)
                {
                    var idle = await MeasureAsync("idle", gateway, cancellationToken);
                    results.Add(idle);

                    Console.WriteLine(FormatProgress(idle));
                    continue;
                }

                await LogInBotsAsync(webApi, step, cancellationToken);

                foreach (var layout in _options.Layouts)
                {
                    ApplyLayout(layout);

                    await Task.Delay(TimeSpan.FromSeconds(_options.WarmupSeconds), cancellationToken);

                    var result = await MeasureAsync(layout.ToString().ToLowerInvariant(), gateway, cancellationToken);
                    results.Add(result);

                    Console.WriteLine(FormatProgress(result));
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Stopped early.");
        }
        finally
        {
            await StopBotsAsync();
        }

        var report = BuildReport(results);

        Console.WriteLine();
        Console.WriteLine(report);

        if (_options.ReportPath is not null)
        {
            await File.WriteAllTextAsync(_options.ReportPath, report, CancellationToken.None);
            Console.WriteLine($"Wrote {_options.ReportPath}.");
        }

        var errors = _counters.Errors;

        Console.WriteLine(errors == 0
            ? "No errors on the bot side. Check the server logs for errors too."
            : $"{errors} error(s) on the bot side: {_counters.LoginFailures} failed logins, {_counters.Disconnects} disconnects, {_counters.CorruptPackets} corrupt packets, {_counters.BotExceptions} bot exceptions.");

        var expected = _options.Steps.Sum(x => x == 0 ? 1 : _options.Layouts.Count);

        return errors == 0 && results.Count == expected ? 0 : 1;
    }

    private ProcessCpuSampler? ResolveGateway()
    {
        if (_options.GatewayPid is null)
            return null;

        if (!ProcessCpuSampler.IsSupported)
        {
            Console.WriteLine("CPU sampling needs Linux's /proc; skipping it.");
            return null;
        }

        if (_options.GatewayPid != "auto")
            return new ProcessCpuSampler(int.Parse(_options.GatewayPid, CultureInfo.InvariantCulture));

        var pid = ProcessCpuSampler.FindProcess("Sanctuary.Gateway");

        return pid is null ? null : new ProcessCpuSampler(pid.Value);
    }

    private void WorkerLoop(int index)
    {
        var bots = _bots.Where((_, i) => i % _options.Threads == index).ToArray();

        while (!_stopping)
        {
            var start = Stopwatch.GetTimestamp();
            var hadData = false;

            foreach (var bot in bots)
            {
                try
                {
                    hadData |= bot.Pump(start);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _counters.BotExceptions);
                    Console.Error.WriteLine($"[{bot.Username}] {ex}");
                }
            }

            var elapsed = Stopwatch.GetTimestamp() - start;

            if (elapsed > Volatile.Read(ref _workerLoopMaxTicks[index]))
                Volatile.Write(ref _workerLoopMaxTicks[index], elapsed);

            if (!hadData)
                Thread.Sleep(1);
        }

        foreach (var bot in bots)
            bot.Post(bot.Stop);

        foreach (var bot in bots)
            bot.Pump(Stopwatch.GetTimestamp());
    }

    private async Task LogInBotsAsync(WebApiClient webApi, int count, CancellationToken cancellationToken)
    {
        if (count <= _active)
            return;

        Console.WriteLine($"Logging in bots {_active + 1} to {count}...");

        var stopwatch = Stopwatch.StartNew();

        using var gate = new SemaphoreSlim(_options.LoginConcurrency);

        var tasks = _bots.Skip(_active).Take(count - _active).Select(async bot =>
        {
            await gate.WaitAsync(cancellationToken);

            try
            {
                string session;

                try
                {
                    session = await webApi.GetSessionAsync(bot.Username, _options.Password!, cancellationToken);
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
                {
                    Interlocked.Increment(ref _counters.LoginFailures);
                    Console.Error.WriteLine($"[{bot.Username}] WebAPI: {ex.Message}");
                    return;
                }

                var home = HomeFor(bot, _options.Layouts[0]);

                bot.Post(() =>
                {
                    bot.SetHome(home.Center, home.Radius);
                    bot.Start(session);
                });

                while (bot.State is not (BotState.InWorld or BotState.Failed))
                    await Task.Delay(50, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);

        _active = count;

        var inWorld = _bots.Take(count).Count(x => x.State == BotState.InWorld);

        Console.WriteLine($"{inWorld} of {count} bots in the world after {stopwatch.Elapsed.TotalSeconds:F1} s.");

        if (inWorld == 0)
            throw new OperationCanceledException("No bot reached the world; see the errors above.");
    }

    private void ApplyLayout(Layout layout)
    {
        foreach (var bot in _bots.Take(_active))
        {
            var home = HomeFor(bot, layout);
            bot.Post(() => bot.SetHome(home.Center, home.Radius));
        }
    }

    private (Vector2 Center, float Radius) HomeFor(Bot bot, Layout layout)
    {
        var random = new Random(HashCode.Combine(_options.Seed, bot.Index, (int)layout));

        if (layout == Layout.Clustered)
        {
            var angle = random.NextDouble() * Math.Tau;
            var distance = ClusterRadius * Math.Sqrt(random.NextDouble());

            return (Spawn + new Vector2((float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance)), WalkRadius);
        }

        var x = PlayableMin.X + random.NextDouble() * (PlayableMax.X - PlayableMin.X);
        var z = PlayableMin.Y + random.NextDouble() * (PlayableMax.Y - PlayableMin.Y);

        return (new Vector2((float)x, (float)z), WalkRadius);
    }

    private async Task<StepResult> MeasureAsync(string layout, ProcessCpuSampler? gateway, CancellationToken cancellationToken)
    {
        var bots = _bots.Take(_active).ToList();

        _moveTracker.Reset();

        foreach (var bot in bots)
            bot.TakeSeenSenders();

        for (var i = 0; i < _workerLoopMaxTicks.Length; i++)
            Volatile.Write(ref _workerLoopMaxTicks[i], 0);

        var counters = _counters.Snapshot();
        var traffic = SumTraffic(bots);
        var botCpu = Process.GetCurrentProcess().TotalProcessorTime;
        var startTime = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        var gatewayPort = GatewayPort();
        var firstDrops = gateway is null ? null : ProcessCpuSampler.ReadUdpDrops(gatewayPort);
        var firstSample = gateway?.Take();
        var lastSample = firstSample;
        var peak = 0.0;

        // Sample every second for the peak; the average comes from the first and last samples.
        for (var second = 0; second < _options.DurationSeconds; second++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            if (gateway is null)
                continue;

            var sample = gateway.Take();
            peak = Math.Max(peak, ProcessCpuSampler.Between(lastSample!, sample).TotalPercent);
            lastSample = sample;
        }

        var seconds = stopwatch.Elapsed.TotalSeconds;
        var endTime = DateTimeOffset.UtcNow;
        var endCounters = _counters.Snapshot();
        var endTraffic = SumTraffic(bots);
        var endBotCpu = Process.GetCurrentProcess().TotalProcessorTime;
        var endDrops = gateway is null ? null : ProcessCpuSampler.ReadUdpDrops(gatewayPort);

        var latency = _moveTracker.Latency;
        var othersSeen = bots.Count == 0 ? 0 : bots.Sum(x => x.TakeSeenSenders()) / (double)bots.Count;
        var loopMaxMs = _workerLoopMaxTicks.Max() * 1000.0 / Stopwatch.Frequency;

        return new StepResult(
            layout,
            bots.Count,
            bots.Count(x => x.State == BotState.InWorld),
            startTime,
            endTime,
            gateway is null ? null : ProcessCpuSampler.Between(firstSample!, lastSample!),
            peak,
            // The bots' sends are the Gateway's receives and the other way round.
            (endTraffic.PacketsSent - traffic.PacketsSent) / seconds,
            (endTraffic.PacketsReceived - traffic.PacketsReceived) / seconds,
            (endTraffic.BytesSent - traffic.BytesSent) / 1024.0 / seconds,
            (endTraffic.BytesReceived - traffic.BytesReceived) / 1024.0 / seconds,
            (endCounters.MovesSent - counters.MovesSent) / seconds,
            (endCounters.MovesSeen - counters.MovesSeen) / seconds,
            othersSeen,
            latency.Count,
            latency.PercentileMs(50),
            latency.PercentileMs(95),
            latency.PercentileMs(99),
            latency.MaxMs,
            (endBotCpu - botCpu).TotalSeconds / seconds * 100,
            loopMaxMs,
            endDrops - firstDrops,
            endCounters.Errors - counters.Errors);
    }

    private int GatewayPort()
    {
        var address = _options.GatewayAddress;
        var colon = address?.LastIndexOf(':') ?? -1;

        return colon > 0 && int.TryParse(address![(colon + 1)..], CultureInfo.InvariantCulture, out var port) ? port : 20260;
    }

    private static (long PacketsSent, long PacketsReceived, long BytesSent, long BytesReceived) SumTraffic(IEnumerable<Bot> bots)
    {
        long packetsSent = 0, packetsReceived = 0, bytesSent = 0, bytesReceived = 0;

        foreach (var bot in bots)
        {
            if (!bot.TryGetGatewayStats(out var stats))
                continue;

            packetsSent += stats.PacketsSent;
            packetsReceived += stats.PacketsReceived;
            bytesSent += stats.BytesSent;
            bytesReceived += stats.BytesReceived;
        }

        return (packetsSent, packetsReceived, bytesSent, bytesReceived);
    }

    private async Task StopBotsAsync()
    {
        _stopping = true;

        foreach (var worker in _workers)
            worker.Join();

        // Give the Gateway a moment to save the characters before the process exits.
        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    private static string FormatProgress(StepResult r)
    {
        var cpu = r.GatewayCpu is null ? "cpu n/a" : $"gateway cpu {r.GatewayCpu.TotalPercent:F1}% (busiest thread {r.GatewayCpu.BusiestThreadPercent:F1}%)";

        return $"{r.Layout,-9} {r.Bots,3} bots ({r.BotsInWorld} in world), {r.Start:HH:mm:ss}-{r.End:HH:mm:ss} UTC: {cpu}, " +
            $"{r.PacketsInPerSecond:F0}/{r.PacketsOutPerSecond:F0} pkt/s in/out, move latency p50 {Ms(r.LatencyP50)} p99 {Ms(r.LatencyP99)} ms, errors {r.Errors}";
    }

    private string BuildReport(List<StepResult> results)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Gateway cost by bot count. {_options.DurationSeconds} s measured per row after {_options.WarmupSeconds} s warmup; " +
            $"bots walk at {_options.MoveHz.ToString(CultureInfo.InvariantCulture)} position updates/s, pause {_options.MeanIdleSeconds.ToString(CultureInfo.InvariantCulture)} s on average, " +
            $"jump every {_options.MeanJumpSeconds.ToString(CultureInfo.InvariantCulture)} s while walking and chat every {_options.MeanChatSeconds.ToString(CultureInfo.InvariantCulture)} s. " +
            "CPU is % of one core. In/out are from the Gateway's side. Gateway UDP drops are datagrams the kernel threw away " +
            "because the Gateway's receive buffer was full. Bot loop max is the longest a bot worker took to go once " +
            "round its bots; if it nears the latency figures, the bots, not the server, are slowing things down.");
        sb.AppendLine();
        sb.AppendLine("| Layout | Bots | Gateway CPU avg | peak | Busiest thread | Packets/s in | out | KiB/s in | out | Moves sent/s | Move copies/s | Others seen per bot | Move latency p50 / p95 / p99 / max (ms) | Samples | Gateway RSS (MiB) | Gateway UDP drops | Bot CPU | Bot loop max (ms) | Errors |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|");

        foreach (var r in results)
        {
            var cpu = r.GatewayCpu;

            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {r.Layout} | {r.Bots} | {Pct(cpu?.TotalPercent)} | {(cpu is null ? "n/a" : Pct(r.GatewayPeakPercent))} | {Pct(cpu?.BusiestThreadPercent)} " +
                $"| {r.PacketsInPerSecond:F0} | {r.PacketsOutPerSecond:F0} | {r.KiBInPerSecond:F1} | {r.KiBOutPerSecond:F1} " +
                $"| {r.MovesSentPerSecond:F0} | {r.MovesSeenPerSecond:F0} | {r.OthersSeenPerBot:F1} " +
                $"| {Ms(r.LatencyP50)} / {Ms(r.LatencyP95)} / {Ms(r.LatencyP99)} / {Ms(r.LatencyMax)} | {r.LatencySamples} " +
                $"| {(cpu is null ? "n/a" : (cpu.RssKiB / 1024.0).ToString("F0", CultureInfo.InvariantCulture))} | {r.GatewayUdpDrops?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} | {Pct(r.BotCpuPercent)} | {r.BotLoopMaxMs:F1} | {r.Errors} |"));
        }

        var busiest = results.Select(x => x.GatewayCpu?.BusiestThreadName).Where(x => x is not null).Distinct().ToList();

        if (busiest.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Busiest Gateway thread: {string.Join(", ", busiest)}.");
        }

        return sb.ToString();
    }

    private static string Pct(double? value) => value is null ? "n/a" : value.Value.ToString("F1", CultureInfo.InvariantCulture) + "%";

    private static string Ms(double value) => double.IsNaN(value) ? "-" : value.ToString("F1", CultureInfo.InvariantCulture);
}
