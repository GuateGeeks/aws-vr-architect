using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Unity.WebRTC;

namespace GuateGeeks.AwsVr
{
    // One short-lived session. The app executes a bounded allowlist of tools; no permanent key exists here.
    public sealed class RealtimeVoice : MonoBehaviour
    {
        public Action<string> Status, UserTranscript, AssistantTranscript;
        public Action<string,string,string> ToolCall;
        public Action SpeechStarted,InputCommitted,Interrupted,CaptureStopped;
        public bool UserSpeaking {get;private set;}
        public bool Speaking {get;private set;}
        public bool NaturalTurns {get;private set;}=true;
        public int SessionUpdates {get;private set;}
        public int AudioTurns {get;private set;}
        public float LastFirstAudioMs {get;private set;}=-1;
        public float LastDetectionMs {get;private set;}=-1;
        float speechStoppedAt=-1,committedAt;
        bool waitingFirstAudio;
        public bool Connected {get;private set;}
        public bool Connecting {get;private set;}
        public bool Listening {get;private set;}
        public bool Responding {get;private set;}
        public int TotalTokens {get;private set;}
        public readonly RealtimeCost UsageCost=new RealtimeCost();
        public string Model {get;private set;}
        public string Caption {get;private set;}="";
        public float MicrophoneLevel=>audioInput?audioInput.Level:0;
        public float RecordedSeconds=>audioInput?audioInput.CapturedSamples/(float)RealtimeAudioInput.SampleRate:0;
        public bool PreparingMicrophone=>microphoneStarting;
        RTCPeerConnection peer;
        RTCDataChannel channel;
        AudioStreamTrack microphoneTrack,receivedTrack;
        AudioSource speaker;
        RealtimeAudioInput audioInput;
        AudioClip microphoneClip;
        UnityWebRequest handshake;
        readonly Queue<RealtimeProtocol.Event> events=new Queue<RealtimeProtocol.Event>();
        readonly HashSet<string> calls=new HashSet<string>();
        readonly List<RealtimeProtocol.Event> deferredCalls=new List<RealtimeProtocol.Event>();
        readonly HashSet<string> pendingTools=new HashSet<string>();
        readonly Queue<int> requestedTurns=new Queue<int>();
        readonly Dictionary<string,int> responseTurns=new Dictionary<string,int>();
        int turn;
        int toolRounds;
        bool toolContinuation,responseHadSpeech,answerRecovery;
        int generation,audioGeneration;
        float opened;
        int maximumSeconds;
        bool closing,microphoneStarting,permissionPending,focused=true,paused;
        bool automaticCapture,captureSuspended,microphoneBlocked;
        float lastCapturePoll;
        bool pushToTalk, talkHeld;
        public void SetPushToTalk(bool required, bool held) { pushToTalk=required;talkHeld=held;audioInput?.SetTransmissionEnabled(!required || held); }
        public bool CanResume=>focused && !paused;
        public void Begin(AwsCloudApi.AssistantSession ticket, bool captureMicrophone=true, bool naturalTurns=true)
        {
            Disconnect();closing=false;Connecting=true;generation++;TotalTokens=0;calls.Clear();
            Model=ticket.model;
            automaticCapture=captureMicrophone;captureSuspended=false;microphoneBlocked=false;
            NaturalTurns=naturalTurns;SessionUpdates=AudioTurns=0;LastFirstAudioMs=LastDetectionMs=-1;
            maximumSeconds=Mathf.Clamp(ticket.maxSessionSeconds,30,3300);opened=Time.unscaledTime;
            StartCoroutine(Connect(ticket,generation));
        }
        IEnumerator Connect(AwsCloudApi.AssistantSession ticket,int epoch)
        {
            Status?.Invoke("CONECTANDO · audio OpenAI");
            try {
                var audioObject=new GameObject("ATLAS voice");audioObject.transform.SetParent(transform,false);speaker=audioObject.AddComponent<AudioSource>();speaker.playOnAwake=false;speaker.loop=true;speaker.spatialBlend=0;
                peer=new RTCPeerConnection();
                microphoneTrack=new AudioStreamTrack(){Enabled=false};peer.AddTrack(microphoneTrack);
                var listener=GetComponentInChildren<AudioListener>()??FindAnyObjectByType<AudioListener>();
                if(!listener)throw new InvalidOperationException("No active audio listener.");
                audioInput=listener.gameObject.AddComponent<RealtimeAudioInput>();audioInput.Attach(microphoneTrack);
                peer.OnTrack=e=>{if(epoch!=generation || closing || !(e.Track is AudioStreamTrack audio))return;receivedTrack=audio;speaker.SetTrack(audio);speaker.Play();};
                peer.OnConnectionStateChange=state=> {if(epoch==generation && (state==RTCPeerConnectionState.Failed || state==RTCPeerConnectionState.Disconnected)) Fail("Se perdió la conexión de voz. Reconecta ATLAS.");};
                channel=peer.CreateDataChannel("oai-events");
                channel.OnOpen=()=>{if(epoch==generation){Connected=true;Connecting=false;ConfigureTurnDetection(NaturalTurns);Status?.Invoke("ATLAS ACTIVO · conversación continua");}};
                channel.OnMessage=bytes=>{
                    if(epoch!=generation || closing)return;
                    if(bytes.Length>131072 || events.Count>100){Fail("Sesión cerrada por exceso de eventos.");return;}
                    try{events.Enqueue(RealtimeProtocol.Parse(Encoding.UTF8.GetString(bytes)));}catch(ArgumentException){Fail("Evento de voz inválido.");}
                };
                StartCoroutine(WebRTC.Update());
            } catch(Exception) {Fail("No se pudo iniciar WebRTC en este dispositivo.");yield break;}
            var offer=peer.CreateOffer();yield return offer;
            if(epoch!=generation)yield break;
            if(offer.IsError){Fail("No se pudo preparar el audio.");yield break;}
            var local=offer.Desc;var setting=peer.SetLocalDescription(ref local);yield return setting;
            if(epoch!=generation)yield break;
            if(setting.IsError){Fail("No se pudo configurar el audio local.");yield break;}
            float deadline=Time.unscaledTime+5;
            while(peer.GatheringState!=RTCIceGatheringState.Complete && Time.unscaledTime<deadline)yield return null;
            if(epoch!=generation)yield break;
            handshake=new UnityWebRequest("https://api.openai.com/v1/realtime/calls","POST"){
                uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(peer.LocalDescription.sdp)),downloadHandler=new DownloadHandlerBuffer(),timeout=20,redirectLimit=0};
            handshake.SetRequestHeader("Content-Type","application/sdp");handshake.SetRequestHeader("Authorization","Bearer "+ticket.clientSecret);
            ticket.clientSecret=null;
            yield return handshake.SendWebRequest();
            if(epoch!=generation)yield break;
            bool ok=handshake.result==UnityWebRequest.Result.Success;string answer=ok?handshake.downloadHandler.text:null;
            long httpStatus=handshake.responseCode;string providerCode="unknown";
            if(!ok)try{providerCode=SafeErrorCode(JsonUtility.FromJson<RealtimeProtocol.Event>(handshake.downloadHandler.text)?.error?.code);}catch(ArgumentException){}
            handshake.Dispose();handshake=null;
            if(!ok){Fail("OpenAI no aceptó la conexión (HTTP "+httpStatus+", "+providerCode+"). Reconecta ATLAS.");yield break;}
            var remote=new RTCSessionDescription{type=RTCSdpType.Answer,sdp=answer};var configured=peer.SetRemoteDescription(ref remote);yield return configured;
            if(epoch!=generation)yield break;
            if(configured.IsError){Fail("No se pudo negociar la voz.");yield break;}
            deadline=Time.unscaledTime+15;
            while(!Connected && epoch==generation && Time.unscaledTime<deadline)yield return null;
            if(epoch==generation && !Connected)Fail("La conexión de voz agotó la espera.");
        }
        public void SetCaptureSuspended(bool value)
        {
            if(captureSuspended==value)return;
            captureSuspended=value;
            if(value){Interrupt();StopMicrophone();Send(RealtimeProtocol.CommandJson("input_audio_buffer.clear"));Status?.Invoke("AUDIO EN PAUSA · configuración privada");}
        }
        public void ConfigureTurnDetection(bool natural)
        {
            NaturalTurns=natural;
            if(Connected)Send(RealtimeProtocol.TurnDetectionJson(natural));
        }
        void ResumeMicrophone()
        {
            if(Connected && automaticCapture && !captureSuspended && !microphoneBlocked && CanResume && !Listening && !microphoneStarting)
                StartCoroutine(StartMicrophone(generation,audioGeneration));
        }
        IEnumerator StartMicrophone(int epoch,int audioEpoch)
        {
            microphoneStarting=true;
            Status?.Invoke("PREPARANDO MICRÓFONO · espera el indicador de señal");
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone)) {
                permissionPending=true;
                bool answered=false;var callbacks=new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted+=_=>answered=true;callbacks.PermissionDenied+=_=>answered=true;
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone,callbacks);
                float permissionDeadline=Time.unscaledTime+60;
                while(!answered && epoch==generation && audioEpoch==audioGeneration && Time.unscaledTime<permissionDeadline)yield return null;
                while(answered && (!focused || paused) && epoch==generation && audioEpoch==audioGeneration && Time.unscaledTime<permissionDeadline)yield return null;
                permissionPending=false;
            }
            bool allowed=UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
