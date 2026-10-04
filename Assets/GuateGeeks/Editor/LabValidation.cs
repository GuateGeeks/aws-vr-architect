using System;
using System.IO;
using System.Linq;
using UnityEditor.Compilation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace GuateGeeks.AwsVr.Editor
{
    // File-driven commands allow automated validation in an already-open editor.
    // Only these fixed commands are accepted; no arbitrary code is evaluated.
    [InitializeOnLoad]
    public static class LabValidation
    {
        static double nextPoll;
        static TestRunnerApi runner;
        static LabValidation() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            const string path = "Validation/editor-command.txt";
            if (!File.Exists(path)) return;
            // A command may arrive while the editor is unfocused with auto-refresh deferred.
            // Leave it queued until the current source has actually been imported/compiled.
            string stamp = string.Join("|", Directory.GetFiles("Assets/GuateGeeks", "*.cs", SearchOption.AllDirectories).OrderBy(p => p).Select(p => p + File.GetLastWriteTimeUtc(p).Ticks + new FileInfo(p).Length));
            if (SessionState.GetString("GuateGeeks.Validation.SourceStamp", "") != stamp)
            {
                SessionState.SetString("GuateGeeks.Validation.SourceStamp", stamp);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                CompilationPipeline.RequestScriptCompilation();
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorUtility.scriptCompilationFailed) return;
            string command = File.ReadAllText(path).Trim(); File.Delete(path);
            try
            {
                switch (command)
                {
                    case "setup": LabProjectSetup.Configure(); break;
                    case "resolve-packages": UnityEditor.PackageManager.Client.Resolve(); break;
                    case "quest-setup": QuestBuild.Configure(); break;
                    case "quest-check": QuestBuild.Check(); break;
                    case "build-quest": QuestBuild.RequestBuild(); break;
                    case "edit-tests": Run(TestMode.EditMode); break;
                    case "play-tests": Run(TestMode.PlayMode); break;
                    case "assistant-live-test": Run(TestMode.PlayMode,true); break;
                    case "build-desktop":
                        Directory.CreateDirectory("Builds");
                        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                            scenes = new[] { LabProjectSetup.ScenePath }, locationPathName = "Builds/GuateGeeksAWSVR.exe",
                            target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
                        File.WriteAllText("Validation/desktop-build.txt", report.summary.result + " | errors=" + report.summary.totalErrors + " warnings=" + report.summary.totalWarnings);
                        break;
                    case "open":
                        if (!EditorSceneManager.GetActiveScene().isDirty) EditorSceneManager.OpenScene(LabProjectSetup.ScenePath);
                        else throw new InvalidOperationException("Current scene has unsaved edits; open the lab manually.");
                        break;
                    default: throw new ArgumentException("Unknown lab validation command.");
                }
                if (File.Exists("Validation/command-error.txt")) File.Delete("Validation/command-error.txt");
            }
            catch (Exception ex) { File.WriteAllText("Validation/command-error.txt", ex.ToString()); Debug.LogException(ex); }
        }
        public static void Run(TestMode mode,bool live=false)
        {
            SessionState.SetString("GuateGeeks.Validation.Report",live?"RealtimeLive":mode.ToString());
            if (runner) UnityEngine.Object.DestroyImmediate(runner);
            runner = ScriptableObject.CreateInstance<TestRunnerApi>(); runner.RegisterCallbacks(new Results());
            runner.Execute(new ExecutionSettings(new Filter { testMode = mode, categoryNames=live?null:new[]{"!LiveOpenAI"}, testNames=live?new[]{"GuateGeeks.AwsVr.Tests.RealtimeLiveTests.NativeWebRtcCompletesToolAndAudioResponse"}:null, assemblyNames = new[] { mode == TestMode.EditMode ? "GuateGeeks.AwsVr.EditTests" : "GuateGeeks.AwsVr.PlayTests" } }));
        }
        sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                // Read the platform from the result: instance fields can be reset by
                // the domain reload when entering or leaving a PlayMode test run.
                var mode = result.Test.TestMode;
                if (mode != TestMode.EditMode && mode != TestMode.PlayMode) return;
                string report=SessionState.GetString("GuateGeeks.Validation.Report",mode.ToString());
                Directory.CreateDirectory("Validation"); TestRunnerApi.SaveResultToFile(result, "Validation/" + report + "-results.xml");
                File.WriteAllText("Validation/" + report + "-summary.txt", result.TestStatus + " | passed=" + result.PassCount + " failed=" + result.FailCount + " skipped=" + result.SkipCount);
                // Callbacks are global to the test runner, even across API instances.
                // Retaining this subscriber would overwrite this suite's report on the next run.
                TestRunnerApi.UnregisterTestCallback(this);
            }
        }
    }
}
