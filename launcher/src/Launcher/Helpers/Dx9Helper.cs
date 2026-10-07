using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Launcher.Helpers;

public static partial class Dx9Helper
{
    private static readonly string[] RequiredDlls = ["d3d9.dll", "d3dx9_31.dll"];

    public static bool IsInstalled()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Wine uses wined3d which it ships with, not native DirectX DLLs
                return WineHelper.IsInstalled();
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Unknown platform
                return false;
            }

            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            // The game client is 32-bit, so its DLLs are loaded from SysWOW64 on 64-bit Windows.
            // Since the launcher is 64-bit only, this is the dir we need to check; not System32
            string sysWow64Path = Path.Combine(winDir, "SysWOW64");

            foreach (var requiredDll in RequiredDlls)
            {
                if (!File.Exists(Path.Combine(sysWow64Path, requiredDll)))
                    return false;
            }
        }
        catch
        {
            return false;
        }

        return true;
    }
}