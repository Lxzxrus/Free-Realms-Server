using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Sanctuary.LoadTest;

public enum Layout
{
    Clustered,
    Spread
}

public sealed class LoadTestOptions
{
    public string Host = "127.0.0.1";
    public string? WebApiUrl;
    public int LoginPort = 20042;

    /// <summary>
    /// Overrides the Gateway address the Login server hands out (its gateway.json ServerAddress), for when that
    /// address isn't reachable from the bot's machine.
    /// </summary>
    public string? GatewayAddress;

    public string ClientVersion = "1.910.1.530630";

    public string AccountPrefix = "loadbot";
    public string? Password;

    public List<int> Steps = [10, 25, 50, 100];
    public List<Layout> Layouts = [Layout.Clustered, Layout.Spread];

    public int WarmupSeconds = 15;
    public int DurationSeconds = 60;
    public int Threads = 2;
    public int LoginConcurrency = 4;
    public int Seed = 1;

    // Behaviour. Guesses until a real client is measured; see docs/performance/baseline.md.
    public double MoveHz = 5;
    public float WalkSpeed = 6f;
    public double MeanIdleSeconds = 5;
    public double MeanJumpSeconds = 20;
    public double MeanChatSeconds = 45;

    /// <summary>Gateway process id to sample, "auto" to find it on this machine, or null to skip.</summary>
    public string? GatewayPid = "auto";

    public string? ReportPath;

    public string WebApi => WebApiUrl ?? $"http://{Host}:20040";

    public const string Usage = """
        Sanctuary.LoadTest: logs fake players in through WebAPI, Login and Gateway and measures the Gateway.

        Usage:
          Sanctuary.LoadTest run --password <pw> [options]
          Sanctuary.LoadTest cpu [--pid <pid>|auto] [--interval <s>] [--port <gateway port>]

        run options:
          --host <addr>              Server address (default 127.0.0.1)
          --webapi <url>             WebAPI base URL (default http://<host>:20040)
          --login-port <port>        Login server UDP port (default 20042)
          --gateway-address <a:p>    Use this instead of the Gateway address the Login server hands out
          --client-version <v>       Must match gateway.json ClientVersion (default 1.910.1.530630)
          --password <pw>            Password for the bot accounts (or LOADTEST_PASSWORD). Required
          --account-prefix <p>       Bot accounts are <p>001, <p>002, ... (default loadbot)
          --steps <n,n,...>          Bot counts to measure, in increasing order (default 10,25,50,100).
                                     0 measures the idle server once
          --layouts <l,l>            clustered, spread or both (default clustered,spread)
          --warmup <s>               Seconds to settle before each measurement (default 15)
          --duration <s>             Seconds measured per step and layout (default 60)
          --threads <n>              Bot worker threads (default 2)
          --login-concurrency <n>    Bots logging in at once (default 4)
          --seed <n>                 Random seed, so runs repeat (default 1)
          --move-hz <hz>             Position updates per second while walking (default 5)
          --walk-speed <u/s>         Walking speed in world units per second (default 6)
          --idle <s>                 Mean pause between walks (default 5)
          --jump <s>                 Mean seconds between jumps while walking (default 20)
          --chat <s>                 Mean seconds between chat lines (default 45)
          --gateway-pid <pid|auto|none>  Gateway process to sample CPU from; auto finds it on this machine
          --report <file>            Also write the results table (markdown) to this file

        cpu: run this on the server machine while the bots run elsewhere. It prints the Gateway's CPU and
        the UDP datagrams dropped on its port (default 20260) every interval (default 5 s), so the numbers can
        be lined up with the bot's step times.
        """;

    public static LoadTestOptions Parse(IReadOnlyList<string> args)
    {
        var options = new LoadTestOptions
        {
            Password = Environment.GetEnvironmentVariable("LOADTEST_PASSWORD")
        };

        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i];

            string Value()
            {
                if (i + 1 >= args.Count)
                    throw new ArgumentException($"{name} needs a value.");

                return args[++i];
            }

            switch (name)
            {
                case "--host": options.Host = Value(); break;
                case "--webapi": options.WebApiUrl = Value().TrimEnd('/'); break;
                case "--login-port": options.LoginPort = ParseInt(name, Value()); break;
                case "--gateway-address": options.GatewayAddress = Value(); break;
                case "--client-version": options.ClientVersion = Value(); break;
                case "--password": options.Password = Value(); break;
                case "--account-prefix": options.AccountPrefix = Value(); break;
                case "--steps": options.Steps = Value().Split(',').Select(x => ParseInt(name, x)).ToList(); break;
                case "--layouts": options.Layouts = ParseLayouts(Value()); break;
                case "--warmup": options.WarmupSeconds = ParseInt(name, Value()); break;
                case "--duration": options.DurationSeconds = ParseInt(name, Value()); break;
                case "--threads": options.Threads = ParseInt(name, Value()); break;
                case "--login-concurrency": options.LoginConcurrency = ParseInt(name, Value()); break;
                case "--seed": options.Seed = ParseInt(name, Value()); break;
                case "--move-hz": options.MoveHz = ParseDouble(name, Value()); break;
                case "--walk-speed": options.WalkSpeed = (float)ParseDouble(name, Value()); break;
                case "--idle": options.MeanIdleSeconds = ParseDouble(name, Value()); break;
                case "--jump": options.MeanJumpSeconds = ParseDouble(name, Value()); break;
                case "--chat": options.MeanChatSeconds = ParseDouble(name, Value()); break;
                case "--gateway-pid":
                    var pid = Value();
                    options.GatewayPid = pid == "none" ? null : pid;
                    break;
                case "--report": options.ReportPath = Value(); break;
                default: throw new ArgumentException($"Unknown option {name}.");
            }
        }

        if (string.IsNullOrEmpty(options.Password))
            throw new ArgumentException("Set the bot accounts' password with --password or LOADTEST_PASSWORD.");

        if (options.Steps.Count == 0 || options.Steps.Any(x => x < 0) || !options.Steps.SequenceEqual(options.Steps.Order()))
            throw new ArgumentException("--steps must be in increasing order, and 0 or more.");

        if (options.Steps[^1] > 999)
            throw new ArgumentException("At most 999 bots.");

        if (options.MoveHz <= 0 || options.Threads < 1 || options.LoginConcurrency < 1 || options.DurationSeconds < 1)
            throw new ArgumentException("--move-hz, --threads, --login-concurrency and --duration must be positive.");

        return options;
    }

    private static List<Layout> ParseLayouts(string value)
    {
        if (value == "both")
            return [Layout.Clustered, Layout.Spread];

        return value.Split(',').Select(x => Enum.Parse<Layout>(x, ignoreCase: true)).ToList();
    }

    private static int ParseInt(string name, string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            throw new ArgumentException($"{name}: \"{value}\" isn't a whole number.");

        return result;
    }

    private static double ParseDouble(string name, string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            throw new ArgumentException($"{name}: \"{value}\" isn't a number.");

        return result;
    }
}
