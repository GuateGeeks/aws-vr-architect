using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GuateGeeks.AwsVr.Editor
{
    // A dedicated release command prevents accidental uploads signed with the local debug key.
    public static class QuestRelease
    {
        [MenuItem("GuateGeeks/Meta Quest 3/4 - Build signed private-channel APK")]
        public static void Build()
        {
            QuestBuild.Configure();
            string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new BuildFailedException("Missing release setting: " + name);
            string path = Required("GG_RELEASE_KEYSTORE"), alias = Required("GG_RELEASE_ALIAS");
            if (!File.Exists(path)) throw new BuildFailedException("Release keystore does not exist.");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) throw new BuildFailedException("Select Android before release build.");
            try
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = path; PlayerSettings.Android.keyaliasName = alias;
                PlayerSettings.Android.keystorePass = Required("GG_RELEASE_STORE_PASSWORD");
                PlayerSettings.Android.keyaliasPass = Required("GG_RELEASE_KEY_PASSWORD");
                Directory.CreateDirectory("Builds/Release");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { LabProjectSetup.ScenePath },
                    target = BuildTarget.Android, targetGroup = BuildTargetGroup.Android,
                    locationPathName = "Builds/Release/GuateGeeksAWSVR-Quest3.apk", options = BuildOptions.CompressWithLz4 });
                Directory.CreateDirectory("Validation");
                File.WriteAllText("Validation/quest-release-build.txt", report.summary.result + " | errors=" + report.summary.totalErrors);
                if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Release build failed.");
            }
            finally
            {
                PlayerSettings.Android.keystorePass = PlayerSettings.Android.keyaliasPass = "";
                PlayerSettings.Android.keystoreName = PlayerSettings.Android.keyaliasName = "";
                PlayerSettings.Android.useCustomKeystore = false;
            }
        }
    }
}
