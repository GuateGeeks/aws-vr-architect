using System;
using System.Threading.Tasks;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    public interface ICredentialStore
    {
        Task<string> Load(string binding);
        Task Save(string binding, string password);
        Task Forget();
    }
    // Editor/desktop never falls back to plaintext persistence.
    public sealed class CredentialStore : ICredentialStore
    {
        public static bool Supported => Application.platform == RuntimePlatform.Android;
        static Task<string> Call(string method, params object[] args)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Task.Run(() => {
                AndroidJNI.AttachCurrentThread();
                try
                {
                    using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
                    using (var vault = new AndroidJavaClass("com.guategeeks.awsvr.CredentialVault"))
                    {
                        var values = new object[args.Length + 1]; values[0] = activity;
                        Array.Copy(args, 0, values, 1, args.Length);
                        return vault.CallStatic<string>(method, values);
                    }
                }
                finally { AndroidJNI.DetachCurrentThread(); }
            });
#else
            return method == "save" ? Task.FromException<string>(new NotSupportedException("Recordar está disponible en Quest.")) : Task.FromResult<string>(null);
#endif
        }
        public Task<string> Load(string binding) => Call("load", binding);
        public Task Save(string binding, string password) => Call("save", binding, password);
        public Task Forget() => Call("forget");
    }
}
