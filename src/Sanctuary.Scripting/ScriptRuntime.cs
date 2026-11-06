using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Lua;
using Lua.Standard;

namespace Sanctuary.Scripting;

public sealed class ScriptRuntime
{
    private readonly LuaState _state;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ScriptRuntime(ILogger logger)
    {
        logger.LogDebug("Initializing scripting...");

        _state = LuaState.Create();

        OpenSafeLibraries(_state);

        _state.ModuleLoader = new ScriptModuleLoader();

        logger.LogInformation("Scripting initialized");
    }

    /// <summary>
    /// Opens only the libraries a script needs to compute: base, table, string and math, plus <c>require</c>
    /// for Scripts/Shared modules and the clock half of <c>os</c> (see below).
    /// <c>io</c>, <c>package</c>, <c>debug</c> and the rest of <c>os</c> stay closed, because they reach the
    /// process, the file system and the interpreter's internals. The base library's
    /// <c>dofile</c> and <c>loadfile</c> read any path on disk, so they are removed too.
    /// Scripts are loaded by the server through <see cref="ExecuteFileAsync"/>, not by Lua.
    /// </summary>
    internal static void OpenSafeLibraries(LuaState state)
    {
        state.OpenBasicLibrary();
        state.OpenTableLibrary();
        state.OpenStringLibrary();
        state.OpenMathLibrary();

        state.Environment["dofile"] = LuaValue.Nil;
        state.Environment["loadfile"] = LuaValue.Nil;

        // Upstream OSFR's event scripts (Bruce's concert, the Halloween dance-off, NPC greetings; merged 2026-10-28)
        // need require and os.date. require stays limited to plain module names in Scripts/Shared
        // (ScriptModuleLoader), and the package table that could reconfigure it stays closed. os keeps only its
        // clock functions: nothing that runs a program, reads the environment or touches a file.
        state.OpenModuleLibrary();
        state.Environment["package"] = LuaValue.Nil;

        state.OpenOperatingSystemLibrary();
        var os = state.Environment["os"].Read<LuaTable>();
        var clockOnly = new LuaTable();

        foreach (var name in new[] { "clock", "date", "difftime", "time" })
            clockOnly[name] = os[name];

        state.Environment["os"] = clockOnly;
    }

    internal LuaTable CreateEnv()
    {
        var env = new LuaTable
        {
            Metatable = new LuaTable()
        };

        env.Metatable["__index"] = _state.Environment; // read access to Lua std lib

        return env;
    }

    public async ValueTask ExecuteFileAsync(string path, LuaTable environment, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var closure = await _state.LoadFileAsync(path, "bt", environment, cancellationToken);
            await _state.ExecuteAsync(closure);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<LuaValue[]> CallAsync(LuaValue function, LuaValue[] args, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await _state.CallAsync(function, args);
        }
        finally
        {
            _gate.Release();
        }
    }
}
