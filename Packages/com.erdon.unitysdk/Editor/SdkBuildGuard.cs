using UnityEditor.Build;
using UnityEditor.Build.Reporting;
namespace ErDon.UnitySDK.Editor
{
    // This development release has no real provider. Fail closed instead of shipping a fake integration.
    public sealed class SdkBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return -1000; } }
        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = SdkProjectSettings.instance;
            var errors = settings.Validate();
            errors.AddRange(PlatformDependencyCatalog.CheckPlayerAssemblies(settings.platform.ToString()));
            if (settings.platform == DistributionPlatform.Mock)
                errors.Add("Mock 仅支持 Editor 示例，不能打入 Player。");
            if (errors.Count != 0)
                throw new BuildFailedException("ErDon SDK 构建检查未通过：\n" + string.Join("\n", errors));
        }
    }
}
