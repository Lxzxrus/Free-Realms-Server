using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Lua;

namespace Sanctuary.Scripting;

public sealed class ScriptModuleLoader : ILuaModuleLoader
{
    public static readonly string SharedDirectory = Path.Combine(ScriptManager.BaseDirectory, "Shared");

    public bool Exists(string moduleName) =>
        IsPlainName(moduleName) && File.Exists(GetPath(moduleName));

    public async ValueTask<LuaModule> LoadAsync(string moduleName, CancellationToken cancellationToken = default)
    {
        var text = await File.ReadAllTextAsync(GetPath(moduleName), cancellationToken);

        return new LuaModule(moduleName, text);
    }

    /// <summary>
    /// Evergrove: a module name is a plain file name in Scripts/Shared (letters, digits, '_' and '-'), so require
    /// can't reach another folder on any system. Upstream OSFR refused only the separators, and which characters
    /// separate (a backslash, a drive letter) depends on the system.
    /// </summary>
    internal static bool IsPlainName(string moduleName) =>
        moduleName.Length > 0 && moduleName.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private static string GetPath(string moduleName)
    {
        if (!IsPlainName(moduleName))
            throw new ArgumentException($"\"{moduleName}\" is not a plain module name.", nameof(moduleName));

        return Path.Combine(SharedDirectory, moduleName + ".lua");
    }
}
