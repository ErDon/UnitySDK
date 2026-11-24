using System;
using System.IO;
using ErDon.UnitySDK.Editor;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// Run only in the isolated AndroidBridgeProject; produces a non-production ARM64 IL2CPP smoke APK.
public static class AndroidDevelopmentBuildChecks
{
    public static void Run() {
        try {
            var settings=SdkProjectSettings.instance;
            settings.platform=DistributionPlatform.Android;settings.production=false;
            settings.gameId="android-smoke";settings.appId="com.erdon.sdk.smoke";
            settings.login=false;settings.storage=true;settings.share=true;
            settings.payments=false;settings.rewardedAds=false;settings.network=false;settings.analytics=false;
            settings.SaveSettings();
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android,settings.appId);
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android,ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion=(AndroidSdkVersions)26;
            PlayerSettings.Android.targetSdkVersion=(AndroidSdkVersions)33;
            PlayerSettings.Android.useCustomKeystore=false;
            PlayerSettings.Android.minifyDebug=true;
            var scene=EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,NewSceneMode.Single);
            new GameObject("SdkSmoke").AddComponent<AndroidDeviceSmoke>();
            const string scenePath="Assets/AndroidSmoke.unity";
            EditorSceneManager.SaveScene(scene,scenePath);
            string output=Path.GetFullPath("../android-smoke/SDKSmoke.apk");Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{scenePath},locationPathName=output,target=BuildTarget.Android,options=BuildOptions.Development
            });
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Android smoke build failed: "+report.summary.totalErrors);
            Debug.Log("ANDROID_IL2CPP_R8_BUILD_PASS "+output);
            EditorApplication.Exit(0);
        }catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
}
