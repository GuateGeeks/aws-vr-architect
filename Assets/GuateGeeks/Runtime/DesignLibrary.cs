using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    [Serializable] public sealed class SavedDesign
    {
        public int version = 1;
        public string id, name, savedUtc;
        public Architecture architecture;
    }
    public sealed class DesignLibrary
    {
        readonly string directory;
        public DesignLibrary(string directory) { this.directory = directory; }
        public SavedDesign Save(string name, Architecture graph)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 40) throw new ArgumentException("Nombre de 1 a 40 caracteres.");
            if (!Readable(graph)) throw new ArgumentException("Diseño inválido.");
            Directory.CreateDirectory(directory);
            if (Directory.GetFiles(directory, "*.json").Length >= 48) throw new IOException("Biblioteca llena (48 diseños). Exporta o elimina archivos desde el dispositivo.");
            var entry = new SavedDesign { id = Guid.NewGuid().ToString("N"), name = name.Trim(), savedUtc = DateTime.UtcNow.ToString("o"), architecture = graph.Copy() };
            entry.architecture.ResetStates();
            var path = Path.Combine(directory, entry.id + ".json");
            File.WriteAllText(path + ".tmp", JsonUtility.ToJson(entry, true)); File.Move(path + ".tmp", path);
            return entry;
        }
        public List<SavedDesign> Read()
        {
            var entries = new List<SavedDesign>();
            if (!Directory.Exists(directory)) return entries;
            foreach (var path in Directory.GetFiles(directory, "*.json").Take(48))
            {
                try {
                    if (new FileInfo(path).Length > 131072) continue;
                    var entry = JsonUtility.FromJson<SavedDesign>(File.ReadAllText(path));
                    if (entry != null && entry.version == 1 && !string.IsNullOrWhiteSpace(entry.name) && entry.name.Length <= 40 && Readable(entry.architecture)) entries.Add(entry);
                } catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException) { /* A corrupt entry does not hide the other designs. */ }
            }
            return entries.OrderByDescending(e => e.savedUtc).ToList();
        }
        public static bool Readable(Architecture g)
        {
            if (g == null || g.schemaVersion != 1 || g.nodes == null || g.links == null || g.nodes.Count > 12 || g.links.Count > 36) return false;
            if (g.region != "us-east-1" && g.region != "us-west-2") return false;
            if (g.nodes.Any(n => n == null || string.IsNullOrEmpty(n.id) || string.IsNullOrWhiteSpace(n.name) || n.name.Length > 80 || !Enum.IsDefined(typeof(ServiceKind), n.kind))) return false;
            if (g.nodes.Select(n => n.id).Distinct().Count() != g.nodes.Count) return false;
            if (g.nodes.Any(n => n.setting < 0 || n.setting >= ServiceCatalog.Get(n.kind).Settings.Length || float.IsNaN(n.position.sqrMagnitude) || float.IsInfinity(n.position.sqrMagnitude) || float.IsNaN(n.viewScale) || float.IsInfinity(n.viewScale) || (n.viewScale!=0 && (n.viewScale<.5f || n.viewScale>1.25f)))) return false;
            return g.links.All(l => l != null && g.Find(l.from) != null && g.Find(l.to) != null);
        }
    }
}
