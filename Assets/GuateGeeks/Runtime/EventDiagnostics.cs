using System;
using System.Linq;

namespace GuateGeeks.AwsVr
{
    public static class EventDiagnostics
    {
        [Serializable] public sealed class Evidence
        {
            public string nodeId,eventId,checkedAt,message;
            public int records,errors,deliveries;
            public bool partial;
            public string[] sample;
        }
        public static Evidence Summarize(string nodeId,string eventId,AwsCloudApi.InspectionPage page)
        {
            var entries=(page.entries??Array.Empty<AwsCloudApi.InspectionEntry>()).Where(e=>e.eventId==eventId).ToArray();
            int errors=entries.Count(e=>string.Equals(e.level,"ERROR",StringComparison.OrdinalIgnoreCase) || e.stage=="failed" || e.stage=="error");
            int deliveries=entries.Count(e=>e.stage=="delivered");
            return new Evidence{nodeId=nodeId,eventId=eventId,checkedAt=DateTime.UtcNow.ToString("o"),records=entries.Length,errors=errors,deliveries=deliveries,
                partial=!string.IsNullOrEmpty(page.cursor) || entries.Any(e=>e.truncated),
                message=errors>0?"Errores observados; revisa la evidencia antes de cambiar el diseño.":entries.Length==0?"Sin evidencia todavía. Puede haber demora; no demuestra un fallo.":"Evidencia del evento observada; no confirma por sí sola el flujo completo.",
                sample=entries.Where(e=>e.level=="ERROR" || e.stage=="failed").Concat(entries).Distinct().Take(3).Select(e=>{
                    string text=ArchitectureLab.RedactAssistantData(e.text);return text.Length>700?text.Substring(0,700)+"…":text;
                }).ToArray()};
        }
    }
}
