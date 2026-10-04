package com.guategeeks.awsvr;

import android.content.Context;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.AtomicFile;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.Arrays;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** Private, backup-excluded ciphertext; the AES key never leaves AndroidKeyStore. */
public final class CredentialVault {
    private static final String ALIAS = "GuateGeeks.Cloud.v1";
    private static AtomicFile file(Context context) {
        return new AtomicFile(new File(context.getNoBackupFilesDir(), "cloud-credential.bin"));
    }
    private static KeyStore keys() throws Exception {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore"); store.load(null); return store;
    }
    private static SecretKey key(boolean create) throws Exception {
        KeyStore store = keys();
        if (!store.containsAlias(ALIAS)) {
            if (!create) throw new IOException("Credential key unavailable");
            KeyGenerator generator = KeyGenerator.getInstance("AES", "AndroidKeyStore");
            generator.init(new KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256).build());
            return generator.generateKey();
        }
        return (SecretKey) store.getKey(ALIAS, null);
    }
    public static synchronized String save(Context context, String binding, String password) throws Exception {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding"); cipher.init(Cipher.ENCRYPT_MODE, key(true));
        cipher.updateAAD(binding.getBytes(StandardCharsets.UTF_8));
        byte[] plain = password.getBytes(StandardCharsets.UTF_8);
        byte[] encrypted;
        try { encrypted = cipher.doFinal(plain); } finally { Arrays.fill(plain, (byte)0); }
        AtomicFile target = file(context); FileOutputStream stream = null;
        try {
            stream = target.startWrite(); DataOutputStream out = new DataOutputStream(stream);
            out.writeInt(1); out.writeInt(cipher.getIV().length); out.write(cipher.getIV()); out.writeInt(encrypted.length); out.write(encrypted); out.flush();
            target.finishWrite(stream);
        } catch (Exception e) { if (stream != null) target.failWrite(stream); throw e; }
        return "saved";
    }
    public static synchronized String load(Context context, String binding) throws Exception {
        AtomicFile target = file(context);
        if (!target.getBaseFile().exists()) return null;
        try (DataInputStream in = new DataInputStream(target.openRead())) {
            if (in.readInt() != 1 || in.readInt() != 12) throw new IOException("Invalid credential format");
            byte[] iv = new byte[12]; in.readFully(iv); int size = in.readInt();
            if (size < 17 || size > 4096) throw new IOException("Invalid credential size");
            byte[] encrypted = new byte[size]; in.readFully(encrypted);
            if (in.read() != -1) throw new IOException("Invalid credential format");
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding"); cipher.init(Cipher.DECRYPT_MODE, key(false), new GCMParameterSpec(128, iv));
            cipher.updateAAD(binding.getBytes(StandardCharsets.UTF_8)); byte[] plain = cipher.doFinal(encrypted);
            try { return new String(plain, StandardCharsets.UTF_8); } finally { Arrays.fill(plain, (byte)0); }
        }
    }
    public static synchronized String forget(Context context) throws Exception {
        file(context).delete(); KeyStore store = keys(); if (store.containsAlias(ALIAS)) store.deleteEntry(ALIAS); return "forgotten";
    }
}
