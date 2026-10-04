using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    public sealed partial class ArchitectureLab
    {
        RectTransform codePanel;TMPro.TMP_Text codeTitle,codeBody,codeStatus;
        LabTarget codeConfirm;LambdaCodeDraft codeDraft;AwsCloudApi codeReader;
        string codeIdentity,codeNodeId,codeStack,codePreviousSource,codeOutput="",codeNotice="",codeReviewHash="",codeReviewRevision="",codeReviewOperation="";
        AwsCloudApi codeCloud;AwsCloudApi.CodeVersion[] codeVersions=Array.Empty<AwsCloudApi.CodeVersion>();
        int codeLine,codePage,codeTab,codeVersion,codeEpoch;bool codeBusy,keyboardCodeMode,codeCaps,codeToolActive;
        string codeReviewVersion="",codeUpdateStatus="";
        public bool CodeBusy=>codeBusy;
        public string LambdaDraftSource=>codeDraft?.source;
        string codeDirectory;
        string CodeDirectory=>codeDirectory??Path.Combine(Application.persistentDataPath,"lambda-drafts");
        void TickCodeEditor()
        {
            if(!codePanel)return;
            if(codeDraft!=null && !CodeTargetCurrent){if(codeBusy)AbortCodeOperation();ClearCodeReview();}
            codeConfirm.SetAvailable(CanConfirmCode);
            if(ConfiguringConnection || credentialBusy)codePanel.gameObject.SetActive(false);
        }
        [Serializable] sealed class CodeToolRequest {public int baseRevision=-1;public string nodeId,source,summary,baseSourceHash,eventJson,action;}
        [Serializable] sealed class CodeContext {public string nodeId,source,sourceHash,revisionId,eventJson;public bool validated,tested,publishRequiresClick=true;}
        public void OpenLambdaEditor(string nodeId)
        {
            var node=Graph.Find(nodeId);if(node==null || node.kind!=ServiceKind.Lambda || Busy || codeBusy || ConfiguringConnection || EditingText)return;
            if(!codePanel)BuildCodeEditor();
            string localIdentity=DesignSemantics.Definition(Graph)+"/"+nodeId;
            string identity=Deployed?(Cloud?.StackId??"simulation")+"/"+nodeId:localIdentity;
            if(codeIdentity!=identity || codeCloud!=Cloud) {
                codeIdentity=identity;codeNodeId=nodeId;codeStack=Cloud?.StackId??"";codeCloud=Cloud;
                codeDraft=LambdaCodeDraft.Load(CodeDirectory,identity,nodeId);
                if(IsCloud && Deployed && !File.Exists(Path.Combine(CodeDirectory,LambdaCodeDraft.Hash(identity)+".json"))) {
                    codeDraft=LambdaCodeDraft.Load(CodeDirectory,localIdentity,nodeId);codeDraft.baseSource=codeDraft.revisionId="";
                }
                codePreviousSource=codeDraft.source;codeLine=codePage=codeTab=codeVersion=0;
                codeVersions=Array.Empty<AwsCloudApi.CodeVersion>();codeOutput="";codeUpdateStatus="";codeNotice="Borrador local · Python 3.13 · index.py · máximo 8 KiB";ClearCodeReview();
            }
            codePanel.gameObject.SetActive(true);RefreshCodeEditor();
        }
        void BuildCodeEditor()
        {
            codePanel=Panel(world,"Lambda code studio",new Vector3(0,2.02f,1.05f),new Vector2(1220,1160));codePanel.localScale=Vector3.one*.00112f;
            codePanel.GetComponent<HoloPanelGraphic>().color=new Color(.02f,.045f,.07f,1);
            codeTitle=Text(codePanel,"LAMBDA / CÓDIGO",new Vector2(0,510),new Vector2(1150,60),30,Cyan);
            string[] tabs={"Código","Cambios","Prueba","Versiones"};for(int i=0;i<tabs.Length;i++){int tab=i;Button(codePanel,tabs[i],new Vector2(-420+i*280,445),new Vector2(260,48),()=>{codeTab=tab;codePage=0;RefreshCodeEditor();});}
            Block(codePanel,new Vector2(0,133),new Vector2(1160,550),new Color(.009f,.022f,.035f,1));
            codeBody=Text(codePanel,"",new Vector2(0,133),new Vector2(1140,530),25,White);codeBody.richText=false;codeBody.alignment=TMPro.TextAlignmentOptions.TopLeft;
            codeStatus=Text(codePanel,"",new Vector2(0,-194),new Vector2(1140,100),22,Cyan);codeStatus.richText=false;codeStatus.enableAutoSizing=true;codeStatus.fontSizeMin=17;codeStatus.fontSizeMax=22;
            Button(codePanel,"‹ Página",new Vector2(-460,-135),new Vector2(210,45),()=>{codePage=Math.Max(0,codePage-1);RefreshCodeEditor();});
            Button(codePanel,"Página ›",new Vector2(460,-135),new Vector2(210,45),()=>{codePage++;RefreshCodeEditor();});
            Button(codePanel,"Línea −",new Vector2(-470,-282),new Vector2(180,48),()=>{codeLine=Math.Max(0,codeLine-1);codeTab=0;codePage=codeLine/14;RefreshCodeEditor();});
            Button(codePanel,"Línea +",new Vector2(-275,-282),new Vector2(180,48),()=>{codeLine=Math.Min(codeDraft.source.Split('\n').Length-1,codeLine+1);codeTab=0;codePage=codeLine/14;RefreshCodeEditor();});
            Button(codePanel,"Editar línea",new Vector2(-60,-282),new Vector2(230,48),EditCodeLine);
            Button(codePanel,"Insertar línea",new Vector2(190,-282),new Vector2(230,48),()=>{if(codeBusy)return;var lines=codeDraft.source.Split('\n').ToList();lines.Insert(codeLine+1,"");ChangeCode(string.Join("\n",lines));codeLine++;RefreshCodeEditor();});
            Button(codePanel,"Borrar línea",new Vector2(440,-282),new Vector2(230,48),()=>{if(codeBusy)return;var lines=codeDraft.source.Split('\n').ToList();if(lines.Count>1){lines.RemoveAt(codeLine);ChangeCode(string.Join("\n",lines));codeLine=Math.Min(codeLine,lines.Count-1);RefreshCodeEditor();}});
            Button(codePanel,"Cargar AWS",new Vector2(-445,-347),new Vector2(270,50),()=>{if(!codeBusy)StartCoroutine(LoadCode(true));});
            Button(codePanel,"Evento JSON",new Vector2(-145,-347),new Vector2(270,50),()=>OpenCodeKeyboard("EVENTO JSON · prueba aislada",codeDraft.eventJson,v=>{codeDraft.eventJson=v;SaveCodeDraft();}));
            Button(codePanel,"Validar",new Vector2(155,-347),new Vector2(270,50),()=>{if(!codeBusy)StartCoroutine(RunCodeOperation("validate"));});
            Button(codePanel,"Probar borrador",new Vector2(450,-347),new Vector2(270,50),()=>{if(!codeBusy)StartCoroutine(RunCodeOperation("test"));});
            Button(codePanel,"Revisar publicación",new Vector2(-370,-411),new Vector2(360,50),()=>ReviewCode("publish"),Orange);
            Button(codePanel,"Elegir versión",new Vector2(0,-411),new Vector2(330,50),()=>{if(codeBusy)return;ClearCodeReview();if(codeVersions.Length>0)codeVersion=(codeVersion+1)%codeVersions.Length;codeTab=3;RefreshCodeEditor();});
            Button(codePanel,"Revisar restauración",new Vector2(375,-411),new Vector2(360,50),()=>ReviewCode("rollback"),Orange);
            codeConfirm=Button(codePanel,"Confirmar código en AWS",new Vector2(0,-474),new Vector2(660,50),ConfirmCodeReview,Orange);
            Button(codePanel,"Deshacer borrador",new Vector2(-370,-539),new Vector2(360,48),()=>{if(codeBusy)return;string previous=codePreviousSource;ChangeCode(previous);});
            Button(codePanel,"Guardar borrador",new Vector2(0,-539),new Vector2(330,48),SaveCodeDraft);
            Button(codePanel,"Cerrar código",new Vector2(375,-539),new Vector2(360,48),()=>codePanel.gameObject.SetActive(false));
        }
        void ChangeCode(string source)
        {
            if(codeBusy || codeDraft==null)return;
            try {string old=codeDraft.source;codeDraft.Edit(source);codePreviousSource=old;ClearCodeReview();SaveCodeDraft();}
            catch(ArgumentException error){codeNotice=error.Message;RefreshCodeEditor();}
        }
        void SaveCodeDraft()
        {
            if(codeDraft==null)return;
            try{codeDraft.Save(CodeDirectory,codeIdentity);codeNotice="Borrador guardado localmente. Aún no se publicó en AWS.";}catch(IOException){codeNotice="No se pudo guardar el borrador.";}
            RefreshCodeEditor();
        }
        void ClearCodeReview(){codeReviewHash=codeReviewRevision=codeReviewOperation=codeReviewVersion="";}
        void AbortCodeOperation()
        {
            codeEpoch++;codeReader?.Disconnect();codeReader=null;codeBusy=codeToolActive=false;ClearCodeReview();
            codeNotice="Consulta interrumpida. Carga AWS para verificar cualquier publicación pendiente.";RefreshCodeEditor();
        }
        bool CodeTargetCurrent=>codeDraft!=null && Graph.Find(codeNodeId)?.kind==ServiceKind.Lambda && codeCloud==Cloud && codeStack==(Cloud?.StackId??"");
        void RefreshCodeEditor()
        {
            if(!codePanel || codeDraft==null)return;
            codeTitle.text="LAMBDA / "+(Graph.Find(codeNodeId)?.name??codeNodeId)+" · LÍNEA "+(codeLine+1);
            string content=codeTab==0?string.Join("\n",codeDraft.source.Split('\n').Select((s,i)=>(i==codeLine?"> ":"  ")+(i+1).ToString("D3")+"  "+s)):
                codeTab==1?codeDraft.Difference():codeTab==2?"Evento:\n"+codeDraft.eventJson+"\n\n"+codeOutput:
                codeVersions.Length==0?"Carga AWS para ver sus versiones publicadas.":string.Join("\n",codeVersions.Select((v,i)=>(i==codeVersion?"> ":"  ")+"Versión "+v.version+" · "+v.description));
            // Wrap long source/diff lines into readable continuations; never hide review text.
            var lines=content.Split('\n').SelectMany(line=>Enumerable.Range(0,Math.Max(1,(line.Length+81)/82)).Select(part=>(part>0?"↳ ":"")+line.Substring(Math.Min(part*82,line.Length),Math.Min(82,Math.Max(0,line.Length-part*82))))).ToArray();codePage=Mathf.Clamp(codePage,0,Math.Max(0,(lines.Length-1)/14));
            codeBody.text=string.Join("\n",lines.Skip(codePage*14).Take(14));
            codeStatus.text=(codeBusy?"OPERACIÓN EN CURSO · ":"")+codeNotice;
            codeConfirm.SetAvailable(CanConfirmCode);
        }
        bool CanConfirmCode=>!codeBusy && !Busy && IsCloud && SessionReady && Deployed && CodeTargetCurrent && !string.IsNullOrEmpty(codeReviewOperation) && codeReviewHash==LambdaCodeDraft.Hash(codeDraft.source) && codeReviewRevision==codeDraft.revisionId && (codeReviewOperation!="rollback" || codeVersions.Length>codeVersion && codeReviewVersion==codeVersions[codeVersion].version);
        void EditCodeLine()
        {
            if(codeBusy || codeDraft==null)return;int line=codeLine;
            OpenCodeKeyboard("EDITAR LÍNEA "+(line+1)+" · conserva la indentación",codeDraft.source.Split('\n')[line],value=>{var lines=codeDraft.source.Split('\n');lines[line]=value;ChangeCode(string.Join("\n",lines));});
        }
        void OpenCodeKeyboard(string title,string value,Action<string> accept)
        {
            if(codeBusy || ConfiguringConnection || EditingText)return;
            Rig.ReleaseForConfiguration();if(designKeyboard)Destroy(designKeyboard.gameObject);
            designKeyboard=Panel(world,"Code keyboard",new Vector3(0,1.9f,1.1f),new Vector2(1160,900));
            keyboardCodeMode=true;codeCaps=false;keyboardValue=value;keyboardLimit=4096;acceptKeyboard=accept;
            Text(designKeyboard,title,new Vector2(0,390),new Vector2(1090,55),24,Cyan);
            keyboardText=Text(designKeyboard,value,new Vector2(0,306),new Vector2(1090,100),23,White);keyboardText.richText=false;
            string[] rows={"1234567890","qwertyuiop","asdfghjkl","zxcvbnm_","():,.'\"=[]","+-*/%<>!{}"};
            var letterKeys=new System.Collections.Generic.List<LabTarget>();
            for(int r=0;r<rows.Length;r++)for(int c=0;c<rows[r].Length;c++){string key=rows[r][c].ToString();var button=Button(designKeyboard,key,new Vector2((c-(rows[r].Length-1)/2f)*98,209-r*62),new Vector2(90,53),()=>TypeDesignText(codeCaps?key.ToUpperInvariant():key));if(char.IsLetter(key[0]))letterKeys.Add(button);}
            Button(designKeyboard,"Mayús / minús",new Vector2(-280,-270),new Vector2(300,44),()=>{codeCaps=!codeCaps;foreach(var key in letterKeys)key.Label.text=codeCaps?key.Label.text.ToUpperInvariant():key.Label.text.ToLowerInvariant();});
            Button(designKeyboard,"\\",new Vector2(10,-270),new Vector2(110,44),()=>TypeDesignText("\\"));
            Button(designKeyboard,"#",new Vector2(145,-270),new Vector2(110,44),()=>TypeDesignText("#"));
            Button(designKeyboard,"@",new Vector2(280,-270),new Vector2(110,44),()=>TypeDesignText("@"));
            Button(designKeyboard,"Espacio",new Vector2(-405,-215),new Vector2(245,54),()=>TypeDesignText(" "));
            Button(designKeyboard,"Indentar 4",new Vector2(-135,-215),new Vector2(245,54),()=>TypeDesignText("    "));
            Button(designKeyboard,"Borrar",new Vector2(135,-215),new Vector2(245,54),()=>TypeDesignText(null));
            Button(designKeyboard,"Vaciar",new Vector2(405,-215),new Vector2(245,54),()=>{keyboardValue="";keyboardText.text="";});
            Button(designKeyboard,"Cancelar edición",new Vector2(-265,-325),new Vector2(480,60),CloseDesignKeyboard);
            Button(designKeyboard,"Guardar texto",new Vector2(265,-325),new Vector2(480,60),()=>{var action=acceptKeyboard;var text=keyboardValue;CloseDesignKeyboard();action?.Invoke(text);RefreshCodeEditor();},Green);
        }
        IEnumerator LoadCode(bool replace,bool duringUpdate=false)
        {
            if(codeBusy && !duringUpdate || !CodeTargetCurrent || !IsCloud || !Deployed || !SessionReady || string.IsNullOrEmpty(codeStack)){codeNotice="Despliega y conecta esta Lambda para cargar su código AWS.";RefreshCodeEditor();yield break;}
            int epoch=codeEpoch;codeBusy=true;ClearCodeReview();var reader=codeReader=Cloud.CreateInspectionReader();RefreshCodeEditor();
            AwsCloudApi.CodeResult result=null;string error=null;yield return reader.ReadLambdaCode(codeStack,codeNodeId,(r,e)=>{result=r;error=e;});
            reader.Disconnect();if(epoch!=codeEpoch)yield break;codeReader=null;codeBusy=duringUpdate;
            if(!CodeTargetCurrent)yield break;
            if(result==null){codeUpdateStatus="";codeNotice=error??"Respuesta de código inválida.";RefreshCodeEditor();yield break;}
            codeUpdateStatus=result.updateStatus;codeDraft.baseSource=result.source;codeDraft.revisionId=result.revisionId;
            codeVersions=result.versions??Array.Empty<AwsCloudApi.CodeVersion>();codeVersion=Math.Max(0,codeVersions.Length-1);
            if(replace && result.updateStatus=="Successful"){codePreviousSource=codeDraft.source;codeDraft.Edit(result.source);SaveCodeDraft();}
            codeNotice="AWS · "+result.updateStatus+" · revisión cargada. "+(result.truncated?"Lista de versiones parcial.":"");RefreshCodeEditor();
        }
        void ReviewCode(string operation)
        {
            ClearCodeReview();if(codeBusy || Busy || !CodeTargetCurrent || !Deployed || !SessionReady || string.IsNullOrEmpty(codeDraft.revisionId) || codeUpdateStatus!="Successful"){codeNotice="Carga el código AWS y espera su estado Successful antes de revisar un cambio.";RefreshCodeEditor();return;}
            if(operation=="publish" && !codeDraft.Validated){codeNotice="Valida este borrador antes de publicarlo.";RefreshCodeEditor();return;}
            if(operation=="rollback" && codeVersions.Length==0){codeNotice="No hay una versión publicada para restaurar.";RefreshCodeEditor();return;}
            codeReviewOperation=operation;codeReviewHash=LambdaCodeDraft.Hash(codeDraft.source);codeReviewRevision=codeDraft.revisionId;codeTab=operation=="publish"?1:3;codePage=0;
            codeReviewVersion=codeVersions.Length>0?codeVersions[codeVersion].version:"";
            codeNotice=operation=="publish"?"CONFIRMACIÓN AWS: reemplaza el código ACTIVO de "+Graph.Find(codeNodeId).name+". "+(codeDraft.Tested?"Prueba aislada aprobada.":"Este borrador no tiene una prueba aprobada."):"CONFIRMACIÓN AWS: restaura código de versión "+codeVersions[codeVersion].version+" como nueva versión activa. No restaura configuración.";
            RefreshCodeEditor();
        }
        void ConfirmCodeReview()
        {
            if(!CanConfirmCode)return;
            string operation=codeReviewOperation;ClearCodeReview();StartCoroutine(RunCodeOperation(operation,true));
        }
        IEnumerator RunCodeOperation(string operation,bool confirmed=false,Action<string> completed=null)
        {
            if(codeBusy || codeDraft==null || !IsCloud || !SessionReady || !CodeTargetCurrent){codeNotice="Abre una Lambda y conecta AWS antes de continuar.";RefreshCodeEditor();completed?.Invoke(AssistantReply("blocked",codeNotice));yield break;}
            if((operation=="publish" || operation=="rollback") && (!confirmed || !Deployed)){completed?.Invoke(AssistantReply("blocked","Confirma en el editor de código."));yield break;}
            int epoch=codeEpoch;codeBusy=true;string sourceHash=LambdaCodeDraft.Hash(codeDraft.source);var reader=codeReader=Cloud.CreateInspectionReader();codeNotice="Procesando "+operation+"…";RefreshCodeEditor();
            var request=new AwsCloudApi.CodeRequest{source=codeDraft.source,eventJson=codeDraft.eventJson,stackId=codeStack,resourceId=codeNodeId,revisionId=codeDraft.revisionId,
                version=codeVersions.Length>0?codeVersions[codeVersion].version:"",confirmed=confirmed};
            AwsCloudApi.CodeResult result=null;string error=null;yield return reader.CodeOperation(operation,request,(r,e)=>{result=r;error=e;});
            reader.Disconnect();if(epoch!=codeEpoch)yield break;codeReader=null;
            if(!CodeTargetCurrent){codeBusy=false;completed?.Invoke(AssistantReply("stale","La sesión cambió. Consulta AWS antes de reintentar."));yield break;}
            if(result==null){codeBusy=false;if(operation=="publish" || operation=="rollback")codeUpdateStatus="";codeNotice=error??"Sin respuesta. Consulta AWS antes de reintentar una publicación.";RefreshCodeEditor();completed?.Invoke(AssistantReply("error",codeNotice));yield break;}
            if(result.valid && sourceHash==LambdaCodeDraft.Hash(codeDraft.source))codeDraft.validatedHash=sourceHash;
            if(operation=="test") {codeDraft.testedHash=result.passed?LambdaCodeDraft.Hash(codeDraft.source+"\n"+codeDraft.eventJson):"";codeOutput=result.message+"\n"+result.output+"\n"+result.logs;codeTab=2;codePage=0;}
            if(operation=="publish" || operation=="rollback") {
                codeDraft.rollbackVersion=result.rollbackVersion;codeNotice=result.message;RefreshCodeEditor();
                codeUpdateStatus="InProgress";
                for(int attempt=0;attempt<10;attempt++) {yield return new WaitForSecondsRealtime(2);if(epoch!=codeEpoch)yield break;if(!CodeTargetCurrent)break;yield return LoadCode(operation=="rollback",true);if(codeUpdateStatus=="Successful" || codeUpdateStatus=="Failed")break;}
                if(epoch!=codeEpoch)yield break;
                if(codeUpdateStatus!="Successful" && codeUpdateStatus!="Failed")codeNotice="AWS aceptó el cambio; su finalización aún no está confirmada. Carga AWS para consultar su estado.";
            }else codeNotice=result.message;
            codeBusy=false;
            string notice=codeNotice;SaveCodeDraft();codeNotice=notice;RefreshCodeEditor();
            completed?.Invoke(RedactAssistantData(JsonUtility.ToJson(result)));
        }
        public string ProposeLambdaCode(string arguments)
        {
            CodeToolRequest request;try{if(arguments==null || arguments.Length>18000)throw new ArgumentException();request=JsonUtility.FromJson<CodeToolRequest>(arguments);}catch(ArgumentException){return AssistantReply("invalid","Propuesta de código inválida.");}
            if(request==null || request.baseRevision!=revision || !AssistantCanEdit)return AssistantReply("stale","Obtén contexto y termina la operación actual.");
            if(Graph.Find(request.nodeId)?.kind!=ServiceKind.Lambda)return AssistantReply("invalid","Selecciona una Lambda existente.");
            OpenLambdaEditor(request.nodeId);
            if(request.baseSourceHash!=LambdaCodeDraft.Hash(codeDraft.source))return AssistantReply("stale","El borrador cambió. Lee su código actual.");
            if(!LambdaCodeDraft.Readable(request.source) || (request.eventJson?.Length??0)>4096)return AssistantReply("invalid","Código hasta 8 KiB y evento JSON hasta 4 KiB.");
            ChangeCode(request.source);if(!string.IsNullOrEmpty(request.eventJson))codeDraft.eventJson=request.eventJson;
            SaveCodeDraft();codeTab=1;codePage=0;codeNotice="Propuesta de código para revisar. Valida y prueba antes de publicar.";RefreshCodeEditor();
            return AssistantReply("draft_saved","Borrador actualizado; todavía no se publicó ni ejecutó en tu Lambda.");
        }
        IEnumerator ExecuteCodeTool(string name,string arguments,Action<string> completed)
        {
            CodeToolRequest request=null;try{if(arguments?.Length<=18000)request=JsonUtility.FromJson<CodeToolRequest>(arguments);}catch(ArgumentException){}
            if(request==null || Graph.Find(request.nodeId)?.kind!=ServiceKind.Lambda || !AssistantCanEdit){completed(AssistantReply("blocked","Selecciona una Lambda y termina la operación actual."));yield break;}
            OpenLambdaEditor(request.nodeId);
            if(name=="get_lambda_code") {
                if(Deployed)yield return LoadCode(string.IsNullOrEmpty(codeDraft.revisionId) && codeDraft.source==LambdaCodeDraft.Starter);
                completed(JsonUtility.ToJson(new CodeContext{nodeId=codeNodeId,source=RedactAssistantData(codeDraft.source),sourceHash=LambdaCodeDraft.Hash(codeDraft.source),revisionId=codeDraft.revisionId,eventJson=RedactAssistantData(codeDraft.eventJson),validated=codeDraft.Validated,tested=codeDraft.Tested}));yield break;
            }
            if(request.action=="validate" || request.action=="test"){yield return RunCodeOperation(request.action,false,completed);yield break;}
            if(request.action=="review_publish" || request.action=="review_rollback"){ReviewCode(request.action=="review_publish"?"publish":"rollback");completed(AssistantReply("review_opened",codeNotice));yield break;}
            completed(AssistantReply(request.action=="open"?"opened":"unsupported","La publicación y restauración se confirman manualmente en el editor."));
        }
    }
}
