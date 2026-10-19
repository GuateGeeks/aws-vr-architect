using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GuateGeeks.AwsVr.Editor
{
    public static class RecoveryValidation
    {
        // Use a separate identity when the production signing key is unavailable.
        public static void BuildQuestSideBySide()
        {
            const string output = "Builds/Quest3/GuateGeeksAWSVR-Quest3-Recovery.apk";
            var identifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            var product = PlayerSettings.productName;
            var preloaded = PlayerSettings.GetPreloadedAssets();
            BuildReport report;
            try
            {
                QuestBuild.Configure();
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.guategeeks.awsarchitectlab.recovery");
                PlayerSettings.productName = "GuateGeeks AWS Architect Lab Recovery";
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { LabProjectSetup.ScenePath }, target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android, locationPathName = Path.GetFullPath(output),
                    options = BuildOptions.CompressWithLz4 });
            }
            finally
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, identifier);
                PlayerSettings.productName = product;
                PlayerSettings.SetPreloadedAssets(preloaded);
                AssetDatabase.SaveAssets();
            }
            File.WriteAllText("Recovery/0.24.4/deployment/quest-recovery-build-result.json", JsonUtility.ToJson(new Result {
                result = report.summary.result.ToString(), errors = report.summary.totalErrors,
                warnings = report.summary.totalWarnings, bytes = report.summary.totalSize }, true));
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0)
                throw new System.Exception("Recovery Quest build failed.");
        }

        // Headless entry point for a playable local rehearsal build. It does not deploy to AWS or a headset.
        public static void BuildDesktop()
        {
            Directory.CreateDirectory("Builds/Recovery");
            var preloaded = PlayerSettings.GetPreloadedAssets();
            BuildReport report;
            try { report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/Scenes/AWSArchitectLab.unity" },
                locationPathName = "Builds/Recovery/GuateGeeksAWSVR-Recovered.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None }); }
            finally
            {
                // XR's desktop postprocessor removes Android preload references from PlayerSettings.
                // Keep that filtering in the player, but preserve the Quest project's source settings.
                PlayerSettings.SetPreloadedAssets(preloaded);
                AssetDatabase.SaveAssets();
            }
            File.WriteAllText("Recovery/0.24.4/desktop-build-result.json", JsonUtility.ToJson(new Result {
                result = report.summary.result.ToString(), errors = report.summary.totalErrors,
                warnings = report.summary.totalWarnings, bytes = report.summary.totalSize }, true));
            if (report.summary.result != BuildResult.Succeeded) throw new System.Exception("Recovery desktop build failed.");
        }
        [System.Serializable] sealed class Result { public string result; public int errors, warnings; public ulong bytes; }
    }
}
