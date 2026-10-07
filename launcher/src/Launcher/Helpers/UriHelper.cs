using System.Linq;

namespace Launcher.Helpers;

public static class UriHelper
{
    public static string JoinUriPaths(string url, params string[] paths)
    {
        return paths.Aggregate(url, (c, s) => $"{c.TrimEnd('/')}/{s.Replace('\\', '/').Trim('/')}");
    }

    public static string StripLeaf(string url)
    {
        var lastSlashIndex = url.LastIndexOf('/');
        if (lastSlashIndex >= 0)
        {
            return url[..(lastSlashIndex + 1)];
        }
        return url;
    }
}