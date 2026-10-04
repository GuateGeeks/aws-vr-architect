using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class CloudProfileTests
    {
        [TestCase("")]
        [TestCase("1234567")]
        public void InvalidPasswordLengthRejected(string password)
        {
            Assert.Throws<ArgumentException>(() => new CloudProfile().Connection(password).Validate());
        }
        [Test] public void SixCharactersAcceptedAndNeverSerializedInProfile()
        {
            var profile = new CloudProfile(); profile.Connection("Ab12Cd").Validate();
            Assert.That(JsonUtility.ToJson(profile), Does.Not.Contain("password").And.Not.Contain("Ab12Cd"));
        }
        [Test] public void CredentialBindingChangesWithDestinationIdentityAndSlot()
        {
            var original = new CloudProfile().Binding;
            Assert.AreNotEqual(original, new CloudProfile { endpoint = "https://other.invalid" }.Binding);
            Assert.AreNotEqual(original, new CloudProfile { username = "other" }.Binding);
            Assert.AreNotEqual(original, new CloudProfile { slot = "2" }.Binding);
        }
        [Test] public void MissingAndCorruptProfileNeverAutoConnect()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            try {
                Assert.IsFalse(CloudProfile.Read(path).autoConnect);
                File.WriteAllText(path, "not json");
                Assert.IsFalse(CloudProfile.Read(path).autoConnect);
            } finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
