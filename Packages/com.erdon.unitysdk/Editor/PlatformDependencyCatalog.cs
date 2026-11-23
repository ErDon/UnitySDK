using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEngine;
namespace ErDon.UnitySDK.Editor
{
    public static class PlatformDependencyCatalog
    {
        public static List<string> CheckVendorInstallations(string target)
        {
            return PlatformIsolationPolicy.CheckVendorInstallations(target,
                PlatformIsolationPolicy.KnownVendorRoots.Where(AssetDatabase.IsValidFolder));
        }
        public static PlatformDependency[] ReadInstalled()
        {
            var entries = new List<PlatformDependency>();
            foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()) {
                var file = Path.Combine(package.resolvedPath, "erdon-platform.json");
                if (!File.Exists(file)) continue;
                PlatformDependency entry;
                try { entry = JsonUtility.FromJson<PlatformDependency>(File.ReadAllText(file)); }
                catch (Exception) { throw new BuildFailedException("Invalid platform manifest: " + package.name); }
                var errors = PlatformIsolationPolicy.ValidateManifest(entry);
                if (entry != null && (entry.Package != package.name || entry.Version != package.version)) errors.Add("Installed package differs from pinned manifest: " + package.name);
                if (errors.Count != 0) throw new BuildFailedException(string.Join("\n",errors));
                entries.Add(entry);
            }
            return entries.ToArray();
        }
        public static List<string> CheckPlayerAssemblies(string target)
        {
            return CheckPlayerAssemblies(target, ReadInstalled());
        }
        public static List<string> CheckPlayerAssemblies(string target, PlatformDependency[] dependencies)
        {
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player);
            // Precompiled vendor DLLs do not appear as source assemblies in GetAssemblies.
            // Inspect references as well, even when a vendor package has no asmdef.
            var names = assemblies.Select(x=>x.name).Concat(assemblies.SelectMany(x=>x.compiledAssemblyReferences));
            return PlatformIsolationPolicy.CheckIncluded(target, dependencies, names, new string[0]);
        }
    }
    public sealed class SdkArtifactAudit : IPostprocessBuildWithReport
    {
        public int callbackOrder { get { return int.MaxValue; } }
        public void OnPostprocessBuild(BuildReport report)
        {
            var target = SdkProjectSettings.instance.platform.ToString();
            var dependencies = PlatformDependencyCatalog.ReadInstalled();
            var errors = PlatformIsolationPolicy.CheckIncluded(target, dependencies,
                report.GetFiles().Select(x=>x.path),
                report.packedAssets.SelectMany(x=>x.contents).Select(x=>x.sourceAssetPath));
            errors.AddRange(PlatformDependencyCatalog.CheckPlayerAssemblies(target));
            errors.AddRange(PlatformDependencyCatalog.CheckVendorInstallations(target));
            Directory.CreateDirectory("Library/ErDonSdk");
            File.WriteAllText("Library/ErDonSdk/last-artifact-audit.txt",
                "Target="+target+"\nScope=registered assemblies and BuildReport source assets only\n"+
                "Not a complete native/JS payload inspection\n"+string.Join("\n",errors));
            if (errors.Count != 0) throw new BuildFailedException(string.Join("\n",errors));
        }
    }
}

