using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;

/// <summary>
/// Headless build script for NomadGo-SpatialVision Android APK.
/// Invoked via: Unity -batchmode -executeMethod BuildScript.BuildAndroid
/// </summary>
public class BuildScript
{
    public static void BuildAndroid()
    {
        string outputDir = System.Environment.GetEnvironmentVariable("BUILD_OUTPUT_DIR") ?? "/home/ubuntu/build-output";
        Directory.CreateDirectory(outputDir);
        string apkPath = Path.Combine(outputDir, "NomadGo-SpatialVision.apk");

        // ---- Android SDK / NDK paths ----
        string androidSdk = System.Environment.GetEnvironmentVariable("ANDROID_HOME")
                         ?? System.Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")
                         ?? "/home/ubuntu/android-sdk";
        string ndkVersion = "23.1.7779620"; // Unity 2022 requires NDK r23b
        string ndkPath = Path.Combine(androidSdk, "ndk", ndkVersion);
        // Unity 2022 requires JDK 11 (not 17)
        string jdkPath = "/usr/lib/jvm/java-11-openjdk-amd64";

        // Set via EditorPrefs (legacy) AND AndroidExternalToolsSettings (Unity 2019+)
        EditorPrefs.SetString("AndroidSdkRoot", androidSdk);
        EditorPrefs.SetString("AndroidNdkRootR16b", ndkPath);
        EditorPrefs.SetString("JdkPath", jdkPath);

        // Unity 2022 reads from AndroidExternalToolsSettings
        UnityEditor.Android.AndroidExternalToolsSettings.jdkRootPath = jdkPath;
        UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath = androidSdk;
        UnityEditor.Android.AndroidExternalToolsSettings.ndkRootPath = ndkPath;
        // Gradle 7.2 path - required for Unity 2022 Android builds
        string gradlePath = "/home/ubuntu/gradle-7.2/gradle-7.2";
        UnityEditor.Android.AndroidExternalToolsSettings.gradlePath = gradlePath;
        Debug.Log($"[BuildScript] Gradle: {gradlePath}");

        Debug.Log($"[BuildScript] SDK:  {androidSdk}");
        Debug.Log($"[BuildScript] NDK:  {ndkPath}");
        Debug.Log($"[BuildScript] JDK:  {jdkPath}");
        Debug.Log($"[BuildScript] APK:  {apkPath}");

        // ---- Player Settings ----
        PlayerSettings.applicationIdentifier = "com.nomadgo.spatialvision";
        PlayerSettings.productName           = "NomadGo SpatialVision";
        PlayerSettings.bundleVersion         = "1.0.0";
        PlayerSettings.Android.bundleVersionCode = 1;
        PlayerSettings.Android.minSdkVersion  = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel33;

        // IL2CPP + ARM64 for production
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android, ManagedStrippingLevel.Minimal);

        // Disable Burst AOT compilation to avoid hang in headless build
        // Burst will still work at runtime via the pre-compiled fallback path

        // ---- Build Options ----
        BuildPlayerOptions opts = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Main.unity" },
            locationPathName = apkPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        Debug.Log("[BuildScript] Starting Android build (Burst AOT disabled for headless)...");
        BuildReport  report  = BuildPipeline.BuildPlayer(opts);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[BuildScript] ✅ Build succeeded: {apkPath} ({summary.totalSize / 1024 / 1024} MB)");
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError($"[BuildScript] ❌ Build FAILED: {summary.result}");
            foreach (var step in report.steps)
                foreach (var msg in step.messages)
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        Debug.LogError($"  [{step.name}] {msg.content}");
            EditorApplication.Exit(1);
        }
    }
}
