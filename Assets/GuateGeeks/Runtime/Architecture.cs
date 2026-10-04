using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    public enum ServiceKind { ApiGateway, Lambda, DynamoDB, S3, SQS, EventBridge, CloudWatch }
    public enum ResourceState { Draft, Provisioning, Ready, Failed }

    public sealed class ServiceDefinition
    {
        public ServiceKind Kind;
        public string Name, Glyph, Category, Description, SettingLabel;
        public string[] Settings;
        public Color Color;
        public ServiceDefinition(ServiceKind kind, string name, string glyph, string category, string description,
            string hex, string settingLabel, params string[] settings)
        {
            Kind = kind; Name = name; Glyph = glyph; Category = category; Description = description;
            ColorUtility.TryParseHtmlString(hex, out Color color); Color = color;
            SettingLabel = settingLabel; Settings = settings;
        }
    }

    public static class ServiceCatalog
    {
        public static readonly ServiceDefinition[] All = {
            new ServiceDefinition(ServiceKind.ApiGateway, "API Gateway", "API", "ENTRADA", "La puerta de entrada a tu arquitectura. Recibe solicitudes y activa una función.", "#C38AFF", "Tipo de API", "HTTP API", "REST API"),
            new ServiceDefinition(ServiceKind.Lambda, "Lambda", "λ", "CÓMPUTO", "Ejecuta código cuando ocurre un evento. Conecta una fuente con datos o una cola.", "#FFAA48", "Memoria", "128 MB", "256 MB", "512 MB", "1024 MB"),
            new ServiceDefinition(ServiceKind.DynamoDB, "DynamoDB", "DB", "BASE DE DATOS", "Guarda datos de tu aplicación en una tabla NoSQL. La clave de la demo es id.", "#65A9FF", "Capacidad", "Bajo demanda", "Aprovisionada"),
            new ServiceDefinition(ServiceKind.S3, "S3", "S3", "ALMACENAMIENTO", "Almacena objetos y archivos. Puede enviar eventos de nuevos objetos a Lambda.", "#6CE1AD", "Versionado", "Activado", "Desactivado"),
            new ServiceDefinition(ServiceKind.SQS, "SQS", "Q", "MENSAJERÍA", "Desacopla productores y consumidores con una cola de mensajes.", "#FF82BC", "Tipo de cola", "Estándar", "FIFO"),
            new ServiceDefinition(ServiceKind.EventBridge, "EventBridge", "EV", "EVENTOS", "Distribuye eventos a funciones o colas para construir flujos asíncronos.", "#DB92F9", "Bus", "AWS Day", "Aplicación"),
            new ServiceDefinition(ServiceKind.CloudWatch, "CloudWatch", "CW", "OBSERVABILIDAD", "Define un dashboard y la retención de logs Lambda asociados. No transporta mensajes.", "#68DCEB", "Retención de logs Lambda", "7 días", "14 días", "30 días")
        };
        public static ServiceDefinition Get(ServiceKind kind) => All[(int)kind];
        // Deliberately bounded teaching model, not an exhaustive AWS compatibility matrix.
        public static bool CanConnect(ServiceKind from, ServiceKind to)
        {
            if (to == ServiceKind.CloudWatch) return from != ServiceKind.CloudWatch;
            switch (from)
            {
                case ServiceKind.ApiGateway: return to == ServiceKind.Lambda;
                case ServiceKind.Lambda: return to == ServiceKind.DynamoDB || to == ServiceKind.S3 || to == ServiceKind.SQS || to == ServiceKind.EventBridge;
                case ServiceKind.S3: return to == ServiceKind.Lambda || to == ServiceKind.SQS;
                case ServiceKind.SQS: return to == ServiceKind.Lambda;
                case ServiceKind.EventBridge: return to == ServiceKind.Lambda || to == ServiceKind.SQS;
                default: return false;
            }
        }
    }

    [Serializable]
    public sealed class ResourceNode
    {
        public string id;
        public ServiceKind kind;
        public string name;
        public Vector3 position;
        public int setting;
        // Zero inherits the headset size preference; visual metadata is ignored by AWS.
        public float viewScale;
        public ResourceState state;
    }
    [Serializable]
    public sealed class ResourceLink { public string from, to; }
    [Serializable]
    public sealed class Architecture
    {
        public const int MaxNodes = 12;
        public int schemaVersion = 1;
        public string region = "us-east-1";
        public List<ResourceNode> nodes = new List<ResourceNode>();
        public List<ResourceLink> links = new List<ResourceLink>();

        public ResourceNode Add(ServiceKind kind, Vector3 position)
        {
            if (nodes.Count >= MaxNodes) return null;
            int suffix = 1; string prefix = ServiceCatalog.Get(kind).Name + " ";
            while (nodes.Any(n => n.name == prefix + suffix)) suffix++;
            var node = new ResourceNode { id = Guid.NewGuid().ToString("N"), kind = kind,
                name = prefix + suffix, position = position };
            nodes.Add(node); return node;
        }
        public ResourceNode Find(string id) => nodes.Find(n => n.id == id);
        public void Remove(string id) { nodes.RemoveAll(n => n.id == id); links.RemoveAll(l => l.from == id || l.to == id); }
        public bool Connect(string from, string to, out string message)
        {
            if (!CanConnect(from, to, out message)) return false;
            links.Add(new ResourceLink { from = from, to = to }); return true;
        }
        public bool CanConnect(string from, string to, out string message)
        {
            var a = Find(from); var b = Find(to);
            if (a == null || b == null) { message = "Selecciona dos recursos existentes."; return false; }
            if (from == to) { message = "Selecciona un recurso diferente como destino."; return false; }
            if (links.Any(l => l.from == from && l.to == to)) { message = "Estos recursos ya están conectados."; return false; }
            if (!ServiceCatalog.CanConnect(a.kind, b.kind)) { message = "Ruta no disponible en esta demo: " + a.name + " → " + b.name; return false; }
            if (links.Count >= 36) { message = "Esta plataforma admite hasta 36 conexiones."; return false; }
            if (a.kind == ServiceKind.S3 && b.kind == ServiceKind.SQS && b.setting == 1)
            { message = "S3 no notifica directamente a SQS FIFO. Elige una cola estándar."; return false; }
            if ((a.kind == ServiceKind.ApiGateway || a.kind == ServiceKind.SQS) && b.kind == ServiceKind.Lambda && links.Any(l => l.from == from && Find(l.to)?.kind == ServiceKind.Lambda))
            { message = "Esta plataforma admite un destino Lambda por API o cola."; return false; }
            if (a.kind == ServiceKind.S3 && b.kind != ServiceKind.CloudWatch && links.Any(l => l.from == from && Find(l.to)?.kind != ServiceKind.CloudWatch))
            { message = "Esta plataforma admite un destino de notificación por bucket."; return false; }
            if (HasPath(to, from, new HashSet<string>())) { message = "Este enlace crearía un ciclo. Prueba otra ruta."; return false; }
            message = a.name + " → " + b.name; return true;
        }
        bool HasPath(string start, string goal, HashSet<string> seen)
        {
            if (start == goal) return true;
            if (!seen.Add(start)) return false;
            return links.Where(l => l.from == start).Any(l => HasPath(l.to, goal, seen));
        }
        public List<string> Validate()
        {
            var issues = new List<string>();
            if (schemaVersion != 1) issues.Add("Versión de diseño no compatible.");
            if (region != "us-east-1" && region != "us-west-2") issues.Add("Región no disponible en la demo.");
            if (nodes == null || links == null) { issues.Add("El diseño está incompleto."); return issues; }
            if (nodes.Count < 2) issues.Add("Agrega al menos dos servicios.");
            if (nodes.Count > MaxNodes) issues.Add("El límite es de 12 recursos por diseño.");
            if (links.Count > 36) issues.Add("Esta plataforma admite hasta 36 conexiones.");
            if (nodes.Any(n => n == null || string.IsNullOrEmpty(n.id) || !Enum.IsDefined(typeof(ServiceKind), n.kind)))
            { issues.Add("El diseño contiene recursos inválidos."); return issues; }
            if (nodes.Select(n => n.id).Distinct().Count() != nodes.Count) issues.Add("Hay identificadores duplicados.");
            if (links.Any(l => l == null || Find(l.from) == null || Find(l.to) == null))
            { issues.Add("El diseño contiene enlaces sin recurso."); return issues; }
            if (links.Select(l => l.from + ":" + l.to).Distinct().Count() != links.Count) issues.Add("Hay enlaces duplicados.");
            foreach (var n in nodes)
            {
                if (string.IsNullOrWhiteSpace(n.name) || n.name.Length > 80) issues.Add("Nombre de 1 a 80 caracteres requerido: " + n.id);
                var destinations = links.Where(l => l.from == n.id).Select(l => Find(l.to)).ToList();
                if (n.kind == ServiceKind.ApiGateway && (n.setting != 0 || !destinations.Any(t => t.kind == ServiceKind.Lambda))) issues.Add(n.name + ": define HTTP API con un destino Lambda.");
                if ((n.kind == ServiceKind.ApiGateway || n.kind == ServiceKind.SQS) && destinations.Count(t => t.kind == ServiceKind.Lambda) > 1) issues.Add(n.name + ": esta plataforma admite un destino Lambda.");
                if (n.kind == ServiceKind.S3 && destinations.Count(t => t.kind != ServiceKind.CloudWatch) > 1) issues.Add(n.name + ": esta plataforma admite una notificación.");
                if (n.kind == ServiceKind.S3 && destinations.Any(t => t.kind == ServiceKind.SQS && t.setting == 1)) issues.Add(n.name + ": S3 no notifica directamente a SQS FIFO.");
                if (n.setting < 0 || n.setting >= ServiceCatalog.Get(n.kind).Settings.Length) issues.Add("Configuración inválida: " + n.name);
                if (!IsFinite(n.viewScale) || (n.viewScale!=0 && (n.viewScale<.5f || n.viewScale>1.25f))) issues.Add("Tamaño visual inválido: " + n.name);
                if (!IsFinite(n.position.x) || !IsFinite(n.position.y) || !IsFinite(n.position.z)) issues.Add("Posición inválida: " + n.name);
                if (!links.Any(l => l.from == n.id || l.to == n.id)) issues.Add(n.name + " está sin conectar.");
            }
            foreach (var l in links)
            {
                if (l.from == l.to || !ServiceCatalog.CanConnect(Find(l.from).kind, Find(l.to).kind)) issues.Add("Conexión no compatible.");
                if (HasPath(l.to, l.from, new HashSet<string>())) { issues.Add("El diseño contiene un ciclo."); break; }
            }
            return issues;
        }
        static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        public Architecture Copy() => JsonUtility.FromJson<Architecture>(JsonUtility.ToJson(this));
        public void ResetStates() { foreach (var node in nodes) node.state = ResourceState.Draft; }
        public static Architecture Preset(int index)
        {
            var graph = new Architecture();
            ServiceKind[] kinds = index == 1 ? new[] { ServiceKind.EventBridge, ServiceKind.SQS, ServiceKind.Lambda, ServiceKind.DynamoDB }
                : index == 2 ? new[] { ServiceKind.S3, ServiceKind.Lambda, ServiceKind.DynamoDB }
                : new[] { ServiceKind.ApiGateway, ServiceKind.Lambda, ServiceKind.DynamoDB };
            for (int i = 0; i < kinds.Length; i++)
            {
                graph.Add(kinds[i], new Vector3((i - (kinds.Length - 1) * .5f) * (kinds.Length == 4 ? 1f : 1.15f), 1.52f, 2.65f));
                if (i > 0) graph.Connect(graph.nodes[i - 1].id, graph.nodes[i].id, out _);
            }
            return graph;
        }
    }
}
