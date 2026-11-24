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
        // Known installed vendor roots must be checked even when their ErDon adapter is absent.
        public static string[] KnownVendorRoots { get { return new[] {
            "Packages/com.qq.weixin.minigame", "Assets/Plugins/com.tiktok.minigame",
            "Assets/Plugins/ByteGame/com.bytedance.starksdk", "Packages/com.bytedance.starksdk"
        }; } }
        public static List<string> CheckVendorInstallations(string target, IEnumerable<string> installedRoots)
        {
            var errors = new List<string>();
            var roots = new HashSet<string>(installedRoots.Select(Normalize), StringComparer.OrdinalIgnoreCase);
            foreach(var root in KnownVendorRoots) {
                if(!roots.Contains(root))continue;
                // com.bytedance.starksdk is shared by vendor distributions; UPM identity alone is ambiguous.
                if(root == "Packages/com.bytedance.starksdk") {
                    if(target != "Douyin" && target != "TikTok")errors.Add("Foreign ByteDance SDK installation: " + root);
                    continue;
                }
                var platform = root == "Packages/com.qq.weixin.minigame" ? "WeChat" :
                    root == "Assets/Plugins/com.tiktok.minigame" ? "TikTok" : "Douyin";
                if(platform != target)errors.Add("Foreign SDK installation: " + platform + "/" + root);
            }
            return errors;
        }
        public static List<string> CheckPlatformDefines(string target, string defines, string engineTarget)
        {
            var errors = new List<string>();
            var known = new Dictionary<string,string>(StringComparer.Ordinal) {
                {"WeChat", "ERDON_PLATFORM_WECHAT"},
                {"Douyin", "ERDON_PLATFORM_DOUYIN"},
                {"TikTok", "ERDON_PLATFORM_TIKTOK"},
                {"Android", "ERDON_PLATFORM_ANDROID"}
            };
            var selected = new HashSet<string>((defines ?? "").Split(';').Select(x=>x.Trim()).Where(x=>x.StartsWith("ERDON_PLATFORM_",StringComparison.Ordinal)), StringComparer.Ordinal);
            string required;
            if (target == "Mock") {
                if(selected.Count != 0)errors.Add("Editor Mock must not enable a real-platform define");
                return errors;
            }
            if(!known.TryGetValue(target ?? "", out required)) {
                errors.Add("No registered platform define for target: " + target);
                return errors;
            }
            if(!selected.Contains(required))errors.Add("Missing target define: " + required);
            foreach(var symbol in selected)
                if(symbol != required)errors.Add("Foreign or unknown platform define: " + symbol);
            if(engineTarget != (target == "Android" ? "Android" : "WebGL"))errors.Add("Platform does not match engine build target: " + target);
            return errors;
        }
        // Positive check complements foreign-platform isolation: defines alone do not install an adapter.
        public static List<string> CheckTargetAdapter(string target, IEnumerable<PlatformDependency> dependencies,
            IEnumerable<string> playerAssemblyNames)
        {
            var errors = new List<string>();
            string package, assembly;
            switch (target) {
                case "WeChat": package = "com.erdon.unitysdk.wechat"; assembly = "ErDon.UnitySDK.WeChat"; break;
                case "Douyin": package = "com.erdon.unitysdk.douyin"; assembly = "ErDon.UnitySDK.Douyin"; break;
                case "Android": package = "com.erdon.unitysdk.android"; assembly = "ErDon.UnitySDK.Android"; break;
                case "TikTok": package = "com.erdon.unitysdk.tiktok"; assembly = "ErDon.UnitySDK.TikTok"; break;
                default: errors.Add("No Player adapter registered for target: " + target); return errors;
            }
            var matches = dependencies.Where(x => x != null && x.Platform == target && x.Package == package).ToArray();
            if (matches.Length != 1) errors.Add("Exactly one target adapter manifest required: " + package);
            else {
                errors.AddRange(ValidateManifest(matches[0]));
                if (matches[0].Assemblies == null || !matches[0].Assemblies.Contains(assembly))
                    errors.Add("Target manifest must declare adapter assembly: " + assembly);
            }
            // Only source Player assemblies supplied by CompilationPipeline qualify; an Editor-only DLL reference does not.
            if (!playerAssemblyNames.Contains(assembly, StringComparer.Ordinal))
                errors.Add("Target adapter absent from Player compilation: " + assembly);
            return errors;
        }
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
            var name = Path.GetFileName(Normalize(value));
            return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(name) : name;
        }
        static string Normalize(string path) { return (path ?? "").Replace('\\','/').TrimEnd('/'); }
    }
}
