using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Tutan.Messages.Editor
{
    public static class PathUtils
    {
        // Resolves a sibling asset (same base name as this script) to a project-relative
        // path, derived from this source file's location so it is independent of where the
        // package is mounted. The .uxml/.uss live next to this .cs with a matching name.
        public static string RelativePath(string extension, [CallerFilePath] string sourceFilePath = "")
        {
            var path = sourceFilePath.Replace('\\', '/');
            var assetPath = Path.ChangeExtension(path, extension);

            foreach (var root in new[] { "/Packages/", "/Assets/" })
            {
                int i = assetPath.IndexOf(root, StringComparison.Ordinal);
                if (i >= 0) return assetPath.Substring(i + 1);
            }

            return assetPath;
        }
    }
}
