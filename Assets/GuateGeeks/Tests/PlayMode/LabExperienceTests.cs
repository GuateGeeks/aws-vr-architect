using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class LabExperienceTests
    {
        ArchitectureLab lab;
        string codeTestDirectory;
        string originalSave;
        bool hadSave;
        string originalCheckpoint;
        bool hadCheckpoint;
        string originalProfile;
        string ProfilePath => Path.Combine(Application.persistentDataPath, "cloud-profile.json");
        string CheckpointPath => Path.Combine(Application.persistentDataPath, "aws-cloud-checkpoint.json");
        readonly Dictionary<string, string> menuPreferences = new Dictionary<string, string>();
        int backgroundPreference;
        bool hadBackgroundPreference;
        bool hadComponentScale;
        float savedComponentScale;
        bool hadSharedRoom, hadStation;
        int sharedRoom, station;
        static readonly string[] MenuNames = { "Lab identity", "Mission status", "01 · Service catalog", "03 · Inspector", "02 · Architecture controls", "Controls reference", "Comfort controls", "04 · Environment settings", "05 · Cloud connection", "Settings console", "Workspace reader" };
        string SavePath => Path.Combine(Application.persistentDataPath, "aws-day-architecture.json");
        [UnitySetUp]
        public IEnumerator Setup()
        {
            originalProfile = File.Exists(ProfilePath) ? File.ReadAllText(ProfilePath) : null;
            if (File.Exists(ProfilePath)) File.Delete(ProfilePath);
            hadSave = File.Exists(SavePath); originalSave = hadSave ? File.ReadAllText(SavePath) : null;
            hadCheckpoint = File.Exists(CheckpointPath); originalCheckpoint = hadCheckpoint ? File.ReadAllText(CheckpointPath) : null;
            menuPreferences.Clear();
            foreach (var name in MenuNames) foreach (var layout in new[] { "", "shared." }) {
                string key = LabMenu.PreferencePrefix + layout + name;
                menuPreferences[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null; PlayerPrefs.DeleteKey(key);
            }
            // Every test starts alone at the front of the table; the shared-room tests opt in.
            hadSharedRoom = PlayerPrefs.HasKey(SharedSpace.SharedPreference); sharedRoom = PlayerPrefs.GetInt(SharedSpace.SharedPreference);
            hadStation = PlayerPrefs.HasKey(SharedSpace.StationPreference); station = PlayerPrefs.GetInt(SharedSpace.StationPreference);
            PlayerPrefs.DeleteKey(SharedSpace.SharedPreference); PlayerPrefs.DeleteKey(SharedSpace.StationPreference);
            hadBackgroundPreference = PlayerPrefs.HasKey(LabEnvironmentSettings.PreferenceKey);
            backgroundPreference = PlayerPrefs.GetInt(LabEnvironmentSettings.PreferenceKey);
            PlayerPrefs.DeleteKey(LabEnvironmentSettings.PreferenceKey);
            hadComponentScale=PlayerPrefs.HasKey(ArchitectureLab.ComponentScalePreference);
            savedComponentScale=PlayerPrefs.GetFloat(ArchitectureLab.ComponentScalePreference,1);
            PlayerPrefs.DeleteKey(ArchitectureLab.ComponentScalePreference);
            yield return SceneManager.LoadSceneAsync("AWSArchitectLab");
            yield return null;
            lab = Object.FindAnyObjectByType<ArchitectureLab>(); Assert.IsNotNull(lab);
            codeTestDirectory=Path.Combine(Path.GetTempPath(),"atlas-play-"+System.Guid.NewGuid().ToString("N"));Inject("codeDirectory",codeTestDirectory);
            yield return WaitIdle();
        }
        [UnityTearDown]
        public IEnumerator Teardown()
        {
            if (originalProfile != null) File.WriteAllText(ProfilePath, originalProfile); else if (File.Exists(ProfilePath)) File.Delete(ProfilePath);
            if (hadSave) File.WriteAllText(SavePath, originalSave); else if (File.Exists(SavePath)) File.Delete(SavePath);
            if (hadCheckpoint) File.WriteAllText(CheckpointPath, originalCheckpoint); else if (File.Exists(CheckpointPath)) File.Delete(CheckpointPath);
            if (lab) Object.Destroy(lab.gameObject); yield return null;
            if(Directory.Exists(codeTestDirectory))Directory.Delete(codeTestDirectory,true);
            foreach (var pair in menuPreferences) { if (pair.Value == null) PlayerPrefs.DeleteKey(pair.Key); else PlayerPrefs.SetString(pair.Key, pair.Value); }
            if (hadBackgroundPreference) PlayerPrefs.SetInt(LabEnvironmentSettings.PreferenceKey, backgroundPreference); else PlayerPrefs.DeleteKey(LabEnvironmentSettings.PreferenceKey);
            if(hadComponentScale)PlayerPrefs.SetFloat(ArchitectureLab.ComponentScalePreference,savedComponentScale);else PlayerPrefs.DeleteKey(ArchitectureLab.ComponentScalePreference);
            if (hadSharedRoom) PlayerPrefs.SetInt(SharedSpace.SharedPreference, sharedRoom); else PlayerPrefs.DeleteKey(SharedSpace.SharedPreference);
            if (hadStation) PlayerPrefs.SetInt(SharedSpace.StationPreference, station); else PlayerPrefs.DeleteKey(SharedSpace.StationPreference);
            PlayerPrefs.Save();
        }
        IEnumerator WaitIdle()
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (lab.Busy && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(lab.Busy, "Operation timed out");
        }
        void Click(string prefix)
        {
            var button = Object.FindObjectsByType<LabTarget>()
                .Where(t => t.isActiveAndEnabled && t.Label && t.Label.text.StartsWith(prefix) && lab.CanInteract(t))
                .OrderByDescending(t => t.Label.text == prefix).FirstOrDefault();
            Assert.IsNotNull(button, "Missing button: " + prefix); Assert.IsTrue(button.Available, "Disabled button: " + prefix); button.Activate();
        }
        void Capture(string name)
        {
            HoloReveal.CompleteAll(); // Panels materialise over ~0.3 s; screenshots show the settled state.
            var camera = Camera.main; var previous = camera.targetTexture;
            var rt = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 }; camera.targetTexture = rt; camera.Render();
            var active = RenderTexture.active; RenderTexture.active = rt;
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
            Directory.CreateDirectory("Validation"); File.WriteAllBytes("Validation/" + name + ".png", texture.EncodeToPNG());
            RenderTexture.active = active; camera.targetTexture = previous; Object.Destroy(rt); Object.Destroy(texture);
        }
        [UnityTest] public IEnumerator OfficialAwsIconsRemainReadableAndDoNotInterceptInteractions()
        {
            lab.Rig.enabled = false;
            var graph = new Architecture();
            for (int i = 0; i < ServiceCatalog.All.Length; i++)
                graph.Add(ServiceCatalog.All[i].Kind, new Vector3((i % 4 - 1.5f) * .65f, 1.45f, 1.9f + (i / 4) * .65f));
            lab.SetGraph(graph);
            yield return null;
            yield return null;
            foreach (var definition in ServiceCatalog.All)
            {
                var texture = AwsServiceIcons.Texture(definition.Kind);
                Assert.IsNotNull(texture, definition.Name);
                Assert.AreEqual(512, texture.width);
                Assert.IsTrue(texture.mipmapCount > 1, "Distance filtering: " + definition.Name);
                Assert.IsTrue(Object.FindObjectsByType<UnityEngine.UI.RawImage>().Any(r => r.texture == texture), "Catalog keeps the official flat icon: " + definition.Name);
                var view = Object.FindObjectsByType<NodeView>().Single(n => n.Model.kind == definition.Kind);
                var emblem = view.Emblem;
                Assert.IsNotNull(emblem, definition.Name); Assert.AreEqual(definition.Kind, emblem.Kind);
                var mesh = emblem.GetComponent<MeshFilter>().sharedMesh;
                Assert.AreSame(AwsIconGeometry.Mesh(definition.Kind), mesh, "One shared mesh per service");
                Assert.Greater(mesh.vertexCount, 300, definition.Name);
                Assert.AreEqual(AwsIconGeometry.Size, mesh.bounds.size.x, .002f); Assert.AreEqual(AwsIconGeometry.Size, mesh.bounds.size.y, .002f);
                Assert.IsEmpty(emblem.GetComponentsInChildren<Collider>(), "The node owns interaction");
                Assert.IsEmpty(view.GetComponentsInChildren<UnityEngine.UI.RawImage>(), "No flat sticker on the tabletop object");
                var direction = emblem.transform.position - Camera.main.transform.position;
                direction.y = 0;
                Assert.Greater(Vector3.Dot(emblem.transform.forward, direction.normalized), .95f, "Symbol faces the viewer: " + definition.Name);
            }
            Click("Lambda");
            yield return null;
            Assert.IsTrue(lab.Placing);
            var ghost = GameObject.Find("Placement preview");
            var ghostEmblem = ghost.GetComponentInChildren<AwsServiceEmblem>();
            Assert.IsNotNull(ghostEmblem); Assert.AreEqual(ServiceKind.Lambda, ghostEmblem.Kind);
            Assert.AreEqual(AwsIconGeometry.Size * .92f * lab.ComponentScale, ghostEmblem.transform.lossyScale.x * AwsIconGeometry.Size, .01f, "Placement shows the real-size emblem");
            Click("Cancelar colocación");
            yield return null;
            Assert.IsFalse(lab.Placing);
            Assert.AreEqual(7, lab.Graph.nodes.Count);
            // Capture the normal starting architecture after checking the complete catalog.
            lab.SetGraph(Architecture.Preset(0));
            yield return null;
            var lambda = Object.FindObjectsByType<NodeView>().Single(n => n.Model.kind == ServiceKind.Lambda);
            lab.Select(lambda);
            yield return null;
            Capture("36-aws-icons-overview");
            var camera = Camera.main;
            var position = camera.transform.position; var rotation = camera.transform.rotation;
            camera.transform.position = new Vector3(-.3f, 1.85f, .75f);
            camera.transform.LookAt(new Vector3(-.1f, 1.45f, 2.2f));
            yield return null;
            Capture("37-aws-icons-tabletop");
            // Move to the other side: the emblem turns so the official symbol stays front-facing and readable.
            camera.transform.position = new Vector3(0, 1.8f, 3.5f);
            camera.transform.LookAt(lambda.transform.position);
            yield return new WaitForSecondsRealtime(1.5f);
            var away = lambda.Emblem.transform.position - camera.transform.position; away.y = 0;
            Assert.Greater(Vector3.Dot(lambda.Emblem.transform.forward, away.normalized), .9f);
            Capture("39-aws-emblem-back");
            camera.transform.SetPositionAndRotation(position, rotation);
        }
        [UnityTest] public IEnumerator VoicePreviewCanCancelApplyAndUndoWithoutAnEarlyMutation()
        {
            lab.SetGraph(Architecture.Preset(0));lab.SetAssistantEnabled(true);
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var voice=lab.gameObject.AddComponent<RealtimeVoice>();
            typeof(RealtimeVoice).GetProperty("Connected").SetValue(voice,true);
            typeof(ArchitectureLab).GetField("assistantVoice",flags).SetValue(lab,voice);
            var pending=(HashSet<string>)typeof(RealtimeVoice).GetField("pendingTools",flags).GetValue(voice);
            string id=lab.Graph.nodes[0].id;string before=JsonUtility.ToJson(lab.Graph);
            void Begin(string callId) {
                pending.Add(callId);
                var type=typeof(ArchitectureLab).GetNestedType("AssistantCall",System.Reflection.BindingFlags.NonPublic);
                var call=System.Activator.CreateInstance(type);
                type.GetField("id").SetValue(call,callId);type.GetField("name").SetValue(call,"spatial_action");
                type.GetField("args").SetValue(call,"{\"baseRevision\":"+lab.ArchitectureRevision+",\"action\":\"resize\",\"nodeIds\":[\""+id+"\"],\"scale\":0.75}");
                lab.StartCoroutine((IEnumerator)typeof(ArchitectureLab).GetMethod("ExecuteAssistantTool",flags).Invoke(lab,new[]{call}));
            }
            Begin("cancel");yield return null;yield return null;
            Assert.IsTrue(lab.VoicePreviewActive);Assert.AreEqual(before,JsonUtility.ToJson(lab.Graph));
            Assert.IsNotNull(GameObject.Find("ATLAS proposed action"));Capture("35-voice-action-preview");
            Assert.That(((TMPro.TMP_Text)typeof(ArchitectureLab).GetField("presenceTarget",flags).GetValue(lab)).text,Does.Contain("75%"));
            lab.CancelInteraction();yield return null;yield return null;
            Assert.IsFalse(lab.VoicePreviewActive);Assert.AreEqual(before,JsonUtility.ToJson(lab.Graph));Assert.IsFalse(pending.Contains("cancel"));
            Begin("apply");yield return null;yield return null;
            lab.ApplyVoicePreviewNow();yield return null;yield return null;
            Assert.AreEqual(.75f,lab.Graph.Find(id).viewScale);Assert.IsFalse(lab.VoicePreviewActive);
            lab.ExecuteVoiceAction("ui_action","{\"baseRevision\":"+lab.ArchitectureRevision+",\"action\":\"undo\"}");
            Assert.AreEqual(before,JsonUtility.ToJson(lab.Graph));
            Begin("stale");yield return null;yield return null;
            lab.Graph.Find(id).name="Manually changed";lab.ApplyVoicePreviewNow();yield return null;yield return null;
            Assert.AreEqual("Manually changed",lab.Graph.Find(id).name);Assert.AreNotEqual(.75f,lab.Graph.Find(id).viewScale);
            lab.SetAssistantEnabled(false);
        }
        [UnityTest] public IEnumerator VoiceCommandsEditComponentsConnectionsAndUndoThroughTheLab()
        {
            lab.Rig.enabled=false;
            string Call(string tool,string fields)=>lab.ExecuteVoiceAction(tool,"{\"baseRevision\":"+lab.ArchitectureRevision+","+fields+"}");
            string initial=JsonUtility.ToJson(lab.Graph);
            Assert.That(Call("component_action","\"action\":\"add\",\"kind\":4,\"name\":\"Orders queue\",\"setting\":0"),Does.Contain("applied"));
            var queue=lab.Graph.nodes.Last();var lambda=lab.Graph.nodes.First(n=>n.kind==ServiceKind.Lambda);
            Assert.AreEqual("Orders queue",queue.name);
            Assert.That(Call("component_action","\"action\":\"update\",\"nodeId\":\""+queue.id+"\",\"name\":\"Pending orders\",\"setting\":1"),Does.Contain("applied"));
            Assert.AreEqual(1,queue.setting);
            string edge="\"from\":\""+lambda.id+"\",\"to\":\""+queue.id+"\"";
            Assert.That(Call("connection_action","\"action\":\"connect\","+edge),Does.Contain("applied"));
            Assert.That(Call("connection_action","\"action\":\"connect\",\"from\":\""+queue.id+"\",\"to\":\""+lambda.id+"\""),Does.Contain("invalid"));
            Assert.That(Call("connection_action","\"action\":\"disconnect\","+edge),Does.Contain("applied"));
            Assert.That(Call("component_action","\"action\":\"remove\",\"nodeId\":\""+queue.id+"\""),Does.Contain("applied"));
            for(int i=0;i<5;i++)Assert.That(Call("ui_action","\"action\":\"undo\""),Does.Contain("completed"));
            Assert.AreEqual(initial,JsonUtility.ToJson(lab.Graph));Assert.IsFalse(lab.Deployed);
            yield return null;
        }
        [UnityTest] public IEnumerator VoiceActionsRespectStaleContextManualDraftsAndAwsConfirmation()
        {
            lab.Rig.enabled=false;int revision=lab.ArchitectureRevision;
            string Action(string name)=>lab.ExecuteVoiceAction("ui_action","{\"baseRevision\":"+lab.ArchitectureRevision+",\"action\":\""+name+"\"}");
            lab.AddResource(ServiceKind.SQS);
            Assert.That(lab.ExecuteVoiceAction("component_action","{\"baseRevision\":"+revision+",\"action\":\"add\",\"kind\":1,\"setting\":0,\"name\":\"Stale\"}"),Does.Contain("stale"));
            var node=lab.Graph.nodes.First(n=>n.kind==ServiceKind.Lambda);lab.Select(lab.Views[node.id]);Click("256 MB");
            Assert.That(Action("arrange"),Does.Contain("blocked"));
            Assert.That(Action("apply_changes"),Does.Contain("completed"));Assert.AreEqual(1,node.setting);
            Assert.That(Action("confirm_deployment"),Does.Contain("blocked"));
            Assert.That(Action("delete_stack"),Does.Contain("blocked"));Assert.IsFalse(lab.Busy);Assert.IsFalse(lab.Deployed);
            Assert.That(Action("preview_flow"),Does.Contain("blocked"),"Disconnected queue must not be reported as a running preview.");
            Assert.That(Action("open_deployment_review"),Does.Contain("blocked"),"Invalid graph must not be reported as an open deployment review.");
            if(File.Exists(SavePath))File.Delete(SavePath);
            Assert.That(Action("load"),Does.Contain("unavailable"),"Missing save must not be reported as loaded.");
            Assert.That(lab.ExecuteVoiceAction("component_action","{}"),Does.Contain("stale"));
            yield return null;
        }
        [UnityTest] public IEnumerator ComponentSizingPreservesDeploymentAndAppliesToNewObjects()
        {
            lab.Rig.enabled=false;Click("Desplegar demo");Click("Confirmar simulación");yield return WaitIdle();
            string definition=DesignSemantics.Definition(lab.Graph);int revision=lab.ArchitectureRevision;
            Click("Ajustes");Click("Espacio");Click("Compacto 50%");Click("Distribuir en la mesa");
            Assert.AreEqual(.5f,lab.ComponentScale);Assert.IsTrue(lab.Deployed);Assert.AreEqual(revision,lab.ArchitectureRevision);
            Assert.AreEqual(definition,DesignSemantics.Definition(lab.Graph));
            Assert.IsTrue(lab.Views.Values.All(v=>v.transform.localScale==Vector3.one*.5f));
            Assert.IsFalse(lab.SetComponentScale(float.NaN));Assert.IsFalse(lab.SetComponentScale(.1f));
            Click("Cerrar ajustes");lab.AddResource(ServiceKind.S3);
            Assert.AreEqual(Vector3.one*.5f,lab.Views[lab.Graph.nodes.Last().id].transform.localScale);
            yield return null;Capture("30-compact-components");
        }
        [UnityTest] public IEnumerator AssistantStaysEnabledWhenHiddenUntilExplicitlyDisabled()
        {
            lab.Rig.enabled=false;lab.OpenAssistant();Click("Activar ATLAS");Assert.IsTrue(lab.AssistantEnabled);
            Assert.IsFalse(Object.FindObjectsByType<LabTarget>().Any(t=>t.Label && (t.Label.text=="Hablar" || t.Label.text=="Enviar voz")));
            Click("Panel");Click("Cerrar ATLAS");yield return new WaitForSecondsRealtime(.2f);Assert.IsTrue(lab.AssistantEnabled);
            lab.StartPlacement(ServiceKind.S3);
            Click("ATLAS · ACTIVO");Click("Desactivar ATLAS");yield return new WaitForSecondsRealtime(.2f);Assert.IsFalse(lab.AssistantEnabled);lab.CancelInteraction();
            yield return null;Capture("29-atlas-handsfree");
        }
        [UnityTest] public IEnumerator SpatialVoiceMovesAndResizesOnlyTheReferencedNodesWithUndo()
        {
            lab.Rig.enabled=false;lab.Select(lab.Views[lab.Graph.nodes[1].id]);
            string id=lab.Graph.nodes[1].id;string definition=DesignSemantics.Definition(lab.Graph);Vector3 original=lab.Graph.Find(id).position;
            var point=new Vector3(0,1.5f,1.8f);var ray=new Ray(point+Vector3.up*2,Vector3.down);
            lab.SetAssistantEnabled(true);
            lab.ObserveVoicePointer("right",null,ray);yield return new WaitForSecondsRealtime(.35f);lab.ObserveVoicePointer("right",null,ray);lab.FreezeVoicePointing();
            var context=JsonUtility.FromJson<SpatialContextFixture>(lab.AssistantContextJson());Assert.IsTrue(context.pointing.hasLocation);
            string Args(string fields)=>"{\"baseRevision\":"+lab.ArchitectureRevision+",\"nodeIds\":[\""+id+"\"],"+fields+"}";
            Assert.That(lab.ExecuteVoiceAction("spatial_action",Args("\"action\":\"move\",\"locationId\":\""+context.pointing.locationId+"\"")),Does.Contain("applied"));
            Assert.Less(Vector3.Distance(point,lab.Graph.Find(id).position),.0001f);Assert.AreEqual(definition,DesignSemantics.Definition(lab.Graph));
            Assert.That(lab.ExecuteVoiceAction("spatial_action",Args("\"action\":\"resize\",\"scale\":0.5")),Does.Contain("applied"));
            Assert.AreEqual(Vector3.one*.5f,lab.Views[id].transform.localScale);Assert.AreEqual(Vector3.one,lab.Views[lab.Graph.nodes[0].id].transform.localScale);
            Assert.That(lab.ExecuteVoiceAction("ui_action","{\"baseRevision\":"+lab.ArchitectureRevision+",\"action\":\"undo\"}"),Does.Contain("completed"));
            Assert.AreEqual(0,lab.Graph.Find(id).viewScale);Assert.Less(Vector3.Distance(point,lab.Graph.Find(id).position),.0001f);
            lab.ExecuteVoiceAction("ui_action","{\"baseRevision\":"+lab.ArchitectureRevision+",\"action\":\"undo\"}");Assert.AreEqual(original,lab.Graph.Find(id).position);
            Assert.That(lab.ExecuteVoiceAction("spatial_action",Args("\"action\":\"move\",\"locationId\":\"invented\"")),Does.Contain("stale"));
            lab.SetAssistantEnabled(false);
        }
        [System.Serializable] sealed class SpatialContextFixture {public SpatialVoiceContext.Snapshot pointing;}
        [UnityTest] public IEnumerator VoiceMoveUsesVisibleTableBelowControllerAndShowsItsDestination()
        {
            lab.Rig.enabled=false;lab.SetAssistantEnabled(true);string id=lab.Graph.nodes[2].id;
            var surface=new Vector3(1.15f,ArchitectureLab.VoiceTableHeight,3.15f);var origin=new Vector3(0,1.25f,.25f);var ray=new Ray(origin,surface-origin);
            Physics.SyncTransforms();
            var pick=typeof(LabRig).GetMethod("Pick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var target=(LabTarget)pick.Invoke(lab.Rig,new object[]{ray,0f});
            Assert.IsFalse(target && target.Node,"Fixture points at empty table, possibly through a floating button.");
            lab.ObserveVoicePointer("right",target,ray);yield return new WaitForSecondsRealtime(.35f);lab.ObserveVoicePointer("right",target,ray);lab.BeginVoicePointing();
            yield return new WaitForSecondsRealtime(.12f);Assert.IsTrue(lab.VoiceDestinationVisible);Capture("32-atlas-tabletop-destination");
            lab.ObserveVoicePointer("right",null,new Ray(origin,Vector3.up));yield return new WaitForSecondsRealtime(.4f);lab.FreezeVoicePointing();
            var context=JsonUtility.FromJson<SpatialContextFixture>(lab.AssistantContextJson());Assert.IsTrue(context.pointing.hasLocation);
            string result=lab.ExecuteVoiceAction("spatial_action","{\"baseRevision\":"+lab.ArchitectureRevision+",\"nodeIds\":[\""+id+"\"],\"action\":\"move\",\"locationId\":\""+context.pointing.locationId+"\"}");
            Assert.That(result,Does.Contain("applied"));Assert.Less(Vector3.Distance(new Vector3(surface.x,1.5f,surface.z),lab.Graph.Find(id).position),.0001f);
            lab.SetAssistantEnabled(false);Assert.IsFalse(lab.VoiceDestinationVisible);
        }
        [UnityTest] public IEnumerator VoiceSizeAndArrangeIsOneUndoAndCompactResultStaysVisible()
        {
            lab.Rig.enabled=false;lab.SetAssistantEnabled(true);string before=JsonUtility.ToJson(lab.Graph);
            Assert.That(lab.ExecuteVoiceAction("set_component_size","{\"baseRevision\":"+lab.ArchitectureRevision+",\"scale\":0.5,\"arrange\":true}"),Does.Contain("applied"));
            yield return new WaitForSecondsRealtime(.2f);Assert.AreEqual("HECHO",lab.AssistantPresenceState);
            Capture("31-atlas-compact-feedback");Click("Deshacer");
            Assert.AreEqual(1,lab.ComponentScale);Assert.AreEqual(before,JsonUtility.ToJson(lab.Graph));
            Click("Desactivar");Assert.IsFalse(lab.AssistantEnabled);
        }
        [UnityTest] public IEnumerator InterruptingPreviewOrManuallyEditingPreventsDelayedVoiceMutation()
        {
            lab.Rig.enabled=false;lab.OpenAssistant();var voice=lab.gameObject.AddComponent<RealtimeVoice>();Inject("assistantVoice",voice);
            typeof(RealtimeVoice).GetProperty("Connected").SetValue(voice,true);
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var pending=(HashSet<string>)typeof(RealtimeVoice).GetField("pendingTools",flags).GetValue(voice);
            var type=typeof(ArchitectureLab).GetNestedType("AssistantCall",System.Reflection.BindingFlags.NonPublic);
            var method=typeof(ArchitectureLab).GetMethod("ExecuteAssistantTool",flags);
            string target=lab.Graph.nodes[1].id;
            for(int iteration=0;iteration<2;iteration++) {
                string callId="preview-"+iteration;pending.Add(callId);var call=System.Activator.CreateInstance(type);
                type.GetField("id").SetValue(call,callId);type.GetField("name").SetValue(call,"component_action");
                type.GetField("args").SetValue(call,"{\"action\":\"remove\",\"baseRevision\":"+lab.ArchitectureRevision+",\"nodeId\":\""+target+"\"}");
                lab.StartCoroutine((IEnumerator)method.Invoke(lab,new[]{call}));yield return null;yield return null;
                Assert.IsTrue(lab.VoicePreviewActive);Assert.IsNotNull(lab.Graph.Find(target));
                if(iteration==0)voice.Interrupt();else lab.AddResource(ServiceKind.S3);
                yield return new WaitForSecondsRealtime(.85f);Assert.IsNotNull(lab.Graph.Find(target));Assert.IsFalse(lab.VoicePreviewActive);
            }
        }
        [UnityTest] public IEnumerator WorkflowReviewCombinesGraphLayoutAndSizeWithSingleUndo()
        {
            lab.Rig.enabled=false;lab.OpenAssistant();string before=JsonUtility.ToJson(lab.Graph);float scale=lab.ComponentScale;var next=Architecture.Preset(1);
            string proposal=JsonUtility.ToJson(new AssistantProposal{baseRevision=lab.ArchitectureRevision,nodes=next.nodes.ToArray(),links=next.links.ToArray(),summary="Plan"});
            proposal=proposal.Substring(0,proposal.Length-1)+",\"componentScale\":0.65,\"arrange\":true,\"openReview\":true}";
            Assert.That(lab.ProposeAssistantWorkflow(proposal),Does.Contain("pending_user_review"));Assert.AreEqual(before,JsonUtility.ToJson(lab.Graph));Assert.AreEqual(scale,lab.ComponentScale);
            yield return new WaitForSecondsRealtime(.2f);
            Click("Aplicar propuesta");Assert.AreEqual(.65f,lab.ComponentScale);Assert.AreEqual(4,lab.Graph.nodes.Count);Assert.IsFalse(lab.Deployed);
            yield return null;Capture("32-workflow-review");yield return new WaitForSecondsRealtime(.2f);Click("Deshacer IA");Assert.AreEqual(before,JsonUtility.ToJson(lab.Graph));Assert.AreEqual(scale,lab.ComponentScale);
            Assert.That(lab.ProposeAssistantWorkflow(proposal),Does.Contain("invalid_proposal"));Assert.IsFalse(lab.AssistantHasProposal);
        }
        [UnityTest] public IEnumerator CodeDraftRejectsStaleHashAndKeyboardPreservesPythonIndentation()
        {
            lab.Rig.enabled=false;string directory=Path.Combine(Path.GetTempPath(),"atlas-editor-"+System.Guid.NewGuid().ToString("N"));Inject("codeDirectory",directory);
            try {
                var node=lab.Graph.nodes.First(n=>n.kind==ServiceKind.Lambda);lab.Select(lab.Views[node.id]);Click("Código Lambda");
                string source="def handler(event, context):\n    return event\n";
                string Proposal(string hash)=>"{\"nodeId\":\""+node.id+"\",\"baseRevision\":"+lab.ArchitectureRevision+",\"baseSourceHash\":\""+hash+"\",\"source\":\""+source.Replace("\n","\\n")+"\"}";
                Assert.That(lab.ProposeLambdaCode(Proposal("old")),Does.Contain("stale"));Assert.AreNotEqual(source,lab.LambdaDraftSource);
                Assert.That(lab.ProposeLambdaCode(Proposal(LambdaCodeDraft.Hash(lab.LambdaDraftSource))),Does.Contain("draft_saved"));Assert.AreEqual(source,lab.LambdaDraftSource);
                Assert.IsFalse(lab.GetComponentsInChildren<LabTarget>().Single(t=>t.Label && t.Label.text=="Confirmar código en AWS").Available);
                Click("Línea +");Click("Editar línea");Click("Guardar texto");Assert.AreEqual(source,lab.LambdaDraftSource);
                yield return null;Capture("33-code-editor");
            }finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
        }
        [UnityTest] public IEnumerator SupportedCreationDefaultsAndLocalRemovalAreUndoable()
        {
            const string key="GuateGeeks.ComponentDefault.1";bool existed=PlayerPrefs.HasKey(key);int previous=PlayerPrefs.GetInt(key);
            try {
                lab.Rig.enabled=false;PlayerPrefs.SetInt(key,2);lab.AddResource(ServiceKind.Lambda);
                var node=lab.Graph.nodes.Last();Assert.AreEqual(2,node.setting);string before=JsonUtility.ToJson(lab.Graph);
                Click("Quitar objeto");Assert.IsNull(lab.Graph.Find(node.id));
                Click("Deshacer");Assert.AreEqual(before,JsonUtility.ToJson(lab.Graph));
                yield return null;
            }finally{if(existed)PlayerPrefs.SetInt(key,previous);else PlayerPrefs.DeleteKey(key);}
        }
        [UnityTest] public IEnumerator LambdaExamplesAndReusableCasesStayLocal()
        {
            lab.Rig.enabled=false;var node=lab.Graph.nodes.First(n=>n.kind==ServiceKind.Lambda);lab.OpenLambdaEditor(node.id);
            Click("Evento ejemplo");Click("Guardar caso");Click("Guardar texto");
            var file=Directory.GetFiles(codeTestDirectory,"*.json").Single();
            var draft=JsonUtility.FromJson<LambdaCodeDraft>(File.ReadAllText(file));
            Assert.AreEqual(1,draft.testCases.Length);Assert.That(draft.testCases[0].eventJson,Does.Contain("body"));
            Click("Elegir caso");Click("Borrar caso");
            draft=JsonUtility.FromJson<LambdaCodeDraft>(File.ReadAllText(file));Assert.IsEmpty(draft.testCases);
            Assert.IsFalse(lab.Deployed);yield return null;
        }
        [UnityTest] public IEnumerator CodeReviewCannotSilentlySwitchRollbackVersionOrConfirmFromVoice()
        {
            lab.Rig.enabled=false;var transport=new OfflineCloudTransport();var fixture=JsonUtility.FromJson<CloudFixture>(File.ReadAllText(Path.Combine(Application.dataPath,"GuateGeeks/Tests/Editor/Fixtures/backend-contract.json")));
            transport.Responses.Enqueue(fixture.session);transport.Responses.Enqueue(fixture.catalog);
            lab.ConfigureCloud(new CloudConnection{endpoint="https://example.invalid/prod",username="test",password="Test42",deploymentId="1"},transport,_=>null);yield return WaitIdle();
            var cloud=(AwsCloudApi)typeof(ArchitectureLab).GetField("api",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(lab);
            typeof(AwsCloudApi).GetProperty("StackId").SetValue(cloud,"stack-test");typeof(ArchitectureLab).GetProperty("Deployed").SetValue(lab,true);
            var node=lab.Graph.nodes.First(n=>n.kind==ServiceKind.Lambda);lab.OpenLambdaEditor(node.id);
            transport.Responses.Enqueue(new CloudReply{Code=200,Json=JsonUtility.ToJson(new AwsCloudApi.CodeResult{source=LambdaCodeDraft.Starter,revisionId="r1",updateStatus="Successful",versions=new[]{new AwsCloudApi.CodeVersion{version="1"},new AwsCloudApi.CodeVersion{version="2"}}})});
            Click("Cargar AWS");float until=Time.realtimeSinceStartup+5;while(lab.CodeBusy && Time.realtimeSinceStartup<until)yield return null;Assert.IsFalse(lab.CodeBusy);
            Click("Revisar restauración");var confirm=lab.GetComponentsInChildren<LabTarget>().Single(t=>t.Label && t.Label.text=="Confirmar código en AWS");Assert.IsTrue(confirm.Available);
            Click("Elegir versión");Assert.IsFalse(confirm.Available);Assert.IsFalse(transport.Requests.Any(r=>r.StartsWith("POST")));
            Click("Revisar restauración");typeof(AwsCloudApi).GetProperty("StackId").SetValue(cloud,"replacement");yield return null;Assert.IsFalse(confirm.Available);
        }
        [UnityTest] public IEnumerator ExpectedOutputCannotPassAgainstAnOlderBackend()
        {
            lab.Rig.enabled=false;var transport=new OfflineCloudTransport();var fixture=JsonUtility.FromJson<CloudFixture>(File.ReadAllText(Path.Combine(Application.dataPath,"GuateGeeks/Tests/Editor/Fixtures/backend-contract.json")));
            transport.Responses.Enqueue(fixture.session);transport.Responses.Enqueue(fixture.catalog);
            lab.ConfigureCloud(new CloudConnection{endpoint="https://example.invalid/prod",username="test",password="Test42",deploymentId="1"},transport,_=>null);yield return WaitIdle();
            var node=lab.Graph.nodes.First(n=>n.kind==ServiceKind.Lambda);lab.OpenLambdaEditor(node.id);
            var draft=(LambdaCodeDraft)typeof(ArchitectureLab).GetField("codeDraft",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(lab);
            draft.expectedOutput="42";
            transport.Responses.Enqueue(new CloudReply{Code=200,Json="{\"valid\":true,\"passed\":true,\"output\":\"42\"}"});
            Click("Probar borrador");float until=Time.realtimeSinceStartup+5;while(lab.CodeBusy && Time.realtimeSinceStartup<until)yield return null;
            Assert.IsFalse(lab.CodeBusy);Assert.IsFalse(draft.Tested);
            transport.Responses.Enqueue(new CloudReply{Code=200,Json="{\"valid\":true,\"passed\":true,\"expectedChecked\":true,\"output\":\"42\"}"});
            Click("Probar borrador");until=Time.realtimeSinceStartup+5;while(lab.CodeBusy && Time.realtimeSinceStartup<until)yield return null;
            Assert.IsFalse(lab.CodeBusy);Assert.IsTrue(draft.Tested);
        }
        [UnityTest] public IEnumerator ReviewedCodePublicationStaysLockedThroughAwsPolling()
        {
            lab.Rig.enabled=false;var transport=new OfflineCloudTransport();var fixture=JsonUtility.FromJson<CloudFixture>(File.ReadAllText(Path.Combine(Application.dataPath,"GuateGeeks/Tests/Editor/Fixtures/backend-contract.json")));
            transport.Responses.Enqueue(fixture.session);transport.Responses.Enqueue(fixture.catalog);
            lab.ConfigureCloud(new CloudConnection{endpoint="https://example.invalid/prod",username="test",password="Test42",deploymentId="1"},transport,_=>null);yield return WaitIdle();
            var cloud=(AwsCloudApi)typeof(ArchitectureLab).GetField("api",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(lab);
            typeof(AwsCloudApi).GetProperty("StackId").SetValue(cloud,"stack-test");typeof(ArchitectureLab).GetProperty("Deployed").SetValue(lab,true);
            var node=lab.Graph.nodes.First(n=>n.kind==ServiceKind.Lambda);lab.OpenLambdaEditor(node.id);
            CloudReply Code(string revision,string state)=>new CloudReply{Code=200,Json=JsonUtility.ToJson(new AwsCloudApi.CodeResult{source=LambdaCodeDraft.Starter,revisionId=revision,updateStatus=state,versions=new[]{new AwsCloudApi.CodeVersion{version="1"}}})};
            IEnumerator WaitCode(){float deadline=Time.realtimeSinceStartup+8;while(lab.CodeBusy && Time.realtimeSinceStartup<deadline)yield return null;Assert.IsFalse(lab.CodeBusy);}
            transport.Responses.Enqueue(Code("r1","Successful"));Click("Cargar AWS");yield return WaitCode();
            transport.Responses.Enqueue(new CloudReply{Code=200,Json="{\"valid\":true,\"message\":\"Syntax valid\"}"});Click("Validar");yield return WaitCode();Click("Revisar publicación");
            Assert.AreEqual(1,transport.Requests.Count(r=>r.StartsWith("POST")),"Only validation ran before manual confirmation.");
            transport.Responses.Enqueue(new CloudReply{Code=202,Json="{\"revisionId\":\"r2\",\"updateStatus\":\"InProgress\",\"rollbackVersion\":\"1\",\"message\":\"Accepted\"}"});
            transport.Responses.Enqueue(Code("r2","InProgress"));transport.Responses.Enqueue(Code("r3","Successful"));Click("Confirmar código en AWS");yield return new WaitForSecondsRealtime(.4f);
            Assert.IsTrue(lab.CodeBusy);Assert.IsFalse(lab.CanInteract(lab.Views[node.id].Target));yield return WaitCode();
            Assert.AreEqual(1,transport.Requests.Count(r=>r.EndsWith("/code/publish")));Assert.IsTrue(lab.GetComponentsInChildren<TMPro.TMP_Text>().Any(t=>t.text.Contains("AWS · Successful")));
        }
        [UnityTest] public IEnumerator DiagnosticsAutomaticallyReadAndStopOnCloseWithoutSendingEvents()
        {
            lab.Rig.enabled=false;var transport=new OfflineCloudTransport();var fixture=JsonUtility.FromJson<CloudFixture>(File.ReadAllText(Path.Combine(Application.dataPath,"GuateGeeks/Tests/Editor/Fixtures/backend-contract.json")));
            transport.Responses.Enqueue(fixture.session);transport.Responses.Enqueue(fixture.catalog);
            lab.ConfigureCloud(new CloudConnection{endpoint="https://example.invalid/prod",username="test",password="Test42",deploymentId="1"},transport,_=>null);yield return WaitIdle();
            var cloud=(AwsCloudApi)typeof(ArchitectureLab).GetField("api",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(lab);
            typeof(AwsCloudApi).GetProperty("StackId").SetValue(cloud,"stack-test");typeof(AwsCloudApi).GetProperty("LastEventId").SetValue(cloud,"event-test");typeof(ArchitectureLab).GetProperty("Deployed").SetValue(lab,true);
            var node=lab.Graph.nodes.First(n=>n.kind==ServiceKind.Lambda);
            CloudReply Evidence(int n)=>new CloudReply{Code=200,Json=JsonUtility.ToJson(new AwsCloudApi.InspectionPage{stackId="stack-test",resourceId=node.id,entries=new[]{new AwsCloudApi.InspectionEntry{eventId="event-test",text="observed "+n,stage="delivered"}}})};
            transport.Responses.Enqueue(Evidence(1));Assert.That(lab.StartDiagnostics(new[]{node.id}),Does.Contain("monitoring"));yield return new WaitForSecondsRealtime(.3f);
            Assert.That(lab.DiagnosticsContextJson(),Does.Contain("observed 1"));transport.Responses.Enqueue(Evidence(2));yield return new WaitForSecondsRealtime(5.3f);
            Assert.That(lab.DiagnosticsContextJson(),Does.Contain("observed 2"));Assert.IsFalse(transport.Requests.Any(r=>r.StartsWith("POST")));Capture("34-live-diagnostics");
            Click("Cerrar diagnóstico");Assert.IsFalse(lab.DiagnosticsActive);
        }
        [UnityTest] public IEnumerator AssistantProposalsAreAtomicUndoableAndNeverDeployAutomatically()
        {
            lab.Rig.enabled=false;lab.OpenAssistant();var original=JsonUtility.ToJson(lab.Graph);
            var next=Architecture.Preset(1);
            var proposal=new AssistantProposal{baseRevision=lab.ArchitectureRevision,nodes=next.nodes.ToArray(),links=next.links.ToArray(),summary="Agregar cola"};
            Assert.That(lab.ProposeAssistantArchitecture(JsonUtility.ToJson(proposal)),Does.Contain("pending_user_review"));
            Assert.AreEqual(original,JsonUtility.ToJson(lab.Graph));Assert.IsTrue(lab.AssistantHasProposal);Assert.IsFalse(lab.Deployed);
            yield return null;Capture("28-atlas-proposal");
            Click("Aplicar propuesta");Assert.AreEqual(4,lab.Graph.nodes.Count);Assert.IsFalse(lab.AssistantHasProposal);Assert.IsFalse(lab.Deployed);
            yield return new WaitForSecondsRealtime(.2f);Click("Deshacer IA");Assert.AreEqual(original,JsonUtility.ToJson(lab.Graph));
            Assert.That(lab.ProposeAssistantArchitecture(JsonUtility.ToJson(proposal)),Does.Contain("invalid_proposal"));
            proposal.baseRevision=lab.ArchitectureRevision;
            lab.ProposeAssistantArchitecture(JsonUtility.ToJson(proposal));lab.ApplyAssistantProposal();
            var node=lab.Views.Values.First();Assert.IsTrue(lab.BeginGrab(node));node.transform.localPosition+=Vector3.right*.2f;lab.EndGrab(node);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.IsFalse(Object.FindObjectsByType<LabTarget>().Single(t=>t.Label && t.Label.text=="Deshacer IA").Available,"AI undo must not undo a later manual move.");
            Click("Cerrar ATLAS");
        }
        [UnityTest] public IEnumerator AssistantDiscardsProposalAfterManualEditAndCannotOverwriteDraft()
        {
            lab.Rig.enabled=false;lab.OpenAssistant();var next=Architecture.Preset(1);
            string Propose()=>lab.ProposeAssistantArchitecture(JsonUtility.ToJson(new AssistantProposal{baseRevision=lab.ArchitectureRevision,nodes=next.nodes.ToArray(),links=next.links.ToArray()}));
            Propose();Assert.IsTrue(lab.AssistantHasProposal);
            Click("Procesar archivos");var changed=JsonUtility.ToJson(lab.Graph);lab.ApplyAssistantProposal();Assert.AreEqual(changed,JsonUtility.ToJson(lab.Graph));Assert.IsFalse(lab.AssistantHasProposal);
            lab.Select(lab.Views[lab.Graph.nodes.First(n=>n.kind==ServiceKind.Lambda).id]);Click("512 MB");
            Assert.IsTrue(lab.HasPendingDefinition);Assert.That(Propose(),Does.Contain("blocked"));Assert.IsFalse(lab.AssistantHasProposal);
            Click("Cancelar edición");Click("Cerrar ATLAS");yield return null;
        }
        [UnityTest] public IEnumerator DedicatedObjectInspectorDoesNotReplaceReaderAndSettingsShareOnePanel()
        {
            lab.Rig.enabled = false;
            Click("Slots / inspección"); yield return null;
            var reader = lab.GetComponentsInChildren<LabMenu>().Single(m => m.name == "Workspace reader");
            var readerTitle = reader.GetComponentsInChildren<TMPro.TMP_Text>().Single(t => t.text == "SLOTS / INSPECCIÓN AWS");
            lab.Select(lab.Views[lab.Graph.nodes[1].id]); yield return null;
            Assert.IsTrue(readerTitle && readerTitle.gameObject.activeInHierarchy);
            var inspector = lab.GetComponentsInChildren<LabMenu>().Single(m => m.name == "03 · Inspector");
            Assert.IsTrue(inspector.GetComponentsInChildren<TMPro.TMP_Text>().Any(t => t.text.Contains(lab.Graph.nodes[1].name)));
            lab.Select(lab.Views[lab.Graph.nodes[2].id]); yield return null;
            Assert.IsTrue(inspector.GetComponentsInChildren<TMPro.TMP_Text>().Any(t => t.text.Contains(lab.Graph.nodes[2].name)));
            Capture("16-dedicated-inspector");
            Click("Ajustes"); yield return null;
            Assert.IsFalse(reader.gameObject.activeSelf);
            Click("Cerrar ajustes"); Assert.IsTrue(reader.gameObject.activeSelf);
            Click("Volver al diseño"); Click("Ajustes"); yield return null;
            var settings = lab.GetComponentsInChildren<LabMenu>().Single(m => m.name == "Settings console");
            Assert.AreEqual(1, settings.GetComponentsInChildren<LabMenu>(true).Length);
            Click("Espacio"); yield return null; Capture("17-unified-settings");
            Assert.IsTrue(settings.GetComponentsInChildren<LabTarget>().Any(t => t.Label && t.Label.text == "Passthrough"));
            Click("Controles"); yield return null; Capture("18-hand-controls");
            Click("Conexión AWS"); yield return null; Capture("19-aws-settings");
            Click("Configurar conexión"); yield return null;
            Assert.IsTrue(lab.ConfiguringConnection);
            Assert.IsTrue(settings.GetComponentsInChildren<LabTarget>().Any(t => t.Label && t.Label.text == "Probar y conectar"));
            Click("Cancelar"); Click("Cerrar ajustes"); Assert.IsFalse(settings.gameObject.activeSelf);
        }
        [UnityTest] public IEnumerator StudioPlacementDraftKeyboardAndReviewRemainExplicit()
        {
            lab.Rig.enabled = false;
            Click("SQS"); Assert.IsTrue(lab.Placing); Assert.AreEqual(3, lab.Graph.nodes.Count);
            lab.PreviewPlacement(lab.Graph.nodes[0].position); lab.ConfirmPlacement(); Assert.IsTrue(lab.Placing, "Overlapping placement must not commit");
            lab.CancelInteraction(); Assert.IsFalse(lab.Placing); Assert.AreEqual(3, lab.Graph.nodes.Count);
            lab.StartPlacement(ServiceKind.SQS); lab.PreviewPlacement(new Vector3(0, 1.5f, 1.7f)); lab.ConfirmPlacement();
            var queue = lab.Graph.nodes.Last(); Assert.AreEqual(ServiceKind.SQS, queue.kind);
            Click("FIFO"); Assert.AreEqual(0, queue.setting); Click("Cancelar edición"); Assert.AreEqual(0, queue.setting);
            Click("FIFO"); Click("Aplicar cambios"); Assert.AreEqual(1, queue.setting);
            Click("Nombre:"); Assert.IsTrue(lab.EditingText); Assert.IsFalse(lab.BeginGrab(lab.Views.Values.First()));
            Assert.IsFalse(lab.CanInteract(lab.Views.Values.First().Target));
            Click("Vaciar"); lab.TypeDesignText("Pedidos"); Click("Aceptar nombre"); Assert.AreNotEqual("Pedidos", queue.name);
            Assert.IsTrue(lab.HasPendingDefinition); Click("Desplegar demo"); Assert.IsFalse(lab.Busy);
            Click("Aplicar cambios"); Assert.AreEqual("Pedidos", queue.name);
            yield return null; Capture("09-studio-definition");
            Click("Revisar diseño"); yield return null; Capture("10-studio-review");
            Click("Ir al objeto"); Assert.IsTrue(lab.GetComponentsInChildren<TMPro.TMP_Text>().Any(t => t.text == "03  /  DEFINIR OBJETO"));
        }
        [UnityTest] public IEnumerator FlowIsFiniteOrderedAndNeverAnimatesObservationEdges()
        {
            lab.Rig.enabled = false;
            var fn = lab.Graph.nodes.First(n => n.kind == ServiceKind.Lambda);
            lab.AddResource(ServiceKind.CloudWatch); var monitor = lab.Graph.nodes.Last();
            lab.SelectPort(lab.Views[fn.id], true); lab.SelectPort(lab.Views[monitor.id], false); lab.CancelInteraction();
            Assert.AreEqual(3, lab.Graph.links.Count);
            lab.PreviewFlow(); Assert.IsTrue(lab.FlowPreviewActive);
            yield return new WaitForSecondsRealtime(.3f);
            var links = lab.GetComponentsInChildren<LinkView>();
            Assert.IsTrue(links.Single(l => l.FromId == lab.Graph.nodes[0].id && !l.IsObservation).PacketVisible);
            Assert.IsFalse(links.Single(l => l.FromId == fn.id && !l.IsObservation).PacketVisible, "Downstream illustration waits its turn");
            Assert.IsFalse(links.Single(l => l.IsObservation).PacketVisible);
            Capture("11-studio-flow");
            yield return new WaitForSecondsRealtime(4.6f);
            Assert.IsFalse(lab.FlowPreviewActive); Assert.IsTrue(links.All(l => !l.PacketVisible)); Assert.IsFalse(lab.Deployed);
            lab.InspectLink(fn.id, monitor.id); Click("Quitar enlace"); Assert.AreEqual(2, lab.Graph.links.Count);
            Click("Deshacer"); Assert.AreEqual(3, lab.Graph.links.Count);
        }
        [UnityTest] public IEnumerator NamedLibraryAndLayoutUndoPreserveDefinition()
        {
            lab.Rig.enabled = false;
            string directory = Path.Combine(Path.GetTempPath(), "gg-vr-library-test-" + System.Guid.NewGuid().ToString("N"));
            Inject("libraryDirectory", directory);
            try {
                Click("Biblioteca"); Click("Guardar diseño con nombre"); Click("Vaciar"); lab.TypeDesignText("Demo reutilizable"); Click("Aceptar nombre");
                yield return null; Capture("12-studio-library");
                var entry = new DesignLibrary(directory).Read().Single(); Assert.AreEqual("Demo reutilizable", entry.name);
                Click("Volver al diseño"); Click("Eventos + cola"); Assert.AreEqual(4, lab.Graph.nodes.Count);
                Click("Biblioteca"); Click("Abrir este diseño"); Assert.AreEqual(3, lab.Graph.nodes.Count);
                Click("Desplegar demo"); Click("Confirmar simulación"); yield return WaitIdle(); Assert.IsTrue(lab.Deployed);
                var original = lab.Graph.nodes.Select(n => n.position).ToArray();
                Click("Ordenar"); Assert.IsTrue(lab.Deployed); Click("Deshacer"); Assert.IsTrue(lab.Deployed);
                CollectionAssert.AreEqual(original, lab.Graph.nodes.Select(n => n.position).ToArray());
                lab.Select(lab.Views[lab.Graph.nodes[1].id]); Click("512 MB"); Click("Aplicar cambios"); Assert.IsFalse(lab.Deployed);
            } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
        [System.Serializable] sealed class CloudFixture
        {
            public Architecture graph;
            public CloudReply session, catalog, validation, created, ready, accepted;
        }
        sealed class OfflineCloudTransport : ICloudTransport
        {
            public bool Stall;
            public readonly Queue<CloudReply> Responses = new Queue<CloudReply>();
            public readonly List<string> Requests = new List<string>();
            public IEnumerator Send(string method, string url, string authorization, string json, System.Action<CloudReply> complete)
            {
                Requests.Add(method + " " + url); yield return null;
                while (Stall) yield return null;
                Assert.IsNotEmpty(Responses); complete(Responses.Dequeue());
            }
            public void Abort() { Stall = false; }
        }
        [UnityTest] public IEnumerator SlotInspectionAndCleanupRequireExplicitConfirmation()
        {
            lab.Rig.enabled = false;
            var fixture = JsonUtility.FromJson<CloudFixture>(File.ReadAllText(Path.Combine(Application.dataPath, "GuateGeeks/Tests/Editor/Fixtures/backend-contract.json")));
            var transport = new OfflineCloudTransport();
            transport.Responses.Enqueue(fixture.session); transport.Responses.Enqueue(fixture.catalog);
            lab.ConfigureCloud(new CloudConnection { endpoint = "https://example.invalid/prod", username = "test", password = "Test42", deploymentId = "1" }, transport, _ => null);
            yield return WaitIdle();
            var state = JsonUtility.FromJson<AwsCloudApi.DeploymentStatus>(fixture.ready.Json);
            state.nodes = new[] { new AwsCloudApi.NodeStatus { resourceId = "table", name = "Pedidos", kind = 2, physicalId = "ggawsday-demo-1-table", status = "CREATE_COMPLETE" } };
            var ready = new CloudReply { Code = 200, Json = JsonUtility.ToJson(state) };
            transport.Responses.Enqueue(ready);
            Click("Slots / inspección"); Click("Slot 1"); yield return WaitIdle();
            yield return null; Capture("13-slots");
            Click("DynamoDB · Pedidos");
            transport.Responses.Enqueue(new CloudReply { Code = 200, Json = JsonUtility.ToJson(new AwsCloudApi.InspectionPage {
                stackId = state.stackId, resourceId = "table", message = "Datos reales de prueba contractual · solo lectura",
                entries = new[] { new AwsCloudApi.InspectionEntry { title = "Ítem DynamoDB", text = "{\n  \"id\": {\"S\": \"pedido-001\"},\n  \"message\": {\"S\": \"<b>literal</b>\"}\n}" } }, cursor = "" }) });
            Click("Inspeccionar ítems"); yield return new WaitForSecondsRealtime(.3f);
            var body = lab.GetComponentsInChildren<TMPro.TMP_Text>().Single(t => t.text.Contains("<b>literal</b>")); Assert.IsFalse(body.richText);
            transport.Responses.Enqueue(new CloudReply { Code = 200, Json = JsonUtility.ToJson(new AwsCloudApi.InspectionPage {
                stackId=state.stackId,resourceId="table",entries=new[] {new AwsCloudApi.InspectionEntry { title="Ítem DynamoDB",text="nuevo pedido automático" }},cursor="" }) });
            yield return new WaitForSecondsRealtime(3.2f);
            Assert.IsTrue(lab.GetComponentsInChildren<TMPro.TMP_Text>().Any(t=>t.text=="nuevo pedido automático"));
            Assert.IsFalse(lab.GetComponentsInChildren<TMPro.TMP_Text>().Any(t=>t.text.Contains("<b>literal</b>")),"Unpinned stale items leave the completed snapshot");
            Capture("14-items");
            Click("Volver al recurso"); Click("Volver a slots"); Click("Limpiar slot 1");
            Assert.IsFalse(transport.Requests.Any(r => r.StartsWith("DELETE")));
            yield return null; Capture("15-cleanup-confirmation");
            Click("Cancelar"); Assert.IsFalse(transport.Requests.Any(r => r.StartsWith("DELETE")));
            Click("Limpiar slot 1");
            transport.Responses.Enqueue(ready);
            transport.Responses.Enqueue(new CloudReply { Code = 202, Json = "{\"status\":\"DELETE_IN_PROGRESS\"}" });
            transport.Responses.Enqueue(new CloudReply { Code = 404 });
            var originalGraph = JsonUtility.ToJson(lab.Graph);
            Click("Sí, eliminar slot 1"); yield return WaitIdle();
            Assert.AreEqual(1, transport.Requests.Count(r => r.StartsWith("DELETE")));
            Assert.AreEqual(originalGraph, JsonUtility.ToJson(lab.Graph));
        }
        [UnityTest] public IEnumerator LiveReaderRefreshesPreservesSelectionAndStopsWhenClosed()
        {
            lab.Rig.enabled = false;
            var fixture = JsonUtility.FromJson<CloudFixture>(File.ReadAllText(Path.Combine(Application.dataPath, "GuateGeeks/Tests/Editor/Fixtures/backend-contract.json")));
            var transport = new OfflineCloudTransport();
            transport.Responses.Enqueue(fixture.session); transport.Responses.Enqueue(fixture.catalog);
            lab.ConfigureCloud(new CloudConnection { endpoint="https://example.invalid/prod", username="test", password="Test42", deploymentId="1" },transport,_=>null);
            yield return WaitIdle();
            var state=JsonUtility.FromJson<AwsCloudApi.DeploymentStatus>(fixture.ready.Json);
            state.nodes=new[] { new AwsCloudApi.NodeStatus { resourceId="fn", name="Procesador",kind=1,physicalId="test-function",status="CREATE_COMPLETE" } };
            transport.Responses.Enqueue(new CloudReply { Code=200,Json=JsonUtility.ToJson(state) });
            Click("Slots / inspección"); Click("Slot 1"); yield return WaitIdle(); Click("Lambda · Procesador");
            CloudReply Page(string cursor,params string[] messages) => new CloudReply { Code=200,Json=JsonUtility.ToJson(new AwsCloudApi.InspectionPage {
                stackId=state.stackId,resourceId="fn",cursor=cursor,entries=messages.Select(m=>new AwsCloudApi.InspectionEntry {title=m,text=m+"\n"+new string('x',600)}).ToArray() }) };
            transport.Responses.Enqueue(Page("", "uno"));
            Click("Inspeccionar logs"); yield return new WaitForSecondsRealtime(.3f);
            Assert.IsTrue(lab.LiveInspectionActive); Assert.IsFalse(lab.Busy);
            Assert.IsFalse(lab.GetComponentsInChildren<LabTarget>().Any(t=>t.Label && t.Label.text=="Actualizar"));
            Click("1 · uno"); Click("Texto siguiente");
            transport.Responses.Enqueue(Page("next", "uno")); transport.Responses.Enqueue(Page("", "dos"));
            yield return new WaitForSecondsRealtime(6.5f);
            Assert.IsTrue(transport.Requests.Any(r=>r.Contains("cursor=next")),"Automatically follows AWS pagination");
            Assert.IsTrue(lab.GetComponentsInChildren<TMPro.TMP_Text>().Any(t=>t.text=="Texto 2 / 2"),"Selected text page survives refresh");
            Assert.IsTrue(lab.GetComponentsInChildren<LabTarget>().Any(t=>t.Label && t.Label.text=="1 · dos"));
            Click("Pausar lectura"); int count=transport.Requests.Count;
            yield return new WaitForSecondsRealtime(3.3f); Assert.AreEqual(count,transport.Requests.Count);
            Click("Ajustes"); yield return new WaitForSecondsRealtime(.2f); Click("Cerrar ajustes");
            Click("Seguir recientes");
            transport.Responses.Enqueue(new CloudReply {Code=409,Json="{\"message\":\"El slot cambió\"}"});
            yield return new WaitForSecondsRealtime(.3f);
            Assert.IsFalse(lab.LiveInspectionActive); Assert.IsFalse(lab.Busy);
            Assert.IsTrue(lab.GetComponentsInChildren<TMPro.TMP_Text>().Any(t=>t.text.Contains("HTTP 409")));
            Capture("20-live-reader");
            Click("Ajustes"); Click("Desconectar"); Click("Cerrar ajustes");
            Click("Volver al recurso"); count=transport.Requests.Count;
            yield return new WaitForSecondsRealtime(3.2f); Assert.AreEqual(count,transport.Requests.Count);
            Assert.IsFalse(transport.Requests.Any(r=>r.StartsWith("DELETE") || r.StartsWith("POST")));
        }
        [UnityTest] public IEnumerator IncrementalReaderMergesStableIdsAndRestartsForFilters()
        {
            lab.Rig.enabled=false;
            var fixture=JsonUtility.FromJson<CloudFixture>(File.ReadAllText(Path.Combine(Application.dataPath,"GuateGeeks/Tests/Editor/Fixtures/backend-contract.json")));
            var transport=new OfflineCloudTransport();transport.Responses.Enqueue(fixture.session);transport.Responses.Enqueue(fixture.catalog);
            lab.ConfigureCloud(new CloudConnection {endpoint="https://example.invalid/prod",username="test",password="Test42",deploymentId="1"},transport,_=>null);
            yield return WaitIdle();
            var state=JsonUtility.FromJson<AwsCloudApi.DeploymentStatus>(fixture.ready.Json);
            state.nodes=new[]{new AwsCloudApi.NodeStatus{resourceId="fn",name="Procesador",kind=1,physicalId="test-function",status="CREATE_COMPLETE"}};
            transport.Responses.Enqueue(new CloudReply{Code=200,Json=JsonUtility.ToJson(state)});
            Click("Slots / inspección");Click("Slot 1");yield return WaitIdle();Click("Lambda · Procesador");
            var a=new AwsCloudApi.InspectionEntry{id="a",title="pedido A",text="{\"id\":\"pedido\",\"n\":10000000000000000001}",timestamp=10,level="ERROR",eventId="pedido"};
            var b=new AwsCloudApi.InspectionEntry{id="b",title="pedido B",text=a.text,timestamp=20,level="ERROR",eventId="pedido"};
            CloudReply Page(bool incremental,params AwsCloudApi.InspectionEntry[] entries)=>new CloudReply{Code=200,Json=JsonUtility.ToJson(new AwsCloudApi.InspectionPage{stackId=state.stackId,resourceId="fn",entries=entries,resume="resume-token",incremental=incremental,inspectionVersion=2})};
            transport.Responses.Enqueue(Page(false,a));Click("Inspeccionar logs");yield return new WaitForSecondsRealtime(.3f);
            transport.Responses.Enqueue(Page(true,a,b));yield return new WaitForSecondsRealtime(3.2f);
            Assert.IsTrue(transport.Requests.Last().Contains("resume=resume-token"));
            Assert.IsTrue(lab.GetComponentsInChildren<TMPro.TMP_Text>().Any(t=>t.text=="REGISTROS · 2 en memoria"));
            Assert.IsTrue(lab.GetComponentsInChildren<TMPro.TMP_Text>().Any(t=>t.text.Contains("10000000000000000001")));
            transport.Responses.Enqueue(Page(false,b));Click("Nivel: TODOS");yield return new WaitForSecondsRealtime(.3f);
            Assert.That(transport.Requests.Last(),Does.Contain("level=ERROR"));Assert.That(transport.Requests.Last(),Does.Not.Contain("resume=resume-token"));
            Click("Buscar:");Click("Vaciar");lab.TypeDesignText("pedido");transport.Responses.Enqueue(Page(false,b));Click("Aplicar filtro");yield return new WaitForSecondsRealtime(.3f);
            Assert.That(transport.Requests.Last(),Does.Contain("q=pedido"));
            Click("Pausar lectura");
            var camera=Camera.main;var previousPosition=camera.transform.position;var previousRotation=camera.transform.rotation;
            var reader=lab.GetComponentsInChildren<LabMenu>().Single(m=>m.name=="Workspace reader");
            camera.transform.position=reader.transform.position-reader.transform.forward*1.8f;camera.transform.rotation=reader.transform.rotation;
            Capture("27-filtered-reader");camera.transform.SetPositionAndRotation(previousPosition,previousRotation);
            Click("Volver al recurso");Assert.IsFalse(lab.LiveInspectionActive);
        }
        [UnityTest] public IEnumerator ArmoredHandsHaveVolumeAndEnvironmentMotionCanFreeze()
        {
            lab.Rig.enabled=false;
            var reactor=lab.GetComponentsInChildren<Transform>().Single(t=>t.name=="Reactor outer turbine");
            var before=reactor.localRotation; yield return new WaitForSecondsRealtime(.15f); Assert.AreNotEqual(before,reactor.localRotation);
            lab.Feedback.ToggleMotion(); yield return null; before=reactor.localRotation;
            yield return new WaitForSecondsRealtime(.15f); Assert.AreEqual(before,reactor.localRotation);
            Capture("21-reactor-lab");
            {
                // The horizon band and floor light belong to the virtual room (hidden with the background / passthrough).
                var horizon = lab.Environment.VirtualRoom.GetComponentsInChildren<Transform>().Single(t => t.name == "Guatemala volcano horizon");
                Assert.IsTrue(horizon.GetComponent<MeshRenderer>().enabled);
                Assert.IsNotNull(lab.Environment.VirtualRoom.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Teal floor ribbon"));
                var cam = Camera.main; var p0 = cam.transform.position; var r0 = cam.transform.rotation;
                cam.transform.SetPositionAndRotation(new Vector3(0, 1.6f, .2f), Quaternion.Euler(-8, 58, 0)); yield return null; Capture("40-volcano-horizon");
                cam.transform.position = new Vector3(.9f, 2.55f, .95f); cam.transform.LookAt(new Vector3(0, .9f, 2.55f)); yield return null; Capture("41-table-and-stands");
                Assert.IsTrue(lab.GetComponentsInChildren<LineRenderer>().Count(l => l.name == "Table power trace" && l.enabled) >= 2, "Each object has a table trace");
                // No back wall or racks; GuateGeeks signs with live eyes and the digital quetzal are present.
                var names = lab.Environment.VirtualRoom.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList();
                Assert.IsFalse(names.Contains("Reactor containment wall")); Assert.IsFalse(names.Contains("Compute bay"));
                Assert.AreEqual(2, names.Count(n => n.StartsWith("GuateGeeks sign")));
                Assert.GreaterOrEqual(lab.GetComponentsInChildren<GeekEyes>().Length, 3, "Eyes mark on both signs and in the reactor core");
                var quetzal = lab.GetComponentInChildren<DigitalQuetzal>(true); Assert.IsNotNull(quetzal);
                quetzal.Preview(.33f); yield return null;
                Assert.IsFalse(quetzal.Flying, "Reduced motion keeps the quetzal grounded");
                lab.Feedback.ToggleMotion(); quetzal.Preview(.33f); yield return null; yield return null;
                Assert.IsTrue(quetzal.Flying); Assert.IsTrue(quetzal.Bird.gameObject.activeInHierarchy);
                cam.transform.position = new Vector3(0, 1.7f, .1f); cam.transform.LookAt(quetzal.Bird.position); yield return new WaitForSecondsRealtime(.25f);
                cam.transform.LookAt(quetzal.Bird.position); yield return null; Capture("42-digital-quetzal");
                cam.transform.position = new Vector3(0, 1.75f, .3f); cam.transform.LookAt(new Vector3(0, 2.7f, 8)); yield return null; Capture("43-guategeeks-signs");
                cam.transform.SetPositionAndRotation(p0, r0);
            }
            var poses=new Vector3[26]; var valid=Enumerable.Repeat(true,26).ToArray();
            // Synthetic open pose for deterministic rendering; this is not headset evidence.
            poses[0]=new Vector3(0,0,.04f); poses[1]=Vector3.zero;
            for(int i=0;i<4;i++) poses[2+i]=new Vector3(-.036f-i*.016f,0,.027f+i*.023f);
            for(int f=0;f<4;f++) for(int j=0;j<5;j++) poses[6+f*5+j]=new Vector3((f-1.5f)*.021f,0,.025f+j*(f==3?.021f:.027f));
            var root=new GameObject("Synthetic glove validation"); root.transform.SetParent(lab.transform,false);
            var hand=root.AddComponent<ArmoredHandVisual>(); hand.Initialize(true); hand.ApplyPoses(poses,valid,Quaternion.identity);
            var mesh=root.GetComponent<MeshFilter>().sharedMesh;
            Assert.Greater(mesh.bounds.size.y,.03f); Assert.Greater(mesh.vertexCount,1000); Assert.IsTrue(root.GetComponent<MeshRenderer>().enabled);
            root.transform.position=lab.Rig.ViewCamera.transform.TransformPoint(new Vector3(0,-.03f,.48f));
            root.transform.rotation=Quaternion.Euler(-70,0,0); root.transform.localScale=Vector3.one*1.5f;
            yield return null; Capture("22-armored-hand");
            valid[1]=false; hand.ApplyPoses(poses,valid,Quaternion.identity); Assert.IsFalse(root.GetComponent<MeshRenderer>().enabled);
            Object.Destroy(root);
        }
        [UnityTest] public IEnumerator GuidedDemoRequiresConfirmationAndCompletesLocalWorkflow()
        {
            lab.Rig.enabled=false; var original=JsonUtility.ToJson(lab.Graph);
            lab.OpenGuidedDemo(); Assert.AreEqual(original,JsonUtility.ToJson(lab.Graph));
            Click("Empezar con mi diseño"); Click("Revisar y continuar"); Assert.AreEqual(2,lab.GuidedStep);
            Click("Abrir despliegue"); Assert.IsFalse(lab.Deployed); Assert.IsFalse(lab.Busy);
            Capture("25-guided-confirmation");
            Click("Confirmar simulación"); yield return WaitIdle(); yield return new WaitForSecondsRealtime(.3f);
            Assert.AreEqual(3,lab.GuidedStep); Click("Enviar evento"); yield return WaitIdle();yield return new WaitForSecondsRealtime(.3f);
            Assert.AreEqual(4,lab.GuidedStep);Click("Entendido: simulado");Click("Limpiar mesa");Assert.AreEqual(3,lab.Graph.nodes.Count);
            Click("Sí, limpiar");yield return new WaitForSecondsRealtime(.3f);Assert.AreEqual(6,lab.GuidedStep);
            Capture("26-guided-complete");
        }
        [UnityTest] public IEnumerator ConfirmedLinksAndPreviewAreDistinctAndNeverAnimateObservation()
        {
            lab.Rig.enabled=false; lab.AddResource(ServiceKind.CloudWatch);
            var fn=lab.Graph.nodes.First(n=>n.kind==ServiceKind.Lambda);var monitor=lab.Graph.nodes.Last();
            lab.SelectPort(lab.Views[fn.id],true);lab.SelectPort(lab.Views[monitor.id],false);lab.CancelInteraction();
            var links=lab.GetComponentsInChildren<LinkView>();
            var data=links.First(l=>!l.IsObservation); var observation=links.Single(l=>l.IsObservation);
            data.ConfirmObserved("event-1");observation.ConfirmObserved("event-1");yield return null;
            Assert.IsTrue(data.ObservedConfirmation);Assert.IsTrue(data.PacketVisible);Assert.IsFalse(observation.PacketVisible);
            Assert.IsTrue(data.GetComponentsInChildren<TMPro.TMP_Text>().Any(t=>t.text=="CONFIRMADO · AWS"));
            data.Preview(0,1,false);yield return null;Assert.IsFalse(data.ObservedConfirmation);
            Assert.IsTrue(data.GetComponentsInChildren<TMPro.TMP_Text>().Any(t=>t.text.StartsWith("SIMULACIÓN")));
        }
        sealed class MemoryVault : ICredentialStore
        {
            public string Password, Binding;
            public bool Fail;
            public System.Threading.Tasks.Task<string> Load(string binding) => System.Threading.Tasks.Task.FromResult(Binding == binding ? Password : null);
            public System.Threading.Tasks.Task Save(string binding, string password) {
                if (Fail) return System.Threading.Tasks.Task.FromException(new System.IO.IOException());
                Binding = binding; Password = password; return System.Threading.Tasks.Task.CompletedTask;
            }
            public System.Threading.Tasks.Task Forget() { Password = null; return System.Threading.Tasks.Task.CompletedTask; }
        }
        void Inject(string name, object value) => typeof(ArchitectureLab).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(lab, value);
        [UnityTest] public IEnumerator VrKeyboardMasksLimitsPasswordAndBlocksGraph()
        {
            Click("Ajustes"); Click("Configurar conexión"); Assert.IsTrue(lab.ConfiguringConnection);
            foreach (char c in "Ab12CdX") lab.TypeConnection(c.ToString());
            var texts = lab.GetComponentsInChildren<TMPro.TMP_Text>().Select(t => t.text).ToArray();
            Assert.IsTrue(texts.Any(t => t.Contains("(6/6)")));
            Assert.IsFalse(texts.Any(t => t.Contains("Ab12Cd")));
            Assert.IsFalse(lab.BeginGrab(lab.Views.Values.First()));
            var outside = lab.GetComponentsInChildren<LabTarget>().First(t => t.Node);
            Assert.IsFalse(lab.CanInteract(outside));
            Capture("08-wireless-connection-form");
            lab.TypeConnection(null);
            Assert.IsTrue(lab.GetComponentsInChildren<TMPro.TMP_Text>().Any(t => t.text.Contains("(5/6)")));
            Click("Cancelar"); Assert.IsFalse(lab.ConfiguringConnection); Assert.IsFalse(lab.SessionReady);
            Click("Volver a demo"); yield return WaitIdle(); Assert.IsTrue(lab.SessionReady);
        }
        [UnityTest] public IEnumerator RememberOnlyAfterSessionAndCatalogThenDisconnectAndForget()
        {
            var fixture = JsonUtility.FromJson<CloudFixture>(File.ReadAllText(Path.Combine(Application.dataPath, "GuateGeeks/Tests/Editor/Fixtures/backend-contract.json")));
            var vault = new MemoryVault(); Inject("credentialStore", vault);
            var profile = new CloudProfile { remember = true }; Inject("profile", profile); Inject("enteredPassword", "Ab12Cd");
            var transport = new OfflineCloudTransport(); transport.Responses.Enqueue(fixture.session); transport.Responses.Enqueue(fixture.catalog);
            lab.ConfigureCloud(profile.Connection("Ab12Cd"), transport, _ => null);
            yield return WaitIdle();
            Assert.AreEqual("Ab12Cd", vault.Password); Assert.IsTrue(CloudProfile.Read(ProfilePath).autoConnect);
            Assert.That(File.ReadAllText(ProfilePath), Does.Not.Contain("Ab12Cd"));
            Assert.AreEqual(2, transport.Requests.Count); Assert.IsFalse(lab.Deployed);
            Click("Ajustes"); Click("Desconectar"); Assert.IsFalse(lab.SessionReady); Assert.IsFalse(CloudProfile.Read(ProfilePath).autoConnect);
            Click("Olvidar credencial"); yield return WaitIdle(); Assert.IsNull(vault.Password); Assert.IsFalse(CloudProfile.Read(ProfilePath).remember);
        }
        [UnityTest] public IEnumerator RejectedSessionNeverStoresCredential()
        {
            var vault = new MemoryVault(); Inject("credentialStore", vault);
            var profile = new CloudProfile { remember = true }; Inject("profile", profile); Inject("enteredPassword", "Ab12Cd");
            var transport = new OfflineCloudTransport(); transport.Responses.Enqueue(new CloudReply { Code = 401 });
            lab.ConfigureCloud(profile.Connection("Ab12Cd"), transport, _ => null); yield return WaitIdle();
            Assert.IsFalse(lab.SessionReady); Assert.IsNull(vault.Password); Assert.IsFalse(CloudProfile.Read(ProfilePath).autoConnect);
        }
        [UnityTest] public IEnumerator VaultFailureKeepsOnlyTemporarySession()
        {
            var fixture = JsonUtility.FromJson<CloudFixture>(File.ReadAllText(Path.Combine(Application.dataPath, "GuateGeeks/Tests/Editor/Fixtures/backend-contract.json")));
            var vault = new MemoryVault { Fail = true }; Inject("credentialStore", vault);
            var profile = new CloudProfile { remember = true }; Inject("profile", profile); Inject("enteredPassword", "Ab12Cd");
            var transport = new OfflineCloudTransport(); transport.Responses.Enqueue(fixture.session); transport.Responses.Enqueue(fixture.catalog);
            lab.ConfigureCloud(profile.Connection("Ab12Cd"), transport, _ => null); yield return WaitIdle();
            Assert.IsTrue(lab.SessionReady); Assert.IsFalse(CloudProfile.Read(ProfilePath).remember); Assert.IsFalse(CloudProfile.Read(ProfilePath).autoConnect);
        }
        [UnityTest]
        public IEnumerator SlowCloudConnectionCanBeStoppedBeforeSessionExists()
        {
            var transport = new OfflineCloudTransport { Stall = true };
            lab.ConfigureCloud(new CloudConnection { endpoint = "https://example.invalid/prod", username = "contract", password = "Test42" }, transport, _ => null);
            yield return null; Assert.IsTrue(lab.Busy); Assert.IsFalse(lab.SessionReady);
            Click("Ajustes"); Click("Dejar de observar AWS"); yield return null;
            Assert.IsFalse(lab.Busy); Assert.IsFalse(lab.SessionReady); Assert.AreEqual(1, transport.Requests.Count);
            Click("Volver a demo"); yield return WaitIdle(); Assert.IsTrue(lab.SessionReady);
        }
        [UnityTest]
        public IEnumerator CloudControlsRequireExplicitCreationAndKeepDemoSeparate()
        {
            var fixture = JsonUtility.FromJson<CloudFixture>(File.ReadAllText(Path.Combine(Application.dataPath, "GuateGeeks/Tests/Editor/Fixtures/backend-contract.json")));
            var transport = new OfflineCloudTransport();
            transport.Responses.Enqueue(fixture.session); transport.Responses.Enqueue(fixture.catalog);
            lab.SetGraph(fixture.graph);
            lab.ConfigureCloud(new CloudConnection { endpoint = "https://example.invalid/prod", username = "contract", password = "Test42" }, transport, _ => null);
            yield return WaitIdle(); Assert.IsTrue(lab.IsCloud); Assert.IsTrue(lab.SessionReady);
            Assert.IsFalse(lab.SimulateFailure); Assert.AreEqual(2, transport.Requests.Count);
            Click("Desplegar / retomar AWS");
            Assert.AreEqual(2, transport.Requests.Count, "Preview/confirmation must not create resources");
            Capture("06-cloud-confirmation");
            transport.Responses.Enqueue(fixture.validation); transport.Responses.Enqueue(fixture.created); transport.Responses.Enqueue(fixture.ready);
            Click("Confirmar creación AWS"); yield return WaitIdle();
            Assert.IsTrue(lab.Deployed); Assert.IsTrue(lab.Graph.nodes.All(n => n.state == ResourceState.Ready));
            Assert.That(File.ReadAllText(CheckpointPath), Does.Not.Contain("Test42"));
            lab.Select(lab.Views["node1"]);
            transport.Responses.Enqueue(fixture.ready); transport.Responses.Enqueue(fixture.accepted);
            Click("Enviar evento de prueba"); yield return WaitIdle(); Assert.AreEqual(1, lab.EventCount);
            var text = Object.FindObjectsByType<TMPro.TMP_Text>().Where(t => t.isActiveAndEnabled).Select(t => t.text).ToArray();
            Assert.IsTrue(text.Any(t => t.Contains("procesamiento aún no confirmado")));
            Assert.IsFalse(text.Any(t => t.Contains("142 ms") || t.Contains("SIN COSTOS AWS") || t.Contains("SIMULADO")));
            Capture("07-cloud-ready");
            Click("Eventos + cola"); Assert.IsFalse(lab.Deployed); Click("Ajustes");
            Click("Recuperar diseño AWS"); Assert.AreEqual("node1", lab.Graph.nodes[1].id);
            Assert.IsFalse(lab.Deployed, "Recovery does not claim a live deployment is ready before querying AWS");
            Click("Volver a demo"); yield return WaitIdle();
            Assert.IsFalse(lab.IsCloud); Assert.IsFalse(lab.Deployed); Assert.IsTrue(lab.SessionReady);
            Assert.IsFalse(transport.Requests.Any(r => r.StartsWith("DELETE")));
        }
        [UnityTest] public IEnumerator AssistantReadsTableWithoutEventAndCleanupRequiresPhysicalConfirmation()
        {
            lab.Rig.enabled=false;
            var fixture=JsonUtility.FromJson<CloudFixture>(File.ReadAllText(Path.Combine(Application.dataPath,"GuateGeeks/Tests/Editor/Fixtures/backend-contract.json")));
            var transport=new OfflineCloudTransport();transport.Responses.Enqueue(fixture.session);transport.Responses.Enqueue(fixture.catalog);
            lab.SetGraph(fixture.graph);
            lab.ConfigureCloud(new CloudConnection{endpoint="https://example.invalid/prod",username="contract",password="Test42",deploymentId="1"},transport,_=>null);
            yield return WaitIdle();Click("Desplegar / retomar AWS");
            transport.Responses.Enqueue(fixture.validation);transport.Responses.Enqueue(fixture.created);transport.Responses.Enqueue(fixture.ready);
            Click("Confirmar creación AWS");yield return WaitIdle();
            var voice=lab.gameObject.AddComponent<RealtimeVoice>();typeof(RealtimeVoice).GetProperty("Connected").SetValue(voice,true);Inject("assistantVoice",voice);
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var pending=(HashSet<string>)typeof(RealtimeVoice).GetField("pendingTools",flags).GetValue(voice);pending.Add("read");
            var table=lab.Graph.nodes.First(n=>n.kind==ServiceKind.DynamoDB);
            transport.Responses.Enqueue(new CloudReply{Code=200,Json=JsonUtility.ToJson(new AwsCloudApi.InspectionPage{
                stackId=JsonUtility.FromJson<AwsCloudApi.DeploymentStatus>(fixture.ready.Json).stackId,resourceId=table.id,cursor="next-page",entries=new[]{new AwsCloudApi.InspectionEntry{text="{\"secret\":\"private\",\"amount\":42}"}}
            })});
            string result=null;
            var method=typeof(ArchitectureLab).GetMethod("ExecuteAssistantCloudTool",flags);
            yield return (IEnumerator)method.Invoke(lab,new object[]{"read_component","{\"nodeId\":\""+table.id+"\",\"cursor\":\"\"}","read",new System.Action<string>(value=>result=value)});
            Assert.That(result,Does.Contain("next-page"));Assert.That(result,Does.Contain("REDACTED"));Assert.That(result,Does.Not.Contain("private"));
            Assert.That(transport.Requests.Last(),Does.Contain("eventId="));Assert.AreEqual(0,lab.EventCount);
            pending.Add("cleanup");transport.Responses.Enqueue(fixture.ready);
            yield return (IEnumerator)method.Invoke(lab,new object[]{"slot_action","{\"slot\":\"1\",\"action\":\"review_cleanup\"}","cleanup",new System.Action<string>(value=>result=value)});
            Assert.That(result,Does.Contain("pending_manual_confirmation"));Assert.IsFalse(transport.Requests.Any(r=>r.StartsWith("DELETE")));
            Click("Cancelar");Assert.IsTrue(lab.Deployed);
        }
        [UnityTest]
        public IEnumerator FailedCloudConnectionStaysExplicitlyOfflineUntilDemoIsSelected()
        {
            var transport = new OfflineCloudTransport(); transport.Responses.Enqueue(new CloudReply { Code = 401 });
            lab.ConfigureCloud(new CloudConnection { endpoint = "https://example.invalid/prod", username = "contract", password = "Test42" }, transport, _ => null);
            yield return WaitIdle(); Assert.IsTrue(lab.IsCloud); Assert.IsFalse(lab.SessionReady);
            Assert.IsFalse(lab.Deployed);
            Assert.IsFalse(Object.FindObjectsByType<LabTarget>().Single(t => t.Label && t.Label.text.StartsWith("Desplegar / retomar AWS")).Available);
            Click("Ajustes"); Click("Volver a demo"); yield return WaitIdle(); Assert.IsTrue(lab.SessionReady); Assert.IsFalse(lab.IsCloud);
        }
        [UnityTest]
        public IEnumerator FocusPanelsSettleOpaqueAfterTheirReveal()
        {
            Click("Ajustes"); yield return new WaitForSecondsRealtime(.6f);
            var settings = lab.GetComponentsInChildren<LabMenu>().Single(m => m.name == "Settings console");
            var glass = settings.GetComponent<HoloPanelGraphic>(); var group = settings.GetComponent<CanvasGroup>();
            // Linear colour space makes any partial coverage very visible; reading panels must settle fully opaque and on top.
            Assert.AreEqual(1, glass.Reveal, .001f); Assert.AreEqual(1, group.alpha, .001f); Assert.AreEqual(1, glass.color.a, .001f);
            Assert.AreEqual(LabVisuals.FocusSortingOrder, settings.GetComponent<Canvas>().sortingOrder);
        }
        [UnityTest]
        public IEnumerator ContextRingActsOnTheSelectedHologram()
        {
            lab.Rig.enabled = false;
            Assert.IsFalse(lab.ContextRingVisible, "No selection, no ring");
            var fn = lab.Views.Values.First(v => v.Model.kind == ServiceKind.Lambda);
            lab.Select(fn); yield return null;
            Assert.IsTrue(lab.ContextRingVisible);
            var ring = GameObject.Find("Object context ring");
            Assert.Less(Vector3.Distance(ring.transform.position, fn.transform.position), .5f, "The ring blooms at the object");
            var actions = ring.GetComponentsInChildren<LabTarget>().Where(t => t.isActiveAndEnabled).Select(t => t.Label.text).ToArray();
            CollectionAssert.IsSubsetOf(new[] { "CONECTAR", "RELACIONES", "FICHA", "CÓDIGO", "QUITAR" }, actions);
            CollectionAssert.DoesNotContain(actions, "DIAGNÓSTICO", "Diagnostics are AWS-only");
            foreach (var label in ring.GetComponentsInChildren<TMPro.TMP_Text>()) { label.ForceMeshUpdate(); Assert.IsFalse(label.isTextTruncated, "Clipped ring label: " + label.text); }
            Assert.IsFalse(ring.GetComponentsInChildren<LabTarget>().Any(t => t.GetComponentInParent<LabMenu>()), "The ring is not a movable menu");
            yield return new WaitForSecondsRealtime(.3f); Capture("38-context-ring");
            Click("CONECTAR"); Assert.IsTrue(lab.ConnectingMode); Assert.AreEqual(1, fn.ConnectionHint);
            yield return null; Assert.IsFalse(lab.ContextRingVisible, "Connecting hides the ring");
            lab.CancelInteraction(); lab.Select(fn); yield return null; Assert.IsTrue(lab.ContextRingVisible);
            int nodes = lab.Graph.nodes.Count, links = lab.Graph.links.Count;
            Click("QUITAR"); Assert.AreEqual(nodes - 1, lab.Graph.nodes.Count);
            yield return null; Assert.IsFalse(lab.ContextRingVisible, "Removed object has no ring");
            Click("Deshacer"); Assert.AreEqual(nodes, lab.Graph.nodes.Count); Assert.AreEqual(links, lab.Graph.links.Count);
            var db = lab.Views.Values.First(v => v.Model.kind == ServiceKind.DynamoDB);
            lab.Select(db); yield return null; Assert.IsTrue(lab.ContextRingVisible);
            Assert.IsFalse(ring.GetComponentsInChildren<LabTarget>().Any(t => t.isActiveAndEnabled && t.Label.text == "CÓDIGO"), "Code is Lambda-only");
            Assert.IsTrue(lab.BeginGrab(db)); yield return null; Assert.IsFalse(lab.ContextRingVisible, "Holding an object hides the ring"); lab.EndGrab(db);
        }
        [UnityTest]
        public IEnumerator BuildInspectConnectDeployAndTestFromWorldControls()
        {
            Assert.IsTrue(lab.SessionReady); Assert.AreEqual(3, lab.Graph.nodes.Count); Assert.IsEmpty(lab.Graph.Validate());
            yield return null; Capture("01-lab-overview");
            Click("CloudWatch"); Assert.AreEqual(3, lab.Graph.nodes.Count); lab.PreviewPlacement(new Vector3(0, 1.4f, 1.8f)); Click("Confirmar ubicación"); Assert.AreEqual(4, lab.Graph.nodes.Count);
            var added = lab.Graph.nodes.Last(); var fn = lab.Graph.nodes.First(n => n.kind == ServiceKind.Lambda);
            Click("Conectar nodos"); lab.Select(lab.Views[fn.id]); lab.Select(lab.Views[added.id]);
            Assert.AreEqual(3, lab.Graph.links.Count); Assert.IsEmpty(lab.Graph.Validate());
            lab.CancelInteraction(); lab.Select(lab.Views[fn.id]); Click("256 MB"); Assert.AreEqual(0, fn.setting); Click("Aplicar cambios"); Assert.AreEqual(1, fn.setting);
            var node = lab.Views[added.id]; Assert.IsTrue(lab.BeginGrab(node)); Assert.IsFalse(lab.BeginGrab(node));
            node.transform.position = new Vector3(1.4f, 1.09f, 2.0f); lab.EndGrab(node); Assert.AreEqual(node.transform.position, added.position);
            Click("Guardar"); Click("Relaciones"); Click("Eliminar recurso"); Assert.AreEqual(3, lab.Graph.nodes.Count);
            Click("Deshacer"); Assert.AreEqual(4, lab.Graph.nodes.Count);
            Click("Cargar"); Assert.AreEqual(4, lab.Graph.nodes.Count);
            Click("Desplegar demo"); yield return null; Capture("02-deployment-review");
            Click("Confirmar simulación"); Assert.IsTrue(lab.Busy);
            int count = lab.Graph.nodes.Count; lab.AddResource(ServiceKind.S3); Assert.AreEqual(count, lab.Graph.nodes.Count);
            yield return WaitIdle(); Assert.IsTrue(lab.Deployed); Assert.IsTrue(lab.Graph.nodes.All(n => n.state == ResourceState.Ready));
            Click("Enviar evento"); yield return WaitIdle(); Assert.AreEqual(1, lab.EventCount);
            Capture("03-deployed-lab");
        }
        [UnityTest]
        public IEnumerator HolographicControlsPreviewSnapAndComfortStayConsistent()
        {
            lab.Rig.enabled = false; // Drive the interaction without the physical mouse overwriting the preview.
            foreach (var panel in Object.FindObjectsByType<HoloPanelGraphic>())
                Assert.IsNotNull(panel.GetComponent<CanvasRenderer>(), "Panel must have a renderer: " + panel.name);
            foreach (var label in Object.FindObjectsByType<TMPro.TMP_Text>())
            {
                Assert.IsNotNull(label.font, "Missing font: " + label.text);
                label.ForceMeshUpdate();
                Assert.IsFalse(label.isTextTruncated, "Clipped label: " + label.text);
            }
            Click("CloudWatch"); lab.PreviewPlacement(new Vector3(0, 1.4f, 1.8f)); Click("Confirmar ubicación");
            var monitor = lab.Views[lab.Graph.nodes.Last().id];
            var fn = lab.Views[lab.Graph.nodes.First(n => n.kind == ServiceKind.Lambda).id];
            Click("Conectar nodos"); lab.Select(fn);
            Assert.AreEqual(1, fn.ConnectionHint); Assert.AreEqual(2, monitor.ConnectionHint);
            int edges = lab.Graph.links.Count;
            lab.PreviewConnection(monitor, monitor.transform.position);
            Assert.IsTrue(lab.HasConnectionPreview); Assert.AreEqual(edges, lab.Graph.links.Count);
            yield return null; Capture("04-connection-preview");
            lab.Rig.SendMessage("OnApplicationFocus", false);
            Assert.IsFalse(lab.HasConnectionPreview, "Focus loss clears the pointer preview");
            lab.Rig.SendMessage("OnApplicationFocus", true);
            lab.PreviewConnection(monitor, monitor.transform.position);
            Assert.IsTrue(lab.HasConnectionPreview);
            lab.Rig.SendMessage("OnApplicationPause", true);
            Assert.IsFalse(lab.HasConnectionPreview, "Headset pause clears the pointer preview");
            lab.Rig.SendMessage("OnApplicationPause", false);
            lab.PreviewConnection(monitor, monitor.transform.position);
            lab.CancelInteraction(); Assert.IsFalse(lab.HasConnectionPreview);
            Assert.IsTrue(lab.Views.Values.All(v => v.ConnectionHint == 0));
            Click("Ajustes"); Click("Espacio"); Click("Ajuste:"); Assert.IsTrue(lab.GridSnap);
            Assert.IsTrue(lab.BeginGrab(monitor)); monitor.transform.localPosition = new Vector3(.34f, 1.36f, 2.24f); lab.EndGrab(monitor);
            Assert.Less(Vector3.Distance(new Vector3(.3f, 1.4f, 2.2f), monitor.Model.position), .001f);
            Click("Animación:"); Assert.IsTrue(lab.Feedback.ReducedMotion);
            float clock = LabFeedback.Clock; yield return new WaitForSecondsRealtime(.12f); Assert.AreEqual(clock, LabFeedback.Clock);
            Click("Animación:"); yield return null; Assert.Greater(LabFeedback.Clock, clock);
            Click("Sonido:"); Assert.IsTrue(lab.Feedback.Muted); Click("Sonido:"); Assert.IsFalse(lab.Feedback.Muted);
            lab.ToggleConnect(); lab.Select(fn); lab.PreviewConnection(monitor, monitor.transform.position);
            lab.SetGraph(Architecture.Preset(1)); Assert.IsFalse(lab.HasConnectionPreview); Assert.IsFalse(lab.ConnectingMode);
        }
        [UnityTest]
        public IEnumerator MenusMoveIndependentlyPersistAndSurviveInspectorRefresh()
        {
            lab.Rig.enabled = false;
            Click("Ajustes"); var menus = lab.GetComponentsInChildren<LabMenu>(); Assert.AreEqual(6, menus.Length);
            var inspector = menus.Single(m => m.name == "03 · Inspector");
            var initial = inspector.transform.position; var initialRotation = inspector.transform.rotation;
            var graph = JsonUtility.ToJson(lab.Graph);
            var first = new object(); var second = new object();
            var ray = new Ray(Vector3.zero, Vector3.forward);
            Assert.IsTrue(inspector.TryGrab(first, ray, 2, Quaternion.identity));
            Assert.IsFalse(inspector.TryGrab(second, ray, 2, Quaternion.identity), "Two hands cannot own one menu");
            inspector.Move(second, new Ray(Vector3.right, Vector3.forward), Quaternion.identity, 1);
            Assert.AreEqual(initial, inspector.transform.position);
            inspector.Move(first, new Ray(Vector3.right, Vector3.forward), Quaternion.Euler(0, 30, 0), .5f);
            Assert.Greater(Vector3.Distance(initial, inspector.transform.position), .5f);
            Assert.Greater(Quaternion.Angle(initialRotation, inspector.transform.rotation), 29);
            inspector.Release(second); Assert.IsTrue(inspector.Grabbed);
            inspector.Release(first); Assert.IsFalse(inspector.Grabbed);
            var moved = inspector.transform.position; var rotation = inspector.transform.rotation; var handle = inspector.Handle;
            lab.Select(lab.Views.Values.First()); yield return null;
            Assert.AreEqual(moved, inspector.transform.position); Assert.AreEqual(handle, inspector.Handle);
            Assert.IsTrue(handle.gameObject.activeInHierarchy, "Inspector refresh must retain the grab handle");
            Assert.AreEqual(graph, JsonUtility.ToJson(lab.Graph), "Menu movement must not change the architecture");
            yield return SceneManager.LoadSceneAsync("AWSArchitectLab"); yield return null;
            lab = Object.FindAnyObjectByType<ArchitectureLab>(); yield return WaitIdle(); lab.Rig.enabled = false;
            inspector = lab.GetComponentsInChildren<LabMenu>().Single(m => m.name == "03 · Inspector");
            Assert.Less(Vector3.Distance(moved, inspector.transform.position), .001f);
            Assert.Less(Quaternion.Angle(rotation, inspector.transform.rotation), .01f);
            Assert.IsTrue(inspector.TryGrab(first, ray, 2, Quaternion.identity));
            lab.Rig.ResetMenus();
            Assert.IsFalse(inspector.Grabbed); Assert.Less(Vector3.Distance(initial, inspector.transform.position), .001f);
            Assert.IsFalse(PlayerPrefs.HasKey(LabMenu.PreferencePrefix + inspector.name));
            foreach (var menu in lab.GetComponentsInChildren<LabMenu>()) Assert.IsNotNull(menu.Handle);
            Capture("05-movable-menus");
        }
        [UnityTest]
        public IEnumerator EnvironmentSettingsHideOnlyTheRoomAndRestoreIt()
        {
            var graph = JsonUtility.ToJson(lab.Graph);
            Click("Ajustes"); Click("Espacio"); Click("Fondo: oculto"); yield return null;
            Assert.AreEqual(LabBackground.Hidden, lab.Environment.Mode);
            Assert.IsFalse(lab.Environment.VirtualRoom.activeSelf);
            Assert.IsTrue(lab.Views.Values.All(v => v.gameObject.activeInHierarchy));
            Assert.IsTrue(lab.GetComponentsInChildren<LabMenu>().All(m => m.Handle.gameObject.activeInHierarchy));
            Assert.AreEqual(graph, JsonUtility.ToJson(lab.Graph));
            Assert.AreEqual((int)LabBackground.Hidden, PlayerPrefs.GetInt(LabEnvironmentSettings.PreferenceKey));
            Assert.IsFalse(lab.Environment.SetMode(LabBackground.Passthrough), "Desktop must not claim real passthrough support");
            Assert.AreEqual(LabBackground.Hidden, lab.Environment.Mode);
            Click("Fondo: laboratorio"); yield return null;
            Assert.IsTrue(lab.Environment.VirtualRoom.activeSelf); Assert.IsFalse(lab.Environment.PassthroughActive);
            Assert.AreEqual(1, lab.Rig.ViewCamera.backgroundColor.a);
        }
        [UnityTest]
        public IEnumerator FailureRetryCancelResetAndCorruptSaveAreRecoverable()
        {
            if (!lab.GetComponentsInChildren<LabMenu>().Any(m => m.name == "Settings console")) Click("Ajustes");
            Click("Simular fallo"); Click("Desplegar demo"); Click("Confirmar simulación"); yield return WaitIdle();
            Assert.IsFalse(lab.Deployed); Assert.IsTrue(lab.Graph.nodes.Any(n => n.state == ResourceState.Failed));
            if (!lab.GetComponentsInChildren<LabMenu>().Any(m => m.name == "Settings console")) Click("Ajustes");
            Click("Simular fallo"); Click("Desplegar demo"); Click("Confirmar simulación"); yield return WaitIdle(); Assert.IsTrue(lab.Deployed);
            Click("Desplegar demo"); Click("Confirmar simulación"); lab.CancelDeployment();
            Assert.IsFalse(lab.Busy); Assert.IsFalse(lab.Deployed); Assert.IsTrue(lab.Graph.nodes.All(n => n.state == ResourceState.Draft));
            yield return new WaitForSecondsRealtime(1.1f); Assert.IsTrue(lab.Graph.nodes.All(n => n.state == ResourceState.Draft));
            File.WriteAllText(SavePath, "{ invalid"); Click("Cargar"); Assert.AreEqual(3, lab.Graph.nodes.Count);
            Click("Limpiar"); Click("Sí, limpiar"); Assert.AreEqual(0, lab.Graph.nodes.Count);
            Click("Deshacer"); Assert.AreEqual(3, lab.Graph.nodes.Count);
        }
    
        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        [UnityTest]
        public IEnumerator SharedRoomCompactsAndTurnsThePersonalConsoleToEachStation()
        {
            lab.Rig.enabled = false;
            var space = lab.Space; Assert.IsFalse(space.SharedRoom, "Solo by default");
            Assert.AreEqual(Vector3.zero, space.Console.localPosition); Assert.AreEqual(Quaternion.identity, space.Console.localRotation);
            var dock = GameObject.Find("02 · Architecture controls").transform; var soloDock = dock.position;
            Assert.IsFalse(GameObject.Find("Shared room stations"), "Floor stations only appear in a shared room");
            Click("Ajustes"); Click("Sala compartida"); Click("Distribución para 4:");
            Assert.IsTrue(space.SharedRoom); yield return null; yield return null;
            Assert.AreEqual(4, space.MarkerCount); Assert.IsTrue(GameObject.Find("Shared room stations"));
            var personal = new[] { "02 · Architecture controls", "01 · Service catalog", "03 · Inspector", "Mission status", "Settings console" };
            void AssertInSector(int s)
            {
                var stand = SharedSpace.StationPosition(s); var forward = Quaternion.Euler(0, SharedSpace.StationYaw(s), 0) * Vector3.forward;
                foreach (var name in personal)
                {
                    var flat = Flat(GameObject.Find(name).transform.position - stand);
                    Assert.LessOrEqual(Vector3.Angle(forward, flat), 55, name + " stays inside the station's sector");
                    Assert.LessOrEqual(flat.magnitude, 1.4f, name + " stays within reach");
                    // Never closer to a neighbour's station than to your own.
                    for (int other = 0; other < SharedSpace.MaxStations; other++) if (other != s)
                        Assert.Greater(Flat(GameObject.Find(name).transform.position - SharedSpace.StationPosition(other)).magnitude, flat.magnitude + .6f, name + " intrudes on station " + (other + 1));
                }
            }
            AssertInSector(0);
            foreach (var label in lab.GetComponentsInChildren<TMPro.TMP_Text>()) { label.ForceMeshUpdate(); Assert.IsFalse(label.isTextTruncated, "Clipped label: " + label.text); }
            Capture("44-shared-room-settings");
            Click("Cerrar ajustes"); yield return null; Capture("45-shared-room-console");
            Click("Ajustes"); Click("Sala compartida"); Click("Estación 3"); yield return null; yield return null;
            Assert.AreEqual(2, space.Station); AssertInSector(2);
            var camera = lab.Rig.ViewCamera.transform;
            Assert.Greater(Vector3.Dot(Flat(camera.forward).normalized, Flat(SharedSpace.Center - camera.position).normalized), .95f, "Station view faces the table");
            Click("Cerrar ajustes");
            // Shared identity turns to each viewer.
            var identity = GameObject.Find("Lab identity").transform;
            Assert.Greater(Vector3.Dot(identity.forward, Flat(identity.position - camera.position).normalized), .95f);
            // Zone guard: the desktop eye sits a metre behind the station, i.e. outside its circle.
            space.EvaluateZoneWithoutXR = true; yield return null;
            Assert.IsTrue(space.OutsideZone); Assert.IsTrue(GameObject.Find("Station guard"));
            camera.position = SharedSpace.StationPosition(2) + Vector3.up * 1.6f; yield return null;
            Assert.IsFalse(space.OutsideZone); space.EvaluateZoneWithoutXR = false;
            lab.SetSharedRoom(false); yield return null;
            Assert.AreEqual(Quaternion.identity, space.Console.localRotation);
            Assert.Less(Vector3.Distance(soloDock, dock.position), .001f, "Solo layout is restored exactly");
        }
        [UnityTest]
        public IEnumerator SimulatedTeammatesShowPresenceAndTheirLocksBlockEditing()
        {
            lab.Rig.enabled = false; lab.SetStation(1); lab.SetSimulatedPeers(true);
            Assert.IsTrue(lab.Space.SharedRoom, "Simulating a team opens the shared room"); Assert.IsTrue(lab.SimulatingPeers);
            for (int i = 0; i < 4; i++) yield return null;
            Assert.AreEqual(3, lab.Peers.Count);
            CollectionAssert.AreEquivalent(new[] { 0, 2, 3 }, lab.Collab.Peers.Select(p => p.Station).ToArray(), "Teammates take the other stations");
            var team = GameObject.Find("02 · Architecture controls").GetComponentsInChildren<TMPro.TMP_Text>().First(t => t.text.StartsWith("EQUIPO"));
            StringAssert.Contains("TÚ", team.text); StringAssert.Contains("ANA", team.text);
            var sim = (SimulatedCollabSession)lab.Collab;
            var views = lab.Views.Values.ToArray(); var mine = views[0]; var theirs = views[1];
            lab.Select(mine); Assert.IsNull(lab.Collab.LocalHold, "Inspection never claims an editing lease");
            Assert.IsTrue(lab.BeginGrab(mine));
            Assert.IsFalse(sim.ForceLock(0, mine.Model.id), "Nobody can take what you hold");
            lab.EndGrab(mine); Assert.IsNull(lab.Collab.LocalHold);
            Assert.IsTrue(sim.ForceLock(0, theirs.Model.id)); yield return null;
            Assert.AreSame(sim.Peers[0], theirs.LockedBy);
            lab.Select(theirs); Assert.AreSame(theirs, lab.SelectedView, "A teammate's object may be inspected privately");
            Assert.IsFalse(lab.BeginGrab(theirs), "A teammate's object cannot be moved");
            lab.ToggleConnect(); lab.Select(mine); lab.Select(theirs);
            StringAssert.DoesNotContain("está editando", lab.StatusMessage, "Connecting may still reference a held object"); lab.CancelInteraction();
            yield return new WaitForSecondsRealtime(1.5f);
            Capture("46-simulated-teammates");
            var view = lab.Rig.ViewCamera.transform; var eye = SharedSpace.Center + new Vector3(-3.2f, 4.1f, -3.2f);
            view.SetPositionAndRotation(eye, Quaternion.LookRotation(SharedSpace.Center + Vector3.up * .9f - eye)); yield return null;
            Capture("47-shared-room-overview");
            lab.SetSimulatedPeers(false); yield return null;
            Assert.AreEqual(0, lab.Peers.Count); Assert.IsNull(theirs.LockedBy);
            lab.SetSharedRoom(false);
        }
        [UnityTest]
        public IEnumerator AimAssistForgivesNearMissesAndBeamsDoNotAllocate()
        {
            lab.Rig.enabled = false; yield return null; Physics.SyncTransforms();
            // The deploy button ends the primary row: nothing else sits just to its right.
            var button = Object.FindObjectsByType<LabTarget>().First(t => t.isActiveAndEnabled && t.Label && t.Label.text.StartsWith("Desplegar demo"));
            var box = button.GetComponent<BoxCollider>();
            var edge = button.transform.TransformPoint(new Vector3(box.size.x / 2, 0, 0));
            var origin = lab.Rig.ViewCamera.transform.position;
            var near = edge + button.transform.right * .015f; // 1.5 cm past the edge: a miss for a thin ray
            var ray = new Ray(origin, (near - origin).normalized);
            Assert.IsFalse(Physics.Raycast(ray, out var direct, 12) && direct.collider == box, "The thin ray misses");
            var pick = typeof(LabRig).GetMethod("Pick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var args = new object[] { ray, 0f };
            Assert.AreSame(button, pick.Invoke(lab.Rig, args), "Aim assist lands on the nearby button");
            var far = edge + button.transform.right * .12f;
            args = new object[] { new Ray(origin, (far - origin).normalized), 0f };
            Assert.AreNotSame(button, pick.Invoke(lab.Rig, args), "A clear miss stays a miss");
            var color = new Color(.2f, .4f, .6f, 1); LabVisuals.Beam(color); LabVisuals.Beam(color, false); LabVisuals.SpecialMaterial("LabGlow", color); // warm the caches
            Assert.AreSame(LabVisuals.Beam(color), LabVisuals.Beam(color));
            TestDelegate lookups = () => { LabVisuals.Beam(color); LabVisuals.Beam(color, false); LabVisuals.SpecialMaterial("LabGlow", color); };
            for (int i = 0; i < 3; i++) lookups(); // JIT and comparer creation happen on the first calls
            Assert.That(lookups, UnityEngine.TestTools.Constraints.ConstraintExtensions.AllocatingGCMemory(Is.Not));
        }
        NetworkCollabSession AttachTestRoom()
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var room = new NetworkCollabSession(new RoomGrant { userId="me", roomId="ABCDEF12", station=0, websocketUrl="wss://test.example/rooms" });
            typeof(NetworkCollabSession).GetField("connected", flags).SetValue(room, true);
            typeof(ArchitectureLab).GetProperty("Collab").SetValue(lab, room);
            var receive = typeof(ArchitectureLab).GetMethod("ReceiveRoomSnapshot", flags);
            room.Snapshot += (System.Action<RoomMessage>)receive.CreateDelegate(typeof(System.Action<RoomMessage>), lab);
            DeliverRoom(room, lab.Graph.Copy(), 0, "");
            return room;
        }
        void DeliverRoom(NetworkCollabSession room, Architecture graph, int revision, string receipt)
        {
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var queue=(System.Collections.Concurrent.ConcurrentQueue<string>)typeof(NetworkCollabSession).GetField("received", flags).GetValue(room);
            queue.Enqueue(JsonUtility.ToJson(new RoomMessage { type="snapshot", roomId=room.Grant.roomId, revision=revision, roomVersion=revision+1,
                hostId="me", graph=graph, requestId=receipt, accepted=true,
                members=new[]{new RoomMember{userId="me",name="ME",role="facilitator",station=0}}, locks=new RoomLease[0] }));
            room.Pump();
        }
        [UnityTest]
        public IEnumerator SharedEditWaitsForAuthoritativeReceiptAndDisconnectBlocksWrites()
        {
            lab.Rig.enabled=false;
            var room=AttachTestRoom();
            var previous=JsonUtility.ToJson(lab.Graph);
            lab.AddResource(ServiceKind.CloudWatch);
            var candidate=lab.Graph.Copy(); Assert.AreNotEqual(previous,JsonUtility.ToJson(candidate));
            yield return null; yield return null;
            Assert.IsTrue(lab.RoomEditPending);
            Assert.AreEqual(previous,JsonUtility.ToJson(lab.Graph),"Unconfirmed candidates must not become shared state");
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var queue=(System.Collections.Concurrent.ConcurrentQueue<string>)typeof(NetworkCollabSession).GetField("outgoing",flags).GetValue(room);
            RoomCommand op=null;
            while(queue.TryDequeue(out var json)) {var command=JsonUtility.FromJson<RoomCommand>(json);if(command.action=="op")op=command;}
            Assert.IsNotNull(op); Assert.AreEqual(0,op.baseRevision);
            DeliverRoom(room,candidate,1,op.requestId);
            Assert.IsFalse(lab.RoomEditPending); Assert.AreEqual(candidate.nodes.Count,lab.Graph.nodes.Count);
            room.Dispose(); Assert.IsTrue(lab.RoomReadOnly);
            int count=lab.Graph.nodes.Count; lab.AddResource(ServiceKind.S3);
            Assert.AreEqual(count,lab.Graph.nodes.Count,"Disconnected rooms cannot silently edit a local fork");
        }
    }
}

