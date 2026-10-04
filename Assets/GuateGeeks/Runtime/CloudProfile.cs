using System;
using System.IO;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    [Serializable]
    public sealed class CloudProfile
    {
        public string endpoint = "https://9bc46tb7d6.execute-api.us-east-1.amazonaws.com/demo";
        public string username = "quest-demo";
        public string slot = "1";
        public bool remember, autoConnect;
        public string Binding => endpoint.TrimEnd('/') + "\n" + username + "\n" + slot;
        public CloudConnection Connection(string password) => new CloudConnection { endpoint = endpoint.TrimEnd('/'), username = username, password = password, deploymentId = slot };
        public static CloudProfile Read(string path)
        {
            try {
                var value = JsonUtility.FromJson<CloudProfile>(File.ReadAllText(path));
                value.Connection("check").Validate();
                return value;
            }
            catch { return new CloudProfile(); }
        }
        public void Save(string path) => File.WriteAllText(path, JsonUtility.ToJson(this));
    }
}
