using System;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEditor.PackageManager;

namespace Tutan.Messages.Editor
{
    internal static class PathUtils
    {
        // Resolves a sibling asset (same base name as this script) to a project-relative
        // path, derived from this source file's location so it is independent of where the
        // package is mounted. The .uxml/.uss live next to this .cs with a matching name.
        public static string RelativePath(string extension, [CallerFilePath] string sourceFilePath = "")
        {
            var path = sourceFilePath.Replace('\\', '/');
            var assetPath = Path.ChangeExtension(path, extension);

            // Embedded package or classic Assets install: the physical path already
            // contains the project-relative root.
            foreach (var root in new[] { "/Packages/", "/Assets/" })
            {
                int i = assetPath.IndexOf(root, StringComparison.Ordinal);
                if (i >= 0) return assetPath.Substring(i + 1);
            }

            // PackageCache (registry/tarball) or "add from disk": the physical path has
            // no project-relative segment. Map it onto the package's virtual
            // Packages/<id>/ mount, which AssetDatabase uses regardless of where the
            // package physically lives.
            var info = PackageInfo.FindForAssembly(typeof(PathUtils).Assembly);
            if (info != null)
            {
                var resolved = info.resolvedPath.Replace('\\', '/');
                if (assetPath.StartsWith(resolved, StringComparison.OrdinalIgnoreCase))
                    return info.assetPath + assetPath.Substring(resolved.Length);
            }

            return assetPath;
        }
    }
}
