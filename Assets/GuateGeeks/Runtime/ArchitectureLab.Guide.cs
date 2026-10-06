using System.Linq;
using UnityEngine;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    public sealed partial class ArchitectureLab
    {
        RectTransform guidedPanel;
        TMPro.TMP_Text guidedTitle, guidedBody;
        LabTarget guidedAction, guidedSecondary;
        int guideStep, guideBaseline;
        bool guideLogs, guideItems, guideCleaned, guideCloud;
        string guideEvent, guideStack;
        AwsCloudApi guideSource;
        float nextGuideUpdate;
        int guideRevision;
        public int GuidedStep => guideStep;
        public void OpenQuickSettings() { if(!Busy && !EditingText && !ConfiguringConnection) ToggleUnifiedSettings(); }
        public void OpenGuidedDemo()
        {
            if(Busy || EditingText || ConfiguringConnection || Placing) return;
            if(!guidedPanel) {
                guidedPanel=Panel(PersonalRoot,"Guided mission",new Vector3(-1.85f,1.9f,1.8f),new Vector2(710,560),-40);
                guidedPanel.localScale=Vector3.one*.00125f;
                guidedTitle=Text(guidedPanel,"",new Vector2(0,216),new Vector2(650,65),27,Cyan);
                guidedBody=Text(guidedPanel,"",new Vector2(0,45),new Vector2(650,260),23,White); guidedBody.richText=false;
                guidedAction=Button(guidedPanel,"",new Vector2(-167,-147),new Vector2(318,58),AdvanceGuide,Green);
                guidedSecondary=Button(guidedPanel,"",new Vector2(167,-147),new Vector2(318,58),SecondaryGuide);
                Button(guidedPanel,"Cerrar guía",new Vector2(-167,-224),new Vector2(318,48),()=>guidedPanel.gameObject.SetActive(false));
                Button(guidedPanel,"Reiniciar guía",new Vector2(167,-224),new Vector2(318,48),()=> {guideStep=0;DrawGuide();});
            }
            guidedPanel.gameObject.SetActive(true); DrawGuide();
        }
        void DrawGuide()
        {
            if(!guidedPanel) return;
            string[] titles={"BIENVENIDO AL LABORATORIO","01 / CONSTRUIR Y REVISAR","02 / DESPLEGAR","03 / ENVIAR UN EVENTO","04 / COMPROBAR RESULTADOS","05 / LIMPIAR","MISIÓN COMPLETA"};
            string[] bodies={
                "Diseña → despliega → envía → inspecciona → limpia.\n\nPuedes practicar gratis en modo local. AWS usa recursos reales y confirmaciones explícitas. El diseño actual se conserva al empezar.",
                "Agrega servicios con Crear. Une SALIDA y ENTRADA, define sus propiedades y revisa el diseño.\n\nEl ejemplo API reemplaza la mesa; Deshacer permite recuperarla.",
                "Revisa región y modo antes de confirmar.\n\nEn AWS elige un slot vacío, o retoma el mismo diseño. Un slot ocupado por otro diseño requiere revisión y limpieza explícita.",
                "Envía un evento por el origen de tu arquitectura.\n\nAceptado significa enviado; los logs y la tabla confirman lo ocurrido después.",
                IsCloud ? "Abre una Lambda y una tabla. En cada lector pulsa Seguir último evento enviado.\n\nLogs: "+(guideLogs?"confirmados":"pendientes")+" · Ítem: "+(guideItems?"confirmado":"pendiente")+"\nSolo cuentan registros del mismo eventId." : "Modo local: el evento y su respuesta son simulados.\n\nLa lectura de CloudWatch y DynamoDB requiere conectar AWS, desplegar y repetir el recorrido.",
                IsCloud ? "Detén los eventos. Abre tu slot, confirma su identidad y usa Limpiar slot.\n\nLa guía termina cuando AWS confirma que está vacío. Cerrar el visor no elimina recursos." : "Limpia la mesa con su confirmación. Puedes recuperarla con Deshacer. No hay recursos AWS en esta práctica.",
                "Completaste el recorrido.\n\nPuedes repetirlo con Eventos + cola o Procesar archivos. Guarda tus diseños en Biblioteca y trae los paneles hacia ti desde la muñeca si pierdes alguno."};
            string[] actions={"Empezar con mi diseño","Revisar y continuar","Abrir despliegue","Enviar evento",IsCloud?"Abrir logs":"Entendido: simulado",IsCloud?"Abrir mi slot":"Limpiar mesa","Terminar"};
            string[] secondary={"Ver controles","Ejemplo API","Conexión AWS","Ver diseño",IsCloud?"Abrir ítems":"Conexión AWS","Volver al diseño","Repetir"};
            guidedTitle.text=titles[guideStep]; guidedBody.text=bodies[guideStep]; guidedAction.Label.text=actions[guideStep]; guidedSecondary.Label.text=secondary[guideStep];
            guidedAction.SetAvailable(!Busy && !EditingText && !ConfiguringConnection && !Placing); guidedSecondary.SetAvailable(!Busy && !EditingText && !ConfiguringConnection && !Placing);
        }
        void AdvanceGuide()
        {
            if(Busy || EditingText || ConfiguringConnection || Placing) return;
            switch(guideStep) {
                case 0: guideCloud=IsCloud; guideSource=Cloud; guideStep=1; guideLogs=guideItems=guideCleaned=false; break;
                case 1:
                    ShowReview(); if(Graph.Validate().Count==0 && !HasPendingDefinition) {guideRevision=revision; guideStep=2; guideCleaned=false;} break;
                case 2: RequestDeployment(); break;
                case 3: selected=null; RefreshSelection(); TestFlow(); break;
                case 4: if(IsCloud) InspectGuided(ServiceKind.Lambda); else guideStep=5; break;
                case 5: if(IsCloud) {OpenSlots();LoadSlot(Cloud.Slot);} else ConfirmReset(); break;
                case 6: PlayerPrefs.SetInt("GuateGeeks.Guide.Completed.v1",1);PlayerPrefs.Save();guidedPanel.gameObject.SetActive(false);break;
            }
            DrawGuide();
        }
        void SecondaryGuide()
        {
            if(Busy || EditingText || ConfiguringConnection || Placing) return;
            if(guideStep==1) {LoadPreset(0);guideStep=1;}
            else if(guideStep==4 && IsCloud) InspectGuided(ServiceKind.DynamoDB);
            else if(guideStep==3 || guideStep==5) ShowInspector();
            else if(guideStep==6) guideStep=0;
            else OpenQuickSettings();
            DrawGuide();
        }
        void InspectGuided(ServiceKind kind)
        {
            var node=Graph.nodes.FirstOrDefault(n=>n.kind==kind);
            if(node==null) {SetStatus("Este diseño no tiene "+kind+". Usa el ejemplo API para el recorrido completo.");return;}
            Select(views[node.id]); InspectSelectedCloudObject();
        }
        void TickGuide()
        {
            if(!guidedPanel || !guidedPanel.gameObject.activeSelf || Graph==null) return;
            if(Time.unscaledTime < nextGuideUpdate) return; nextGuideUpdate=Time.unscaledTime+.25f;
            if(guideStep>0 && guideStep<6 && (guideCloud!=IsCloud || guideSource!=Cloud)) {guideStep=1;guideCloud=IsCloud;guideSource=Cloud;guideLogs=guideItems=guideCleaned=false;}
            if(IsCloud && guideStep>=3 && guideStep<5 && guideStack!=Cloud.StackId) {guideStep=1;guideLogs=guideItems=false;}
            if(guideStep>=2 && guideStep<5 && guideRevision!=revision) {guideStep=1;guideLogs=guideItems=false;}
            if(guideStep==2 && Deployed) {guideStep=3;guideStack=Cloud?.StackId;guideBaseline=EventCount;}
            if(guideStep==3 && EventCount>guideBaseline) {guideStep=4;guideEvent=Cloud?.LastEventId;guideLogs=guideItems=false;}
            if(guideStep==4 && IsCloud && guideLogs && guideItems) guideStep=5;
            if(guideStep==5 && (IsCloud?guideCleaned:Graph.nodes.Count==0)) guideStep=6;
            DrawGuide();
        }
        void GuideObserved(string mode, AwsCloudApi.InspectionPage page, string stack)
        {
            if(guideStep!=4 || !IsCloud || !Deployed || stack!=Cloud.StackId || string.IsNullOrEmpty(guideEvent)) return;
            if(!page.entries.Any(e=>e.eventId==guideEvent)) return;
            if(mode=="logs") guideLogs=true; else if(mode=="items") guideItems=true;
        }
    }
}
