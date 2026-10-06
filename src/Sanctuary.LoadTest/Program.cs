using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Sanctuary.LoadTest;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
{
    Console.WriteLine(LoadTestOptions.Usage);
    return args.Length == 0 ? 1 : 0;
}

using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

try
{
    return args[0] switch
    {
        "run" => await new LoadRunner(LoadTestOptions.Parse(args[1..])).RunAsync(cancellation.Token),
        "cpu" => await MonitorCpuAsync(args[1..], cancellation.Token),
        _ => throw new ArgumentException($"Unknown command {args[0]}. Use run or cpu.")
    };
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine("Run with --help for usage.");
    return 2;
}

// Prints the Gateway's CPU every few seconds, for when the bots run on another machine.
static async Task<int> MonitorCpuAsync(string[] args, CancellationToken cancellationToken)
{
    if (!ProcessCpuSampler.IsSupported)
        throw new ArgumentException("cpu needs Linux's /proc.");

    var pidOption = "auto";
    var interval = 5;
    var port = 20260;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--pid" when i + 1 < args.Length: pidOption = args[++i]; break;
            case "--interval" when i + 1 < args.Length: interval = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
            case "--port" when i + 1 < args.Length: port = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
            default: throw new ArgumentException($"Unknown option {args[i]}.");
        }
    }

    var pid = pidOption == "auto"
        ? ProcessCpuSampler.FindProcess("Sanctuary.Gateway") ?? throw new ArgumentException("No Sanctuary.Gateway process found; pass --pid.")
        : int.Parse(pidOption, CultureInfo.InvariantCulture);

    var sampler = new ProcessCpuSampler(pid);

    Console.WriteLine($"Sampling process {pid} every {interval} s. CPU is % of one core. Ctrl+C to stop.");
    Console.WriteLine($"time (UTC)   total  busiest thread                      RSS  UDP drops on port {port}");

    var previous = sampler.Take();
    var previousDrops = ProcessCpuSampler.ReadUdpDrops(port);

    try
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);

            var sample = sampler.Take();
            var usage = ProcessCpuSampler.Between(previous, sample);

            var drops = ProcessCpuSampler.ReadUdpDrops(port);

            Console.WriteLine($"{DateTimeOffset.UtcNow:HH:mm:ss}  {usage.TotalPercent,6:F1}%  {usage.BusiestThreadPercent,5:F1}% {usage.BusiestThreadName,-24}  {usage.RssKiB / 1024,5} MiB  {drops - previousDrops}");

            previous = sample;
            previousDrops = drops;
        }
    }
    catch (OperationCanceledException)
    {
    }

    return 0;
}
