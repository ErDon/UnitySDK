using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace ErDon.UnitySDK.Editor
{
    // Pure policy: no Unity API, so CI can test the same rules used by the editor.
    [Serializable]
    public sealed class PlatformDependency
    {
        public string Platform;
        public string Package;
        public string Version;
        public string[] Assemblies = new string[0];
        public string[] AssetRoots = new string[0];
    }
    public static class PlatformIsolationPolicy
    {
        public static List<string> ValidateManifest(PlatformDependency dependency)
        {
            var errors = new List<string>();
            if (dependency == null) { errors.Add("Dependency is null"); return errors; }
            if (string.IsNullOrWhiteSpace(dependency.Platform)) errors.Add("Missing platform");
            if (string.IsNullOrWhiteSpace(dependency.Package)) errors.Add("Missing package");
            if (string.IsNullOrWhiteSpace(dependency.Version) || dependency.Version.Contains("*") || dependency.Version.Contains("latest")) errors.Add("Dependency must have a pinned version");
            if (dependency.Assemblies == null || dependency.AssetRoots == null) { errors.Add("Manifest collections must not be null"); return errors; }
            foreach (var root in dependency.AssetRoots) {
                string path = Normalize(root);
                if (string.IsNullOrEmpty(path) || path.Split('/').Any(x=>x=="..") || path.Contains(":")) errors.Add("Invalid asset root: " + path);
                else if (!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Packages/", StringComparison.Ordinal)) errors.Add("Asset root must be inside Assets or Packages: " + path);
            }
            foreach (var assembly in dependency.Assemblies)
                if (string.IsNullOrWhiteSpace(assembly) || assembly.Contains("/") || assembly.Contains("\\")) errors.Add("Invalid assembly name");
            return errors;
        }
        public static List<string> CheckIncluded(string target, IEnumerable<PlatformDependency> dependencies,
            IEnumerable<string> assemblyNames, IEnumerable<string> sourceAssetPaths)
        {
            var errors = new List<string>();
            var assemblies = new HashSet<string>(assemblyNames.Select(x=>AssemblyName(x)), StringComparer.OrdinalIgnoreCase);
            var assets = sourceAssetPaths.Select(Normalize).ToArray();
            foreach (var dependency in dependencies) {
                var invalid = ValidateManifest(dependency);
                errors.AddRange(invalid);
                if (invalid.Count != 0 || dependency.Platform == target) continue;
                foreach (var assembly in dependency.Assemblies)
                    if (assemblies.Contains(AssemblyName(assembly))) errors.Add("Foreign platform assembly: " + dependency.Platform + "/" + assembly);
                foreach (var root in dependency.AssetRoots) {
                    var prefix = Normalize(root).TrimEnd('/');
                    if (assets.Any(x=>x.Equals(prefix,StringComparison.OrdinalIgnoreCase) || x.StartsWith(prefix+"/",StringComparison.OrdinalIgnoreCase)))
                        errors.Add("Foreign platform asset: " + dependency.Platform + "/" + prefix);
                }
            }
            return errors;
        }
        static string AssemblyName(string value)
        {
            var name = Path.GetFileName(value);
            return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(name) : name;
        }
        static string Normalize(string path) { return (path ?? "").Replace('\\','/').TrimEnd('/'); }
    }
}
