using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
namespace ErDon.UnitySDK.Android.Editor
{
    // Runs even when the Android adapter define is absent, so AAR importer mistakes fail closed.
    public sealed class AndroidBuildGuard : IPreprocessBuildWithReport
    {
        const string Aar="Packages/com.erdon.unitysdk.android/Runtime/Plugins/Android/erdon-android.aar";
        public int callbackOrder {get{return -1100;}}
        public void OnPreprocessBuild(BuildReport report) {
            var plugin=AssetImporter.GetAtPath(Aar) as PluginImporter;
            if(plugin==null || plugin.GetCompatibleWithAnyPlatform() || plugin.GetCompatibleWithEditor())
                throw new BuildFailedException("ErDon Android AAR must be Android-only, with Editor/Any Platform disabled");
            foreach(BuildTarget target in new[]{BuildTarget.WebGL,BuildTarget.iOS,BuildTarget.StandaloneWindows64,BuildTarget.StandaloneOSX,BuildTarget.StandaloneLinux64})
                if(plugin.GetCompatibleWithPlatform(target))throw new BuildFailedException("Android AAR enabled on foreign platform: "+target);
            if(report.summary.platform!=BuildTarget.Android)return;
            if(ErDon.UnitySDK.Editor.SdkProjectSettings.instance.platform.ToString()!="Android")throw new BuildFailedException("Remove Android native package from non-Android SDK distributions");
            if(!plugin.GetCompatibleWithPlatform(BuildTarget.Android))throw new BuildFailedException("Android AAR missing or disabled");
            if((int)PlayerSettings.Android.minSdkVersion<26)throw new BuildFailedException("ErDon Android native preview requires minSdkVersion 26 or later");
        }
    }
}
