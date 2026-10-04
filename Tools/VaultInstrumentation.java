package com.guategeeks.vaultvalidation;

import android.app.Instrumentation;
import android.os.Bundle;
import android.content.Context;
import com.guategeeks.awsvr.CredentialVault;
import java.io.*;
import java.security.KeyStore;

/** Runs the production vault source under a separate test application's UID. */
public final class VaultInstrumentation extends Instrumentation {
    @Override public void onCreate(Bundle arguments) { super.onCreate(arguments); start(); }
    private void require(boolean ok) { if (!ok) throw new AssertionError("Vault assertion failed"); }
    @Override public void onStart() {
        Bundle result = new Bundle();
        try {
            Context context = getTargetContext(); String binding = "https://test.invalid\nquest-test\n1";
            CredentialVault.forget(context);
            require(CredentialVault.load(context, binding) == null);
            CredentialVault.save(context, binding, "Ab12Cd");
            require("Ab12Cd".equals(CredentialVault.load(context, binding)));
            File ciphertext = new File(context.getNoBackupFilesDir(), "cloud-credential.bin");
            byte[] bytes = java.nio.file.Files.readAllBytes(ciphertext.toPath());
            require(!new String(bytes, java.nio.charset.StandardCharsets.ISO_8859_1).contains("Ab12Cd"));
            boolean rejected = false;
            try { CredentialVault.load(context, binding + "other"); } catch (Exception expected) { rejected = true; }
            require(rejected);
            bytes[bytes.length - 1] ^= 1; java.nio.file.Files.write(ciphertext.toPath(), bytes);
            rejected = false;
            try { CredentialVault.load(context, binding); } catch (Exception expected) { rejected = true; }
            require(rejected);
            CredentialVault.save(context, binding, "Ab12Cd");
            KeyStore store = KeyStore.getInstance("AndroidKeyStore"); store.load(null); store.deleteEntry("GuateGeeks.Cloud.v1");
            rejected = false;
            try { CredentialVault.load(context, binding); } catch (Exception expected) { rejected = true; }
            require(rejected);
            CredentialVault.forget(context); require(!ciphertext.exists());
            CredentialVault.save(context, binding, "Cd34Ef");
            require("Cd34Ef".equals(CredentialVault.load(context, binding)));
            CredentialVault.forget(context);
            result.putString("stream", "PASS: real Android Keystore roundtrip, ciphertext, profile binding, tamper rejection, missing key, forget and re-enrollment.\n");
            finish(-1, result);
        } catch (Throwable error) {
            result.putString("stream", "FAIL: " + error.getClass().getSimpleName() + "\n"); finish(0, result);
        }
    }
}
