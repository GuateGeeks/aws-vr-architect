using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    [Serializable] public sealed class AssistantProposal
    {
        public int baseRevision;
        public string summary;
        public ResourceNode[] nodes;
        public ResourceLink[] links;
        public Architecture Build(Architecture current,int revision)
        {
            if(current==null || baseRevision!=revision) throw new ArgumentException("El diseño cambió. Pide una propuesta nueva.");
            if(nodes==null || links==null || nodes.Length<2 || nodes.Length>12 || links.Length>36) throw new ArgumentException("Propuesta fuera de los límites del laboratorio.");
            if(nodes.Any(n=>n==null || n.id==null || !Regex.IsMatch(n.id,@"\A[A-Za-z0-9_-]{1,64}\z") || n.name==null || n.name.Length>80 || n.name.Any(char.IsControl) || n.name.IndexOfAny(new[]{'<','>'})>=0))
                throw new ArgumentException("La propuesta contiene nombres o identificadores inválidos.");
            var graph=new Architecture {region=current.region,nodes=nodes.Select(n=>new ResourceNode{id=n.id,name=n.name,kind=n.kind,setting=n.setting,viewScale=current.Find(n.id)?.viewScale??0}).ToList(),links=links.Select(l=>l==null?null:new ResourceLink{from=l.from,to=l.to}).ToList()};
            var issues=graph.Validate();if(issues.Count>0) throw new ArgumentException(string.Join("\n",issues.Take(4)));
            if(graph.nodes.Any(n=>n.setting>=DesignSemantics.OptionCount(n.kind)))throw new ArgumentException("La propuesta usa una opción no disponible en esta versión.");
            // Positions come from the application, never model-provided coordinates.
            bool sameNodes=graph.nodes.Select(n=>n.id).OrderBy(v=>v).SequenceEqual(current.nodes.Select(n=>n.id).OrderBy(v=>v));
            for(int i=0;i<graph.nodes.Count;i++) graph.nodes[i].position=sameNodes?current.Find(graph.nodes[i].id).position:
                new Vector3((i%4-(Math.Min(4,graph.nodes.Count)-1)*.5f)*1.05f,1.52f,2.3f+(i/4)*.75f);
            return graph;
        }
        public static string Difference(Architecture before,Architecture after)
        {
            var lines=new List<string>();
            foreach(var n in before.nodes) if(after.Find(n.id)==null) lines.Add("− "+n.name);
            foreach(var n in after.nodes) {
                var old=before.Find(n.id);
                if(old==null) lines.Add("+ "+ServiceCatalog.Get(n.kind).Name+" · "+n.name);
                else if(old.kind!=n.kind || old.name!=n.name || old.setting!=n.setting) lines.Add("~ "+old.name+" → "+n.name+" · "+ServiceCatalog.Get(n.kind).Name+" / "+n.setting);
            }
            foreach(var l in before.links) if(!after.links.Any(n=>n.from==l.from && n.to==l.to)) lines.Add("− enlace "+before.Find(l.from).name+" → "+before.Find(l.to).name);
            foreach(var l in after.links) if(!before.links.Any(n=>n.from==l.from && n.to==l.to)) lines.Add("+ enlace "+after.Find(l.from).name+" → "+after.Find(l.to).name);
            return lines.Count==0?"Sin cambios de definición.":string.Join("\n",lines);
        }
    }
}
