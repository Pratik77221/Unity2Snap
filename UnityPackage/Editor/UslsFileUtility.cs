using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Unity2Snap.Editor
{
    internal static class UslsFileUtility
    {
        public static string DefaultOutputDirectory
        {
            get
            {
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.Combine(projectRoot, "Unity2SnapExport");
            }
        }

        public static string ToProjectRelativePath(string absolutePath)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var fullPath = Path.GetFullPath(absolutePath);

            if (!fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Replace('\\', '/');
            }

            var relative = fullPath.Substring(projectRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relative.Replace('\\', '/');
        }

        public static string CombineRelative(params string[] parts)
        {
            return Path.Combine(parts).Replace('\\', '/');
        }

        public static string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "unnamed";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                var valid = Array.IndexOf(invalid, c) < 0 && !char.IsControl(c);
                builder.Append(valid ? c : '_');
            }

            var sanitized = builder.ToString().Trim();
            return string.IsNullOrEmpty(sanitized) ? "unnamed" : sanitized;
        }

        public static string StableId(string prefix, string seed)
        {
            return prefix + "_" + Fnva64(seed).ToString("x16");
        }

        private static ulong Fnva64(string value)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            var hash = offset;
            if (value == null)
            {
                return hash;
            }

            for (var i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= prime;
            }

            return hash;
        }
    }
}
