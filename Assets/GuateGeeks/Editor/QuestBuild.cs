using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.OpenXR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace GuateGeeks.AwsVr.Editor
{
    [InitializeOnLoad]
    public static class QuestBuild
    {
        public const string ApkPath = "Builds/Quest3/GuateGeeksAWSVR-Quest3.apk";
        const string PendingKey = "GuateGeeks.QuestBuild.Pending";
        const string ReportPath = "Validation/quest3-preflight.txt";
        static QuestBuild() { EditorApplication.update += ContinueBuild; }

        [MenuItem("GuateGeeks/Meta Quest 3/1 - Prepare project")]
        public static void Configure()
        {
            LabProjectSetup.Configure();
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            PlayerSettings.Android.splitApplicationBinary = false;
            PlayerSettings.Android.useCustomKeystore = false; // Local sideload demo; no signing secrets in the project.
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Minimal);
            var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            var feature = xr.GetFeatures().Single(f => f.GetType().Name == "MetaQuestFeature");
            var meta = new SerializedObject(feature);
            var devices = meta.FindProperty("targetDevices");
            bool found = false;
            for (int i = 0; i < devices.arraySize; i++)
            {
                var device = devices.GetArrayElementAtIndex(i);
                bool quest3 = device.FindPropertyRelative("manifestName").stringValue == "eureka";
                device.FindPropertyRelative("enabled").boolValue = quest3; found |= quest3;
            }
            if (!found) throw new BuildFailedException("Meta Quest 3 target is missing from the OpenXR package.");
            meta.FindProperty("forceRemoveInternetPermission").boolValue = false;
            meta.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(feature);

            // This project's Mobile quality tier is the Android/MetaQuest default.
            // The lab uses unlit geometry; depth/opaque copies, HDR and shadows are unnecessary.
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            var mobile = new SerializedObject(pipeline);
            foreach (string property in new[] { "m_SupportsHDR", "m_RequireDepthTexture", "m_RequireOpaqueTexture", "m_MainLightShadowsSupported", "m_AdditionalLightShadowsSupported", "m_SoftShadowsSupported" })
                mobile.FindProperty(property).boolValue = false;
            mobile.FindProperty("m_MSAA").intValue = 4;
            mobile.FindProperty("m_RenderScale").floatValue = 1;
            mobile.FindProperty("m_ShadowDistance").floatValue = 0;
            mobile.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets(); Check();
        }

        [MenuItem("GuateGeeks/Meta Quest 3/2 - Check readiness")]
        public static void Check() => Preflight();

        static bool Preflight()
        {
            // OpenXR 1.16's Meta Quest rules read the selected UI group, even when
            // validating an explicit Android target. Async switching leaves it on PC.
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
                EditorUserBuildSettings.selectedBuildTargetGroup = BuildTargetGroup.Android;
            var lines = new List<string> { "Quest 3 preflight | " + DateTime.UtcNow.ToString("O"), "Unity " + Application.unityVersion,
                "Active target: " + EditorUserBuildSettings.activeBuildTarget + "; selected group: " + EditorUserBuildSettings.selectedBuildTargetGroup };
            int errors = 0;
            void Require(bool condition, string label) { lines.Add((condition ? "PASS " : "BLOCKED ") + label); if (!condition) errors++; }
            Require(BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android), "Android Build Support installed (restart Unity after installing modules)");
            string android = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer");
            Require(File.Exists(Path.Combine(android,"SDK/platform-tools/adb.exe")), "Bundled Android SDK / adb");
            Require(File.Exists(Path.Combine(android,"NDK/source.properties")), "Bundled Android NDK");
            Require(File.Exists(Path.Combine(android,"OpenJDK/bin/java.exe")), "Bundled OpenJDK");
            string platforms = Path.Combine(android,"SDK/platforms");
            int maxSdk = Directory.Exists(platforms) ? Directory.GetDirectories(platforms).Select(p => int.TryParse(Path.GetFileName(p).Replace("android-", ""), out int sdk) ? sdk : 0).DefaultIfEmpty(0).Max() : 0;
            Require(maxSdk >= 34, "Installed Android platform API >= 34; highest=" + maxSdk);
            Require(PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) == ScriptingImplementation.IL2CPP && PlayerSettings.Android.targetArchitectures == AndroidArchitecture.ARM64, "IL2CPP / ARM64 only");
            Require(PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).SequenceEqual(new[] { GraphicsDeviceType.Vulkan }), "Vulkan");
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            Require(general && general.InitManagerOnStart && general.Manager.activeLoaders.Any(l => l is OpenXRLoader), "OpenXR initializes on Android startup");
            var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            Require(xr && xr.renderMode == OpenXRSettings.RenderMode.SinglePassInstanced, "Single-pass stereo rendering");
            foreach (string name in new[] { "MetaQuestFeature", "MetaQuestTouchPlusControllerProfile", "OculusTouchControllerProfile", "ARCameraFeature", "ARSessionFeature", "OpenXRCompositionLayersFeature", "HandTracking", "MetaHandTrackingAim" })
                Require(xr && xr.GetFeatures().Any(f => f.GetType().Name == name && f.enabled), name);
            var metaFeature = xr ? xr.GetFeatures().FirstOrDefault(f => f.GetType().Name == "MetaQuestFeature") : null;
            if (metaFeature)
            {
                var meta = new SerializedObject(metaFeature);
                var devices = meta.FindProperty("targetDevices"); bool quest3 = false;
                for (int i = 0; i < devices.arraySize; i++)
                {
                    var device = devices.GetArrayElementAtIndex(i);
                    quest3 |= device.FindPropertyRelative("manifestName").stringValue == "eureka" && device.FindPropertyRelative("enabled").boolValue;
                }
                Require(quest3, "Quest 3 manifest target (eureka)");
                Require(!meta.FindProperty("forceRemoveInternetPermission").boolValue, "Cloud integration: Internet permission retained");
            }
            int mobileIndex = Array.IndexOf(QualitySettings.names, "Mobile");
            var pipeline = mobileIndex >= 0 ? QualitySettings.GetRenderPipelineAssetAt(mobileIndex) : null;
            Require(pipeline && AssetDatabase.GetAssetPath(pipeline) == "Assets/Settings/Mobile_RPAsset.asset", "Mobile URP asset assigned");
            if (pipeline)
            {
                var mobile = new SerializedObject(pipeline);
                Require(mobile.FindProperty("m_MSAA").intValue == 4 && !mobile.FindProperty("m_SupportsHDR").boolValue, "Mobile MSAA 4x / HDR off");
            }
            Require(File.Exists(LabProjectSetup.ScenePath), "AWSArchitectLab scene");
            // Some package rules dereference the active platform's render settings.
            // Evaluate the full rule set only after Android has been activated.
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                try
                {
                    var issues = new List<OpenXRFeature.ValidationRule>();
                    OpenXRProjectValidation.GetCurrentValidationIssues(issues, BuildTargetGroup.Android);
                    foreach (var issue in issues) { lines.Add((issue.error ? "BLOCKED " : "NOTICE ") + issue.message); if (issue.error) errors++; }
                }
                catch (Exception e) { lines.Add("BLOCKED OpenXR validation could not complete: " + e); errors++; }
            }
            else lines.Add("NOTICE Full OpenXR validation will run after switching to Android, before building.");
            lines.Add("App ID: " + PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android));
            lines.Add("APK: " + ApkPath + " (local demo signing; not a store release)");
            lines.Add(errors == 0 ? "READY TO BUILD. Headset validation still required." : "NOT BUILT: resolve " + errors + " blocking checks.");
            Directory.CreateDirectory("Validation"); File.WriteAllLines(ReportPath, lines);
            Debug.Log("Quest 3 preflight: " + errors + " blockers. See " + ReportPath);
            return errors == 0;
        }

        [MenuItem("GuateGeeks/Meta Quest 3/3 - Build demo APK")]
        public static void RequestBuild()
        {
            Configure();
            if (!Preflight()) throw new BuildFailedException("Quest 3 prerequisites are missing. See " + ReportPath);
            SessionState.SetBool(PendingKey, true);
            File.WriteAllText("Validation/quest3-build.txt", "PENDING: switching to Android, then building the demo APK.");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android && !EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildTargetGroup.Android, BuildTarget.Android))
            {
                SessionState.SetBool(PendingKey, false);
                throw new BuildFailedException("Could not switch to Android.");
            }
        }
        static void ContinueBuild()
        {
            if (!SessionState.GetBool(PendingKey, false) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) return;
            SessionState.SetBool(PendingKey, false);
            try { BuildApk(); }
            catch (Exception e) { File.AppendAllText("Validation/quest3-build.txt", "\nFAILED: " + e); Debug.LogException(e); }
        }
        // CLI: Unity -batchmode -quit -buildTarget Android -projectPath ... -executeMethod GuateGeeks.AwsVr.Editor.QuestBuild.BuildApk
        public static void BuildApk()
        {
            Configure();
            if (!Preflight() || EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) throw new BuildFailedException("Android must be active and preflight must pass.");
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { LabProjectSetup.ScenePath }, target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android, locationPathName = Path.GetFullPath(ApkPath), options = BuildOptions.CompressWithLz4 });
            var lines = new List<string> { DateTime.UtcNow.ToString("O") + " | " + report.summary.result + " | errors=" + report.summary.totalErrors + " warnings=" + report.summary.totalWarnings,
                "Platform: " + report.summary.platform, "Output: " + report.summary.outputPath };
            foreach (var step in report.steps)
                foreach (var message in step.messages)
                    if (message.type == LogType.Error || message.type == LogType.Exception || message.type == LogType.Warning)
                        lines.Add(step.name + ": " + message.type + " " + message.content);
            bool valid = report.summary.result == BuildResult.Succeeded && report.summary.totalErrors == 0 && report.summary.platform == BuildTarget.Android && File.Exists(ApkPath);
            lines.Add(valid ? "VERIFIED: Android APK exists (" + new FileInfo(ApkPath).Length + " bytes)." : "FAILED: no verified Android APK.");
            File.WriteAllLines("Validation/quest3-build.txt", lines);
            if (!valid) throw new BuildFailedException("Quest APK build failed. See Validation/quest3-build.txt and restart Unity if Android modules were just installed.");
        }
    }
}
