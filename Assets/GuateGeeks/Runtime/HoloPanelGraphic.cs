using UnityEngine;
using UnityEngine.UI;

namespace GuateGeeks.AwsVr
{
    // One generated UI mesh: clipped glass, border, corners and small registration marks.
    // No texture, sprite pack, extra canvas, or post-processing pass is required.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HoloPanelGraphic : MaskableGraphic
    {
        public Color Accent = new Color(.22f, .72f, .82f, 1);
        public bool Detailed = true;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); Rect r = rectTransform.rect; float cut = Detailed ? 20 : 10;
            var p = new[] { new Vector2(r.xMin + cut, r.yMin), new Vector2(r.xMax, r.yMin),
                new Vector2(r.xMax, r.yMax - cut), new Vector2(r.xMax - cut, r.yMax),
                new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin + cut) };
            vh.AddVert(r.center, color, Vector2.zero);
            for (int i = 0; i < p.Length; i++)
            {
                Color shade = color * Mathf.Lerp(.68f, 1, Mathf.InverseLerp(r.yMin, r.yMax, p[i].y)); shade.a = color.a;
                vh.AddVert(p[i], shade, Vector2.zero);
            }
            for (int i = 0; i < p.Length; i++) vh.AddTriangle(0, 1 + i, 1 + (i + 1) % p.Length);
            Color edge = Accent; edge.a = .52f;
            for (int i = 0; i < p.Length; i++) Segment(vh, p[i], p[(i + 1) % p.Length], 1.1f, edge);
            Segment(vh, new Vector2(r.xMin, r.yMax - 48), new Vector2(r.xMin, r.yMax), 3, Accent);
            Segment(vh, new Vector2(r.xMin, r.yMax), new Vector2(r.xMin + 48, r.yMax), 3, Accent);
            Segment(vh, new Vector2(r.xMax - 48, r.yMin), new Vector2(r.xMax, r.yMin), 3, Accent);
            // Inset rim and amber registration marks give the glass a layered chassis.
            Color inset = Accent; inset.a = .13f;
            Segment(vh, new Vector2(r.xMin + 6, r.yMin + 22), new Vector2(r.xMin + 6, r.yMax - 12), 1, inset);
            Segment(vh, new Vector2(r.xMin + 12, r.yMax - 6), new Vector2(r.xMax - 24, r.yMax - 6), 1, inset);
            Color warm = LabVisuals.Orange; warm.a = .8f;
            for (int i = 0; i < 3; i++) Segment(vh, new Vector2(r.xMin + 22 + i * 9, r.yMin + 5), new Vector2(r.xMin + 27 + i * 9, r.yMin + 5), 2, warm);
            if (!Detailed) return;
            Color scan = Accent; scan.a = .025f;
            for (float y = r.yMin + 26; y < r.yMax - 26; y += 26)
                Segment(vh, new Vector2(r.xMin + 10, y), new Vector2(r.xMax - 10, y), .7f, scan);
            for (int i = 0; i < 7; i++)
                Segment(vh, new Vector2(r.xMax - 10, r.yMax - 45 - i * 9), new Vector2(r.xMax - 5, r.yMax - 45 - i * 9), 2, edge);
        }
        static void Segment(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
            int start = vh.currentVertCount;
            vh.AddVert(a - n, color, Vector2.zero); vh.AddVert(a + n, color, Vector2.zero);
            vh.AddVert(b + n, color, Vector2.zero); vh.AddVert(b - n, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