#else
            if(!Application.HasUserAuthorization(UserAuthorization.Microphone))yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
            bool allowed=Application.HasUserAuthorization(UserAuthorization.Microphone);
#endif
            if(epoch!=generation || audioEpoch!=audioGeneration)yield break;
            if(!allowed || !focused || paused){microphoneStarting=false;microphoneBlocked=!allowed;Status?.Invoke("Permite el micrófono y vuelve a activar ATLAS.");yield break;}
            Send(RealtimeProtocol.CommandJson("input_audio_buffer.clear"));
            try{microphoneClip=Microphone.Start(null,true,2,48000);if(microphoneClip)audioInput.Begin(microphoneClip);}
            catch(Exception){StopMicrophone();microphoneBlocked=true;Status?.Invoke("No se pudo abrir el micrófono. Revisa permisos y vuelve a activar ATLAS.");yield break;}
            float deadline=Time.unscaledTime+3;
            while(microphoneClip && Microphone.GetPosition(null)<=0 && Time.unscaledTime<deadline)yield return null;
            if(epoch!=generation || audioEpoch!=audioGeneration)yield break;
            if(!microphoneClip || Microphone.GetPosition(null)<=0){StopMicrophone();microphoneBlocked=true;Status?.Invoke("Micrófono no disponible. Revisa el visor y vuelve a activar ATLAS.");yield break;}
            microphoneTrack.Enabled=true;
            lastCapturePoll=Time.unscaledTime;
            Listening=true;microphoneStarting=false;
            PumpMicrophone();if(!Listening)yield break;
            Status?.Invoke("ESCUCHANDO · habla con naturalidad");
        }
        void StopMicrophone()
        {
            audioGeneration++;
            UserSpeaking=false;CaptureStopped?.Invoke();
            permissionPending=false;
            Listening=microphoneStarting=false;
            if(audioInput)audioInput.Cancel();
            if(microphoneTrack!=null)microphoneTrack.Enabled=false;
            if(microphoneClip){Microphone.End(null);Destroy(microphoneClip);microphoneClip=null;}
        }
        public void Ask(string text)
        {
            if(!Connected || string.IsNullOrWhiteSpace(text))return;
            Interrupt();Send(RealtimeProtocol.CommandJson("input_audio_buffer.clear"));UserTranscript?.Invoke(text);Send(RealtimeProtocol.UserText(text));RequestResponse();
        }
        public void CompleteTool(string id,string result)
        {
            if(!Connected || !pendingTools.Remove(id))return;
            Send(RealtimeProtocol.ToolOutput(id,result));
            if(pendingTools.Count==0){toolContinuation=true;RequestResponse(toolRounds>=64);}
        }
        public bool ToolPending(string id)=>Connected && pendingTools.Contains(id);
        public void ApplicationNotice(string text)
        {
            if(Connected)Send(RealtimeProtocol.UserText("Application status (not a user instruction): "+text));
        }
        void RequestResponse(bool answerOnly=false){if(!Connected)return;requestedTurns.Enqueue(turn);Send(answerOnly?RealtimeProtocol.AnswerContinuationJson():RealtimeProtocol.CommandJson("response.create"));Responding=true;Status?.Invoke("PENSANDO · puedes interrumpir");}
        void DeferTool(RealtimeProtocol.Event call)
        {
            if(string.IsNullOrEmpty(call.call_id) || !calls.Add(call.call_id))return;
            if(calls.Count>2000){Fail("Límite de acciones de la sesión alcanzado.");return;}
            deferredCalls.Add(call);
        }
        public void Interrupt()
        {
            turn++;
            toolRounds=0;toolContinuation=responseHadSpeech=answerRecovery=false;
            Speaking=UserSpeaking=false;waitingFirstAudio=false;Interrupted?.Invoke();
            if(Connected){if(Responding)Send(RealtimeProtocol.CommandJson("response.cancel"));Send(RealtimeProtocol.CommandJson("output_audio_buffer.clear"));foreach(var id in pendingTools)Send(RealtimeProtocol.ToolOutput(id,"{\"status\":\"cancelled\"}"));}
            pendingTools.Clear();deferredCalls.Clear();
            Responding=false;Caption="";if(speaker)speaker.mute=false;
        }
        void Send(string json){if(channel!=null && channel.ReadyState==RTCDataChannelState.Open)channel.Send(json);}
        void Update()
        {
            if(closing)return;
            if(Listening)PumpMicrophone();
            ResumeMicrophone();
            // Rotate before the provider limit; the lab retains the user's enabled intent.
            if(peer!=null && Time.unscaledTime-opened>maximumSeconds){Fail("Renovando sesión de voz…");return;}
            int count=0;
            while(events.Count>0 && count++<20) {
                var message=events.Dequeue();
                if(message.type=="response.created" && !string.IsNullOrEmpty(message.response?.id))
                    responseTurns[message.response.id]=requestedTurns.Count>0?requestedTurns.Dequeue():-1;
                // Cancelled / superseded responses still cost money. Account before turn filtering.
                if(message.type=="response.done")UsageCost.AddResponse(Model,message.response);
                if(message.type=="conversation.item.input_audio_transcription.completed")UsageCost.AddTranscription(message.item_id,message.usage);
                string responseId=message.response_id??message.response?.id;
                if(!string.IsNullOrEmpty(responseId) && responseTurns.TryGetValue(responseId,out int responseTurn) && responseTurn!=turn)continue;
                switch(message.type) {
                    case "session.updated":SessionUpdates++;break;
                    case "input_audio_buffer.speech_started":
                        if(!captureSuspended && CanResume){Interrupt();UserSpeaking=true;speechStoppedAt=-1;SpeechStarted?.Invoke();Status?.Invoke("ESCUCHANDO · habla con naturalidad");}break;
                    case "input_audio_buffer.speech_stopped":speechStoppedAt=Time.unscaledTime;break;
                    case "input_audio_buffer.committed":
                        if(!captureSuspended && CanResume){UserSpeaking=false;AudioTurns++;committedAt=Time.unscaledTime;waitingFirstAudio=true;LastFirstAudioMs=-1;LastDetectionMs=speechStoppedAt<0?-1:(committedAt-speechStoppedAt)*1000;InputCommitted?.Invoke();Debug.Log("ATLAS audio: automatic speech turn accepted");RequestResponse();}break;
                    case "output_audio_buffer.started":
                        Speaking=true;
                        if(waitingFirstAudio){waitingFirstAudio=false;LastFirstAudioMs=(Time.unscaledTime-committedAt)*1000;Debug.Log("ATLAS latency: mode="+(NaturalTurns?"semantic":"server")+" commit_to_audio_ms="+Mathf.RoundToInt(LastFirstAudioMs));}break;
                    case "output_audio_buffer.stopped":
                    case "output_audio_buffer.cleared":Speaking=false;break;
                    case "conversation.item.input_audio_transcription.failed":Status?.Invoke("No se pudo transcribir la voz. Habla de nuevo o usa Escribir.");break;
                    case "conversation.item.input_audio_transcription.completed":UserTranscript?.Invoke(message.transcript);break;
                    case "response.output_audio_transcript.delta":
                    case "response.output_text.delta":
                        responseHadSpeech|=!string.IsNullOrEmpty(message.delta);
                        Caption=(Caption+(message.delta??""));if(Caption.Length>2200)Caption=Caption.Substring(Caption.Length-2200);AssistantTranscript?.Invoke(Caption);break;
                    // A tool continuation belongs to the same user turn. Preserve its spoken
                    // caption when the continuation produces only a tool result or no new speech.
                    case "response.created":responseHadSpeech=false;Responding=true;Status?.Invoke("RESPONDIENDO · Interrumpir para detener");break;
                    case "response.done":
                        Responding=false;TotalTokens+=message.response?.usage?.total_tokens??0;
                        Status?.Invoke(message.response?.status=="failed"?"Respuesta no disponible. Revisa el modelo y cuota OpenAI.":"ESCUCHANDO · puedes seguir hablando");
                        // response.done is authoritative even if argument completion events were absent.
                        if(message.response?.status=="completed" && message.response.output!=null)
                            foreach(var item in message.response.output)
                                if(item.type=="function_call")DeferTool(new RealtimeProtocol.Event{call_id=item.call_id,name=item.name,arguments=item.arguments});
                        var ready=deferredCalls.ToArray();deferredCalls.Clear();
                        if(message.response?.status=="completed"){
                            if(ready.Length>0)toolRounds++;
                            foreach(var call in ready)pendingTools.Add(call.call_id);
                            foreach(var call in ready)ToolCall?.Invoke(call.call_id,call.name,call.arguments);
                            // A silent post-tool completion must not leave the user waiting for another command.
                            if(ready.Length==0 && pendingTools.Count==0 && toolContinuation && !responseHadSpeech && !answerRecovery){answerRecovery=true;RequestResponse(true);}
                        }break;
                    case "response.function_call_arguments.done":
                        DeferTool(message);break;
                    case "error":
                        if(message.error?.code=="response_cancel_not_active")break;
                        Responding=false;
                        Debug.LogWarning("ATLAS provider error code: "+SafeErrorCode(message.error?.code));
                        Status?.Invoke(message.error?.code=="input_audio_buffer_commit_empty"?"OpenAI recibió audio vacío. Comprueba el indicador de señal.":"OpenAI: no se completó la acción. Reintenta o reconecta.");break;
                }
            }
        }
        void Fail(string message){Disconnect();Status?.Invoke(message);}
        static string SafeErrorCode(string value)=>System.Text.RegularExpressions.Regex.IsMatch(value??"",@"\A[a-z_]{1,80}\z")?value:"unknown";
        void PumpMicrophone()
        {
            if(!microphoneClip || !audioInput)return;
            audioInput.SetTransmissionEnabled(!pushToTalk || talkHeld);
            if(!Microphone.IsRecording(null)){StopMicrophone();Status?.Invoke("Recuperando micrófono del visor…");return;}
            if(Time.unscaledTime-lastCapturePoll>1){StopMicrophone();Status?.Invoke("Recuperando audio tras una interrupción…");return;}
            try{audioInput.ReadAvailable(Microphone.GetPosition(null));}
            catch(Exception){Fail("Reconectando micrófono…");return;}
            lastCapturePoll=Time.unscaledTime;
            if(audioInput.Overflow)Fail("Reconectando transmisión de audio…");
        }
        public void Disconnect()
        {
            closing=true;generation++;Connected=Connecting=Responding=Speaking=UserSpeaking=false;waitingFirstAudio=false;Interrupted?.Invoke();StopAllCoroutines();StopMicrophone();
            if(audioInput){audioInput.Detach();Destroy(audioInput);audioInput=null;}
            if(handshake!=null){handshake.Abort();handshake.Dispose();handshake=null;}
            if(channel!=null){channel.Close();channel.Dispose();channel=null;}
            if(peer!=null){peer.Close();peer.Dispose();peer=null;}
            microphoneTrack?.Dispose();microphoneTrack=null;receivedTrack?.Dispose();receivedTrack=null;
            if(speaker)Destroy(speaker.gameObject);
            events.Clear();calls.Clear();deferredCalls.Clear();pendingTools.Clear();requestedTurns.Clear();responseTurns.Clear();turn++;
        }
        void OnApplicationPause(bool value){paused=value;if(value && !permissionPending)Fail("ATLAS desconectado al suspender el visor.");}
        void OnApplicationFocus(bool value){focused=value;if(!value && !permissionPending){Interrupt();StopMicrophone();Send(RealtimeProtocol.CommandJson("input_audio_buffer.clear"));}}
        void OnDestroy(){Disconnect();}
    }
}
