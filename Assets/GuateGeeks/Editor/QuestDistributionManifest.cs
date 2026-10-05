using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace GuateGeeks.AwsVr.Editor
{
    // OpenXR 1.16 emits the legacy Quest 3 alias "eureka". Meta's uploader now
    // expects "quest3". Normalize the generated manifest without a custom template.
    public sealed class QuestDistributionManifest : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 10000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // XR Management 4.4+ emits its metadata in a separate Android library.
            foreach (string manifestPath in new[] {
                Path.Combine(path, "src", "main", "AndroidManifest.xml"),
                Path.Combine(path, "xrmanifest.androidlib", "AndroidManifest.xml")
            })
                if (File.Exists(manifestPath)) NormalizeDevices(manifestPath);
        }

        static void NormalizeDevices(string manifestPath)
        {
            const string android = "http://schemas.android.com/apk/res/android";
            var document = new XmlDocument();
            document.Load(manifestPath);
            var namespaces = new XmlNamespaceManager(document.NameTable);
            namespaces.AddNamespace("android", android);
            var devices = document.SelectSingleNode(
                "/manifest/application/meta-data[@android:name='com.oculus.supportedDevices']",
                namespaces) as XmlElement;
            if (devices == null) return;
            string[] names = devices.GetAttribute("value", android).Split('|');
            bool changed = false;
            for (int i = 0; i < names.Length; i++)
                if (names[i] == "eureka") { names[i] = "quest3"; changed = true; }
            if (!changed) return;
            devices.SetAttribute("value", android, string.Join("|", names));
            document.Save(manifestPath);
        }
    }
}
