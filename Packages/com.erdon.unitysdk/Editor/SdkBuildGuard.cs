using UnityEditor.Build;
using System.Linq;
using UnityEditor.Compilation;
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
            var developmentBuild = report != null && (report.summary.options & BuildOptions.Development) != 0;
            var errors = settings.Validate(developmentBuild);
            // 小游戏使用主循环驱动超时和回调，不能依赖 WebGL worker。
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL && PlayerSettings.WebGL.threadsSupport)
                errors.Add("WEBGL.THREADS_UNSUPPORTED: 请关闭 Player Settings 的 WebGL Threads Support。");
            errors.AddRange(CheckDefines(settings.platform.ToString()));
            errors.AddRange(settings.ValidateCapabilityDefines(PlayerSettings.GetScriptingDefineSymbolsForGroup(
                BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget))));
            errors.AddRange(PlatformIsolationPolicy.CheckTargetAdapter(settings.platform.ToString(),
                PlatformDependencyCatalog.ReadInstalled(),
                CompilationPipeline.GetAssemblies(AssembliesType.Player).Select(x => x.name)));
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
