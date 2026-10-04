using GuateGeeks.AwsVr;
using UnityEditor;
using UnityEngine;

namespace GuateGeeks.Editor
{
    public sealed class CloudSessionWindow : EditorWindow
    {
        // Nonserialized fields: passwords must not survive reloads/editor sessions.
        [System.NonSerialized] string endpoint = "", username = "quest-demo", password = "";
        [System.NonSerialized] int slot;
        [MenuItem("GuateGeeks/Cloud session (Play Mode)")]
        static void Open() => GetWindow<CloudSessionWindow>("AWS session");
        void OnGUI()
        {
            EditorGUILayout.HelpBox("Connects the running lab to GuateGeeksAWS2026. Creating resources still requires confirmation in the lab. Credentials remain in memory.", MessageType.Info);
            endpoint = EditorGUILayout.TextField("HTTPS API URL", endpoint);
            username = EditorGUILayout.TextField("Service account", username);
            password = EditorGUILayout.PasswordField("Password", password);
            slot = EditorGUILayout.Popup("Shared deployment slot", slot, new[] { "1", "2", "3" });
            var lab = Application.isPlaying ? Object.FindAnyObjectByType<ArchitectureLab>() : null;
            using (new EditorGUI.DisabledScope(!lab || lab.Busy))
            {
                if (GUILayout.Button("Connect AWS"))
                {
                    try { lab.ConfigureCloud(new CloudConnection { endpoint = endpoint, username = username, password = password, deploymentId = (slot + 1).ToString() }); password = ""; }
                    catch (System.ArgumentException) { EditorUtility.DisplayDialog("Connection", "Enter an HTTPS URL, service account, password and slot.", "OK"); }
                }
            }
        }
        void OnDisable() { password = ""; }
    }
}
