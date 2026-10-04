using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // These descriptions follow the bounded compiler in GuateGeeksAWS2026, not all AWS capabilities.
    public static class DesignSemantics
    {
        public static string Operation(ServiceKind from, ServiceKind to)
        {
            if (to == ServiceKind.CloudWatch) return "asocia observabilidad";
            if (from == ServiceKind.S3) return "notifica creación de objeto";
            if (from == ServiceKind.SQS) return "entrega a consumidor";
            if (to == ServiceKind.DynamoDB || to == ServiceKind.S3) return "escribe";
            if (to == ServiceKind.EventBridge || to == ServiceKind.SQS) return "publica";
            return "invoca";
        }
        public static string Help(ServiceKind kind)
        {
            switch (kind)
            {
                case ServiceKind.ApiGateway: return "HTTP API con POST /demo y etapa $default. Esta plataforma conecta una Lambda. No admite REST API.";
                case ServiceKind.Lambda: return "Memoria predeterminada: 128 MB. Código de demo fijo, Python 3.13 y timeout de 10 s. Los enlaces definen sus destinos.";
                case ServiceKind.DynamoDB: return "Predeterminado: bajo demanda. Aprovisionada: 1 RCU y 1 WCU. Clave de partición fija: id (texto). Tabla cifrada.";
                case ServiceKind.S3: return "Predeterminado: versionado activo. Bucket privado y cifrado; expiración de objetos a 1 día. Un destino de notificación en esta plataforma.";
                case ServiceKind.SQS: return "Predeterminado: estándar. Visibilidad: 60 s; retención: 1 día. FIFO usa deduplicación. Un consumidor Lambda en esta plataforma.";
                case ServiceKind.EventBridge: return "Se crea un bus y una regla por destino. Los antiguos perfiles AWS Day / Aplicación no cambian el comportamiento del backend.";
                default: return "Dashboard para los servicios asociados. Retención predeterminada: 7 días; afecta logs Lambda asociados. No es una ruta de datos.";
            }
        }
        public static int OptionCount(ServiceKind kind) => kind == ServiceKind.ApiGateway || kind == ServiceKind.EventBridge ? 1 : ServiceCatalog.Get(kind).Settings.Length;
        public static string Definition(Architecture graph)
        {
            var copy = graph.Copy(); copy.nodes = copy.nodes.OrderBy(n => n.id).ToList();
            copy.links = copy.links.OrderBy(l => l.from).ThenBy(l => l.to).ToList();
            foreach (var n in copy.nodes) { n.position = Vector3.zero; n.viewScale=0; n.state = ResourceState.Draft; }
            return JsonUtility.ToJson(copy);
        }
        public static List<string> Auxiliaries(Architecture graph)
        {
            var result = new List<string>();
            if (graph.nodes.Any(n => n.kind == ServiceKind.Lambda)) result.Add("Lambda: grupos de logs. Reutiliza el rol del backend.");
            if (graph.nodes.Any(n => n.kind == ServiceKind.ApiGateway)) result.Add("API: integración, ruta POST /demo, etapa y permiso Lambda.");
            if (graph.links.Any(l => graph.Find(l.from)?.kind == ServiceKind.EventBridge && graph.Find(l.to)?.kind != ServiceKind.CloudWatch)) result.Add("EventBridge: reglas por destino y permisos / políticas.");
            if (graph.links.Any(l => graph.Find(l.from)?.kind == ServiceKind.SQS && graph.Find(l.to)?.kind == ServiceKind.Lambda)) result.Add("SQS → Lambda: event source mapping, lote de 1.");
            if (graph.links.Any(l => graph.Find(l.from)?.kind == ServiceKind.S3 && graph.Find(l.to)?.kind != ServiceKind.CloudWatch)) result.Add("S3: notificación y permiso Lambda / política SQS.");
            if (graph.nodes.Any(n => n.kind == ServiceKind.CloudWatch)) result.Add("CloudWatch: dashboard; no transporta mensajes.");
            return result;
        }
    }
}
