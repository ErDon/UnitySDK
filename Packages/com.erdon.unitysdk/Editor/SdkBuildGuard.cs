using UnityEditor.Build;
using UnityEditor;
using UnityEditor.Build.Reporting;
namespace ErDon.UnitySDK.Editor
{
    // Full runtime assembly and device validation are still incomplete; keep release gates explicit.
    public sealed class SdkBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return -1000; } }
        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = SdkProjectSettings.instance;
            var errors = settings.Validate();
            errors.AddRange(CheckDefines(settings.platform.ToString()));
            errors.AddRange(PlatformDependencyCatalog.CheckPlayerAssemblies(settings.platform.ToString()));
            errors.AddRange(PlatformDependencyCatalog.CheckVendorInstallations(settings.platform.ToString()));
            if (settings.platform == DistributionPlatform.Mock)
                errors.Add("Mock 仅支持 Editor 示例，不能打入 Player。");
            if (errors.Count != 0)
                throw new BuildFailedException("ErDon SDK 构建检查未通过：\n" + string.Join("\n", errors));
        }
        public static System.Collections.Generic.List<string> CheckDefines(string platform)
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            var group = BuildPipeline.GetBuildTargetGroup(target);
            return PlatformIsolationPolicy.CheckPlatformDefines(platform,
                PlayerSettings.GetScriptingDefineSymbolsForGroup(group), target.ToString());
        }
    }
}
