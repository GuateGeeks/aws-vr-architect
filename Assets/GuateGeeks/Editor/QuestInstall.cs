using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;

namespace GuateGeeks.AwsVr.Editor
{
    // Installs the built demo APK on one USB-authorised Meta Quest 3 with Unity's bundled adb, starts it and
    // verifies the installed package (version and SHA-256 of base.apk). Mirrors Tools/Install-Quest3.ps1 so the
    // fixed `install-quest` editor command can deploy without a terminal. Never uninstalls or clears app data.
    public static class QuestInstall
    {
        public const string Package = "com.guategeeks.awsarchitectlab";
        const string Activity = Package + "/com.unity3d.player.UnityPlayerGameActivity";
        public const string ReportPath = "Validation/quest3-install.txt";

        // Fixed `quest-status` command: running process, installed version and recent Unity log lines from the headset.
        public static void Status()
        {
            string adb = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe");
            var log = new List<string> { DateTime.UtcNow.ToString("O") + " | quest-status" };
            foreach (var args in new[] { "shell pidof " + Package, "shell dumpsys package " + Package + " | grep versionName",
                "shell dumpsys power | grep mWakefulness", "logcat -d -t 400 -s Unity" })
            {
                var info = new ProcessStartInfo(adb, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using (var process = Process.Start(info))
                {
                    var o = process.StandardOutput.ReadToEndAsync(); var e = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(60000)) process.Kill();
                    log.Add("> adb " + args); log.Add((o.Result + e.Result).Trim());
                }
            }
            File.WriteAllLines("Validation/quest3-status.txt", log);
        }
        [MenuItem("GuateGeeks/Meta Quest 3/4 - Install built APK on connected Quest")]
        public static void Run()
        {
            var log = new List<string> { DateTime.UtcNow.ToString("O") + " | install-quest" };
            try
            {
                string adb = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe");
                string apk = Path.GetFullPath(QuestBuild.ApkPath);
                if (!File.Exists(adb)) throw new InvalidOperationException("adb not found: " + adb);
                if (!File.Exists(apk)) throw new InvalidOperationException("Build the APK first: " + apk);
                string Adb(string args, int timeoutSeconds = 240)
                {
                    var info = new ProcessStartInfo(adb, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                    using (var process = Process.Start(info))
                    {
                        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                        if (!process.WaitForExit(timeoutSeconds * 1000)) { process.Kill(); throw new TimeoutException("adb " + args); }
                        string output = (stdout.Result + stderr.Result).Trim();
                        log.Add("> adb " + args); log.Add(output);
                        return output;
                    }
                }
                var ready = Adb("devices").Split('\n').Select(l => l.Trim()).Where(l => l.EndsWith("\tdevice") || l.EndsWith(" device"))
                    .Select(l => l.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries)[0]).ToArray();
                if (ready.Length != 1) throw new InvalidOperationException("Connect and authorise exactly one Quest over USB (found " + ready.Length + "). Accept the USB debugging prompt inside the headset.");
                string serial = ready[0];
                string model = Adb("-s " + serial + " shell getprop ro.product.model");
                if (model != "Quest 3") throw new InvalidOperationException("Device is not a Meta Quest 3: " + model);
                string install = Adb("-s " + serial + " install -r \"" + apk + "\"", 600);
                if (!install.Contains("Success")) throw new InvalidOperationException("adb install failed; nothing was uninstalled or cleared.");
                string launch = Adb("-s " + serial + " shell am start -W -n " + Activity);
                string version = Adb("-s " + serial + " shell dumpsys package " + Package + " | grep version");
                string remotePath = Adb("-s " + serial + " shell pm path " + Package).Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("package:") && l.EndsWith("base.apk"))?.Substring(8);
                string remoteHash = remotePath == null ? "" : Adb("-s " + serial + " shell sha256sum " + remotePath).Split(' ')[0].Trim();
                string localHash;
                using (var sha = SHA256.Create()) using (var stream = File.OpenRead(apk)) localHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                log.Add("Local APK SHA-256: " + localHash);
                log.Add("Installed base.apk SHA-256: " + remoteHash);
                bool launched = launch.Contains("Status: ok");
                bool same = remoteHash == localHash;
                log.Add((same ? "VERIFIED" : "FAILED") + ": installed APK " + (same ? "matches" : "does not match") + " the built APK. " + (launched ? "Launch requested: Status ok." : "Launch not confirmed; open the app from Unknown Sources in the headset."));
                if (launch.Contains("LaunchCheckControllerRequiredDialogActivity")) log.Add("Meta asks to wake the Touch Plus controllers before the app opens.");
                if (!same) throw new InvalidOperationException("Installed APK hash mismatch.");
            }
            catch (Exception e) { log.Add("FAILED: " + e.Message); throw; }
            finally { Directory.CreateDirectory("Validation"); File.WriteAllLines(ReportPath, log); }
        }
    }
}
