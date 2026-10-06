using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Sanctuary.LoadTest;

/// <summary>
/// Reads a process's CPU time, total and per thread, from Linux's /proc. Percentages are of one core, so a
/// process using two cores fully reads 200%.
/// </summary>
public sealed class ProcessCpuSampler
{
    // USER_HZ. It is 100 on every mainstream Linux build (getconf CLK_TCK).
    private const double TicksPerSecond = 100.0;

    public int ProcessId { get; }

    public ProcessCpuSampler(int processId)
    {
        ProcessId = processId;
    }

    public sealed record Sample(long Timestamp, long TotalTicks, Dictionary<int, (string Name, long Ticks)> Threads, long RssKiB);

    public sealed record Usage(double TotalPercent, double BusiestThreadPercent, string BusiestThreadName, long RssKiB);

    public static bool IsSupported => OperatingSystem.IsLinux();

    public Sample Take()
    {
        var timestamp = Stopwatch.GetTimestamp();
        var total = ReadTicks($"/proc/{ProcessId}/stat") ?? throw new InvalidOperationException($"Process {ProcessId} is gone.");

        var threads = new Dictionary<int, (string, long)>();

        foreach (var taskDirectory in Directory.EnumerateDirectories($"/proc/{ProcessId}/task"))
        {
            if (!int.TryParse(Path.GetFileName(taskDirectory), out var threadId))
                continue;

            var ticks = ReadTicks(Path.Combine(taskDirectory, "stat"));

            if (ticks is null)
                continue; // the thread ended between listing and reading

            string name;

            try
            {
                name = File.ReadAllText(Path.Combine(taskDirectory, "comm")).Trim();
            }
            catch (IOException)
            {
                continue;
            }

            threads[threadId] = (name, ticks.Value);
        }

        return new Sample(timestamp, total, threads, ReadRssKiB());
    }

    public static Usage Between(Sample start, Sample end)
    {
        var seconds = (end.Timestamp - start.Timestamp) / (double)Stopwatch.Frequency;

        if (seconds <= 0)
            return new Usage(0, 0, "-", end.RssKiB);

        var busiestTicks = 0L;
        var busiestName = "-";

        foreach (var (threadId, (name, ticks)) in end.Threads)
        {
            // A thread that started inside the window counts from zero.
            var used = ticks - (start.Threads.TryGetValue(threadId, out var before) ? before.Ticks : 0);

            if (used > busiestTicks)
            {
                busiestTicks = used;
                busiestName = $"{name} ({threadId})";
            }
        }

        return new Usage(
            (end.TotalTicks - start.TotalTicks) / TicksPerSecond / seconds * 100,
            busiestTicks / TicksPerSecond / seconds * 100,
            busiestName,
            end.RssKiB);
    }

    /// <summary>
    /// Finds a running server such as Sanctuary.Gateway, started either as its own executable or as
    /// <c>dotnet Sanctuary.Gateway.dll</c>. A <c>dotnet run</c> wrapper doesn't match; its child does.
    /// </summary>
    public static int? FindProcess(string name)
    {
        var self = Environment.ProcessId;

        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var pid) || pid == self)
                continue;

            try
            {
                var commandLine = File.ReadAllText(Path.Combine(directory, "cmdline")).Split('\0');

                if (Path.GetFileName(commandLine[0]) == name || commandLine.Any(x => Path.GetFileName(x) == $"{name}.dll"))
                    return pid;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    /// <summary>
    /// Datagrams the kernel dropped because a UDP socket on this port had a full receive buffer, from
    /// /proc/net/udp. Null if no socket is bound to the port.
    /// </summary>
    public static long? ReadUdpDrops(int port)
    {
        var hexPort = port.ToString("X4", CultureInfo.InvariantCulture);

        try
        {
            foreach (var line in File.ReadLines("/proc/net/udp").Skip(1))
            {
                // sl local_address rem_address st tx_queue:rx_queue tr:tm->when retrnsmt uid timeout inode ref pointer drops
                var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (fields.Length >= 13 && fields[1].EndsWith($":{hexPort}", StringComparison.Ordinal))
                    return long.Parse(fields[^1], CultureInfo.InvariantCulture);
            }
        }
        catch (IOException)
        {
        }

        return null;
    }

    private static long? ReadTicks(string statPath)
    {
        string stat;

        try
        {
            stat = File.ReadAllText(statPath);
        }
        catch (IOException)
        {
            return null;
        }

        // The command name sits in parentheses and may contain spaces; fields after it are space separated.
        // utime and stime are fields 14 and 15, which are 12 and 13 after the closing parenthesis.
        var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');

        return long.Parse(fields[11], CultureInfo.InvariantCulture) + long.Parse(fields[12], CultureInfo.InvariantCulture);
    }

    private long ReadRssKiB()
    {
        try
        {
            foreach (var line in File.ReadLines($"/proc/{ProcessId}/status"))
            {
                if (line.StartsWith("VmRSS:", StringComparison.Ordinal))
                    return long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture);
            }
        }
        catch (IOException)
        {
        }

        return 0;
    }
}
