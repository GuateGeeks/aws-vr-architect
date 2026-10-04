using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Text = TMPro.TMP_Text;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    public sealed partial class ArchitectureLab
    {
        CloudProfile profile;
        ICredentialStore credentialStore = new CredentialStore();
        RectTransform connectionForm;
        Text endpointField, usernameField, passwordField, slotField, rememberField, formHint;
        string enteredPassword = "";
        int activeField = 2;
        bool upperCase, credentialBusy;
        readonly System.Collections.Generic.List<LabTarget> letterKeys = new System.Collections.Generic.List<LabTarget>();
        public bool ConfiguringConnection => connectionForm && connectionForm.gameObject.activeSelf;
        string ProfilePath => Path.Combine(Application.persistentDataPath, "cloud-profile.json");
        public bool CanInteract(LabTarget target) => AssistantEnabled && target && (target==assistantConnect || target==assistantDock || target==presenceOff || target==presenceStop) ? true : ConfiguringConnection ? target && (target.transform.IsChildOf(connectionForm) || (target.Menu && settingsPanel && target.Menu.transform == settingsPanel)) : EditingText ? target && target.transform.IsChildOf(designKeyboard) : codeBusy ? target && codePanel && target.transform.IsChildOf(codePanel) : Placing ? target && target.transform.IsChildOf(inspector) : true;

        void BuildConnectionForm()
        {
            profile = CloudProfile.Read(ProfilePath);
            connectionForm = Panel(world, "Configuración de conexión", new Vector3(0, 1.9f, 1.2f), new Vector2(1000, 940));
            Text(connectionForm, "CONEXIÓN AWS / WI-FI", new Vector2(0, 425), new Vector2(920, 45), 28, Cyan);
            endpointField = FormField("API HTTPS", 350, 0);
            usernameField = FormField("CUENTA DE SERVICIO", 270, 1);
            passwordField = FormField("CONTRASEÑA · MÁX. 6", 190, 2);
            slotField = Button(connectionForm, "", new Vector2(-280, 110), new Vector2(360, 56), () => {
                profile.slot = profile.slot == "1" ? "2" : profile.slot == "2" ? "3" : "1"; RefreshForm();
            }).Label;
            rememberField = Button(connectionForm, "", new Vector2(180, 110), new Vector2(540, 56), () => {
                profile.remember = CredentialStore.Supported && !profile.remember; RefreshForm();
            }).Label;
            string[] rows = { "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm" };
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Length; c++)
                {
                    string key = rows[r][c].ToString();
                    var button = Button(connectionForm, key, new Vector2((c - (rows[r].Length - 1) / 2f) * 88, 24 - r * 64), new Vector2(80, 56), () => TypeConnection(upperCase ? key.ToUpperInvariant() : key));
                    if (r > 0) letterKeys.Add(button);
                }
            string[] symbols = { ":", "/", ".", "-", "_", "@" };
            for (int i = 0; i < symbols.Length; i++)
            {
                string key = symbols[i];
                Button(connectionForm, key, new Vector2(-396 + i * 88, -232), new Vector2(80, 56), () => TypeConnection(key));
            }
            Button(connectionForm, "Aa", new Vector2(220, -232), new Vector2(150, 56), () => {
                upperCase = !upperCase;
                foreach (var key in letterKeys) key.Label.text = upperCase ? key.Label.text.ToUpperInvariant() : key.Label.text.ToLowerInvariant();
            });
            Button(connectionForm, "Borrar", new Vector2(390, -232), new Vector2(150, 56), () => TypeConnection(null));
            Button(connectionForm, "Vaciar campo", new Vector2(-300, -300), new Vector2(290, 50), () => { SetField(""); RefreshForm(); });
            Button(connectionForm, "Cancelar", new Vector2(0, -300), new Vector2(270, 50), CloseConnectionForm);
            Button(connectionForm, "Probar y conectar", new Vector2(300, -300), new Vector2(290, 50), ConnectEntered, Orange);
            formHint = Text(connectionForm, "", new Vector2(0, -385), new Vector2(920, 100), 20, White);
            RefreshForm(); connectionForm.gameObject.SetActive(false);
        }
        Text FormField(string title, float y, int field)
        {
            Text(connectionForm, title, new Vector2(0, y + 28), new Vector2(920, 24), 15, Muted);
            var label = Button(connectionForm, "", new Vector2(0, y - 6), new Vector2(920, 48), () => { activeField = field; RefreshForm(); }).Label;
            label.richText = false; label.fontSize = 21; return label;
        }
        string FieldValue => activeField == 0 ? profile.endpoint : activeField == 1 ? profile.username : enteredPassword;
        void SetField(string value) { if (activeField == 0) profile.endpoint = value; else if (activeField == 1) profile.username = value; else enteredPassword = value; }
        public void TypeConnection(string key)
        {
            if (!ConfiguringConnection || Busy) return;
            string value = FieldValue ?? "";
            if (key == null) value = value.Length > 0 ? value.Substring(0, value.Length - 1) : "";
            else if (value.Length + key.Length <= (activeField == 0 ? 240 : activeField == 1 ? 64 : 6)) value += key;
            SetField(value); RefreshForm();
        }
        void RefreshForm()
        {
            endpointField.text = profile.endpoint; usernameField.text = profile.username;
            passwordField.text = new string('●', enteredPassword.Length) + "  (" + enteredPassword.Length + "/6)";
            endpointField.color = activeField == 0 ? Orange : White; usernameField.color = activeField == 1 ? Orange : White; passwordField.color = activeField == 2 ? Orange : White;
            slotField.text = "Slot: " + profile.slot + "  ›";
            rememberField.text = CredentialStore.Supported ? "Recordar y reconectar: " + (profile.remember ? "SÍ" : "NO") : "Editor: sesión temporal";
            if (formHint) formHint.text = "Selecciona un campo y usa el teclado. Contraseña sensible a mayúsculas.\nRecordar conecta al abrir la app; nunca despliega automáticamente.";
        }
        public void OpenConnectionForm()
        {
            if (credentialBusy) return;
            DisconnectCloud(); profile = CloudProfile.Read(ProfilePath); profile.autoConnect = false;
            enteredPassword = ""; activeField = 2; SettingsConnection(true); connectionForm.gameObject.SetActive(true);
            Rig.ReleaseForConfiguration(); ConnectingMode = false; HideConnectionPreview(); RefreshForm(); UpdateButtons();
        }
        void CloseConnectionForm()
        {
            if (Busy) return;
            enteredPassword = ""; RefreshForm(); connectionForm.gameObject.SetActive(false); SettingsConnection(false); UpdateButtons();
        }
        void ConnectEntered()
        {
            if (Busy) return;
            try
            {
                var settings = profile.Connection(enteredPassword); settings.Validate();
                profile.autoConnect = false; profile.Save(ProfilePath);
                connectionForm.gameObject.SetActive(false); SettingsConnection(false); ConfigureCloud(settings);
            }
            catch (Exception) { formHint.text = "Revisa URL HTTPS, usuario, contraseña de 1 a 6 caracteres y slot. No se pudo guardar o validar la configuración."; }
        }
        IEnumerator FinishRememberedConnection()
        {
            if (string.IsNullOrEmpty(enteredPassword)) yield break;
            string secret = enteredPassword; enteredPassword = ""; RefreshForm();
            if (!SessionReady) yield break;
            credentialBusy = true; UpdateButtons();
            Task task = profile.remember ? credentialStore.Save(profile.Binding, secret) : credentialStore.Forget();
            secret = null;
            while (!task.IsCompleted) yield return null;
            credentialBusy = false;
            profile.autoConnect = profile.remember && !task.IsFaulted;
            if (task.IsFaulted) { _ = task.Exception; profile.remember = false; SetStatus("Conectado. No se pudo recordar la credencial; vuelve a configurarla al reiniciar."); }
            SaveProfileSafely();
        }
        IEnumerator StartConnection()
        {
            // Discard legacy bootstrap without ever importing its credential.
            try { string legacy = Path.Combine(Application.persistentDataPath, "cloud-session.json"); if (File.Exists(legacy)) File.Delete(legacy); } catch { }
            if (!profile.remember || !profile.autoConnect) { yield return ConnectSession(); yield break; }
            Busy = true; credentialBusy = true; UpdateButtons();
            var task = credentialStore.Load(profile.Binding);
            while (!task.IsCompleted) yield return null;
            credentialBusy = false; Busy = false;
            if (task.IsFaulted || string.IsNullOrEmpty(task.Result))
            {
                if (task.IsFaulted) _ = task.Exception;
                profile.autoConnect = false; SaveProfileSafely();
                SetStatus("Credencial no disponible. Abre Configurar conexión o entra en demo."); sessionText.text = "SIN SESIÓN"; UpdateButtons(); yield break;
            }
            try { ConfigureCloud(profile.Connection(task.Result)); }
            catch { profile.autoConnect = false; SaveProfileSafely(); SetStatus("Perfil inválido. Abre Configurar conexión."); UpdateButtons(); }
        }
        void SaveProfileSafely()
        {
            try { profile.Save(ProfilePath); }
            catch { SetStatus("No se pudieron guardar las preferencias de conexión."); }
        }
        void ConnectSaved()
        {
            if (Busy) return;
            profile = CloudProfile.Read(ProfilePath);
            if (!profile.remember) { OpenConnectionForm(); return; }
            profile.autoConnect = true; SaveProfileSafely(); operation = StartCoroutine(StartConnection());
        }
        void DisconnectCloud()
        {
            if (credentialBusy) return;
            StopLiveInspection();
            if (operation != null) StopCoroutine(operation);
            operation = null; slotOperation = false; inspectedState = null; api.Disconnect(); Busy = false; SessionReady = false; Deployed = false;
            enteredPassword = "";
            if (profile != null) { profile.autoConnect = false; SaveProfileSafely(); }
            sessionText.text = "DESCONECTADO"; RefreshCloudLabels(); UpdateButtons();
            SetStatus("Desconectado. Los recursos AWS permanecen; conecta cuando quieras continuar.");
        }
        void ForgetCredential()
        {
            if (credentialBusy) return;
            DisconnectCloud(); operation = StartCoroutine(ForgetCredentialRoutine());
        }
        IEnumerator ForgetCredentialRoutine()
        {
            Busy = true; credentialBusy = true; UpdateButtons(); profile.remember = profile.autoConnect = false; SaveProfileSafely();
            var task = credentialStore.Forget(); while (!task.IsCompleted) yield return null;
            Busy = credentialBusy = false; operation = null; UpdateButtons();
            if (task.IsFaulted) { _ = task.Exception; SetStatus("No se pudo eliminar la credencial local. Reintenta Olvidar credencial."); }
            else SetStatus("Credencial olvidada. Configura la conexión para volver a entrar.");
        }
    }
}
