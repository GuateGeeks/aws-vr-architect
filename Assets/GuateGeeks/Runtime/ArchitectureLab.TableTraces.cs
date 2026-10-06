using System.Collections.Generic;
using UnityEngine;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    // Faint power traces on the projection table: one per object, from the table's core out to the point under
    // its projector, with dashes flowing outward. They follow objects as they move. Decorative, not AWS traffic.
    public sealed partial class ArchitectureLab
    {
        const float TableSurface = .748f;
        static readonly Vector3 TableCenter = new Vector3(0, TableSurface, 2.65f);
        readonly List<LineRenderer> tableTraces = new List<LineRenderer>();
        static readonly Color TraceIdle = Hex("#3FB8CC"), TraceLit = Color.Lerp(Cyan, Ice, .4f);
        void TickTableTraces()
        {
            if (!workspace) return;
            int used = 0;
            foreach (var view in views.Values)
            {
                if (!view) continue;
                if (used == tableTraces.Count)
                {
                    var created = Line(workspace, "Table power trace", new Vector3[2], Cyan, .018f);
                    created.textureMode = LineTextureMode.Stretch; created.numCapVertices = 0; tableTraces.Add(created);
                }
                var line = tableTraces[used++];
                var under = DesignPoint(view.transform.position); under.y = TableSurface; // design space: the traces scale with the table
                var flat = under - TableCenter; float length = flat.magnitude;
                bool show = length > .75f && !view.Grabbed;
                line.enabled = show; if (!show) continue;
                var dir = flat / length;
                line.SetPosition(0, TableCenter + dir * .52f);
                line.SetPosition(1, under - dir * .27f * view.transform.localScale.x);
                bool lit = view == selected || view.Model.state == ResourceState.Ready;
                var material = Beam(lit ? TraceLit : TraceIdle);
                if (line.sharedMaterial != material) line.sharedMaterial = material;
                line.widthMultiplier = (lit ? .03f : .022f) * Table.Stroke;
            }
            for (int i = used; i < tableTraces.Count; i++) if (tableTraces[i]) tableTraces[i].enabled = false;
        }
    }
}
