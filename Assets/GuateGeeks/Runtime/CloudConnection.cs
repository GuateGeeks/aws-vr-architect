using System;
using System.IO;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // Runtime-only bootstrap. Never serialize this object into an asset or PlayerPrefs.
    [Serializable]
    public sealed class CloudConnection
    {
        public string endpoint, username, password;
        public string deploymentId = "1";
        public long expiresAt;
        public void Validate()
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("Usa la URL HTTPS de la API, sin credenciales ni parámetros.");
            if (string.IsNullOrEmpty(username) || username.Contains(":") || string.IsNullOrEmpty(password) || password.Length > 6 ||
                System.Text.Encoding.UTF8.GetByteCount(username + ":" + password) > 1500)
                throw new ArgumentException("Credenciales de servicio inválidas.");
            if (deploymentId != "1" && deploymentId != "2" && deploymentId != "3")
                throw new ArgumentException("Selecciona un slot entre 1 y 3.");
        }
        public static CloudConnection Consume(string path)
        {
            string json;
            try
            {
                if (new FileInfo(path).Length > 8192) throw new ArgumentException("Configuración demasiado grande.");
                json = File.ReadAllText(path);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            var value = JsonUtility.FromJson<CloudConnection>(json);
            if (value == null || value.expiresAt < DateTimeOffset.UtcNow.ToUnixTimeSeconds() ||
                value.expiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 120)
                throw new ArgumentException("Configuración vencida. Vuelve a preparar la sesión desde tu PC.");
            value.Validate(); return value;
        }
    }
}
