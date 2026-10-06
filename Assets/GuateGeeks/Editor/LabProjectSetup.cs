using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace GuateGeeks.AwsVr.Editor
{
    public static class LabProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/AWSArchitectLab.unity";
        [MenuItem("GuateGeeks/Configure lab and Quest")]
        public static void Configure()
        {
            if (!File.Exists("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset"))
            {
                string ugui = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UnityEngine.UI.Graphic).Assembly).resolvedPath;
                UnityEditor.AssetPackage.Package.Import(Path.Combine(ugui, "Package Resources/TMP Essential Resources.unitypackage"), false);
            }
            Directory.CreateDirectory("Assets/XR/Settings"); AssetDatabase.Refresh();
            ConfigureXR();
            PlayerSettings.companyName = "GuateGeeks";
            PlayerSettings.productName = "GuateGeeks AWS Architect Lab";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.guategeeks.awsarchitectlab");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.bundleVersion = "0.24.0";
            PlayerSettings.Android.bundleVersionCode = 26;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.optimizedFramePacing = false; // Unity WebRTC Android requirement; OpenXR controls headset pacing.
            // Immersive Meta Horizon uploads require API 34; Auto selects the newest installed SDK.
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1600; PlayerSettings.defaultScreenHeight = 900;
            if (!File.Exists(ScenePath))
            {
                var previous = SceneManager.GetActiveScene();
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("AWS Architect Lab · press Play"); root.AddComponent<ArchitectureLab>();
                EditorSceneManager.SaveScene(scene, ScenePath);
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            }
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            // Keep the original sample available, but launch the lab by default in player builds.
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Validation");
            File.WriteAllText("Validation/setup-result.txt", "Lab scene and OpenXR configured. Android build module installed: " + BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android));
            Debug.Log("GuateGeeks lab configured. Open Assets/Scenes/AWSArchitectLab.unity and press Play.");
        }
        static void ConfigureXR()
        {
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget settings))
            {
                settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(settings, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, settings, true);
            }
            foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
            {
                if (!settings.HasSettingsForBuildTarget(group)) settings.CreateDefaultSettingsForBuildTarget(group);
                if (!settings.HasManagerSettingsForBuildTarget(group)) settings.CreateDefaultManagerSettingsForBuildTarget(group);
                var general = settings.SettingsForBuildTarget(group);
                general.InitManagerOnStart = group == BuildTargetGroup.Android;
                if (!XRPackageMetadataStore.AssignLoader(general.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group))
                    throw new InvalidOperationException("Could not assign OpenXR loader for " + group);
                EditorUtility.SetDirty(general); EditorUtility.SetDirty(general.Manager);
                EnsureOpenXRSettings(group);
                FeatureHelpers.RefreshFeatures(group);
                var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                xr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
                foreach (var feature in xr.GetFeatures())
                {
                    if (feature.GetType().Name == "OculusTouchControllerProfile" || feature.GetType().Name == "MetaQuestTouchPlusControllerProfile" ||
                        (group == BuildTargetGroup.Android && (feature.GetType().Name == "MetaQuestFeature" || feature.GetType().Name == "ARCameraFeature" || feature.GetType().Name == "ARSessionFeature" || feature.GetType().Name == "OpenXRCompositionLayersFeature" || feature.GetType().Name == "HandTracking" || feature.GetType().Name == "MetaHandTrackingAim" || feature.GetType().Name == "FoveatedRenderingFeature")))
                    { feature.enabled = true; EditorUtility.SetDirty(feature); }
                }
                EditorUtility.SetDirty(xr);
            }
            EditorUtility.SetDirty(settings);
        }
        static void EnsureOpenXRSettings(BuildTargetGroup group)
        {
            if (OpenXRSettings.GetSettingsForBuildTargetGroup(group)) return;
            // OpenXR 1.16 omits settings for targets whose build module is not installed.
            // Serialize Android settings now so this project is ready when that module is added.
            if (!EditorBuildSettings.TryGetConfigObject(Constants.k_SettingsKey, out ScriptableObject package))
            {
                var type = typeof(FeatureHelpers).Assembly.GetType("UnityEditor.XR.OpenXR.OpenXRPackageSettings", true);
                package = ScriptableObject.CreateInstance(type);
                AssetDatabase.CreateAsset(package, "Assets/XR/Settings/OpenXR Package Settings.asset");
                EditorBuildSettings.AddConfigObject(Constants.k_SettingsKey, package, true);
            }
            var serialized = new SerializedObject(package); var keys = serialized.FindProperty("Keys"); var values = serialized.FindProperty("Values");
            int index = keys.arraySize; keys.InsertArrayElementAtIndex(index); keys.GetArrayElementAtIndex(index).intValue = (int)group;
            var xr = ScriptableObject.CreateInstance<OpenXRSettings>(); xr.name = group.ToString(); AssetDatabase.AddObjectToAsset(xr, package);
            values.InsertArrayElementAtIndex(index); values.GetArrayElementAtIndex(index).objectReferenceValue = xr;
            serialized.ApplyModifiedPropertiesWithoutUndo(); ((ISerializationCallbackReceiver)package).OnAfterDeserialize();
            EditorUtility.SetDirty(package);
        }
        [MenuItem("GuateGeeks/Open AWS Architect Lab")]
        public static void OpenLab()
        {
            if (!File.Exists(ScenePath)) Configure();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }
        [MenuItem("GuateGeeks/PC preview/Desktop mode (no headset)")]
        public static void Desktop() => SetPCVR(false);
        [MenuItem("GuateGeeks/PC preview/Enable OpenXR headset")]
        public static void Headset() => SetPCVR(true);
        static void SetPCVR(bool enabled)
        {
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (!general) { Configure(); general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone); }
            general.InitManagerOnStart = enabled; EditorUtility.SetDirty(general); AssetDatabase.SaveAssets();
        }
    }
}

