using UnityEngine;
using UnityEngine.UI;

namespace GuateGeeks.AwsVr
{
    // One generated UI mesh: translucent light-glass, soft emitted rim, corner brackets and registration marks.
    // The glow is geometry with an alpha falloff, so no texture, extra canvas or bloom pass is required on Quest.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HoloPanelGraphic : MaskableGraphic
    {
        Color accent = new Color(.22f, .72f, .82f, 1);
        public Color Accent { get => accent; set { if (accent == value) return; accent = value; SetVerticesDirty(); } }
        public bool Detailed = true;
        bool ghost;
        float highlight, reveal = 1;
        // Ghost surfaces are secondary actions: almost no fill until they are aimed at.
        public bool Ghost { get => ghost; set { if (ghost == value) return; ghost = value; SetVerticesDirty(); } }
        // 0 = idle, 1 = aimed/touched. Brackets open outward and the rim brightens.
        public float Highlight { get => highlight; set { value = Mathf.Clamp01(value); if (Mathf.Approximately(value, highlight)) return; highlight = value; SetVerticesDirty(); } }
        // 0..1 materialisation progress used by HoloReveal (fill fades in behind a bright scan line).
        public float Reveal { get => reveal; set { value = Mathf.Clamp01(value); if (Mathf.Approximately(value, reveal)) return; reveal = value; SetVerticesDirty(); } }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); Rect r = rectTransform.rect; float cut = Detailed ? 18 : 9;
            var p = new[] { new Vector2(r.xMin + cut, r.yMin), new Vector2(r.xMax, r.yMin),
                new Vector2(r.xMax, r.yMax - cut), new Vector2(r.xMax - cut, r.yMax),
                new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin + cut) };
            float shown = Mathf.SmoothStep(0, 1, reveal);
            // Translucent glass with a cooler, brighter upper edge.
            Color fill = color; fill.a *= shown;
            Color top = Color.Lerp(fill, Accent, .07f + .08f * highlight); top.a = fill.a;
            // Alpha stays uniform: in linear colour space even 8% less coverage lets ~25% perceived light through.
            Color bottom = fill * .72f; bottom.a = fill.a * (ghost ? .55f : 1);
            vh.AddVert(r.center, Color.Lerp(bottom, top, .5f), Vector2.zero);
            for (int i = 0; i < p.Length; i++) vh.AddVert(p[i], Color.Lerp(bottom, top, Mathf.InverseLerp(r.yMin, r.yMax, p[i].y)), Vector2.zero);
            for (int i = 0; i < p.Length; i++) vh.AddTriangle(0, 1 + i, 1 + (i + 1) % p.Length);

            // Emitted rim: a soft outward falloff fakes bloom for the cost of a few quads.
            float glowWidth = (Detailed ? 16 : 7) * (1 + highlight * .8f);
            Color glow = Accent; glow.a = (Detailed ? .10f : ghost ? .0f : .05f) + .22f * highlight; glow.a *= shown;
            if (glow.a > .004f) for (int i = 0; i < p.Length; i++) Glow(vh, p[i], p[(i + 1) % p.Length], glowWidth, glow);
            Color edge = Accent; edge.a = (ghost ? .16f : Detailed ? .42f : .34f) + .5f * highlight;
            for (int i = 0; i < p.Length; i++)
            {
                if (ghost && i != 0) continue; // Ghost actions keep only a baseline until aimed at.
                Segment(vh, p[i], p[(i + 1) % p.Length], 1.1f + highlight * .6f, edge);
            }
            if (ghost && highlight > .01f)
            {
                Color ring = edge; ring.a *= highlight;
                for (int i = 1; i < p.Length; i++) Segment(vh, p[i], p[(i + 1) % p.Length], 1.1f, ring);
            }

            // Corner brackets: the signature of every holographic surface. They open slightly on aim.
            float arm = Detailed ? 42 : Mathf.Min(16, r.height * .4f), thick = Detailed ? 2.6f : 1.8f, open = highlight * (Detailed ? 5 : 4);
            Color bracket = Accent; bracket.a = Detailed ? .95f : ghost ? .45f * highlight : .55f + .45f * highlight;
            if (bracket.a > .01f)
            {
                Bracket(vh, new Vector2(r.xMin - open, r.yMax + open), 1, -1, arm, thick, bracket);
                Bracket(vh, new Vector2(r.xMax + open, r.yMin - open), -1, 1, arm, thick, bracket);
                if (Detailed || highlight > .01f)
                {
                    Color minor = bracket; minor.a *= Detailed ? .55f : 1;
                    Bracket(vh, new Vector2(r.xMax - cut * .5f + open, r.yMax + open), -1, -1, arm * .6f, thick * .8f, minor);
                    Bracket(vh, new Vector2(r.xMin + cut * .5f - open, r.yMin - open), 1, 1, arm * .6f, thick * .8f, minor);
                }
            }
            if (reveal < 1)
            {
                // A bright scan line sweeps down while the panel materialises.
                float y = Mathf.Lerp(r.yMax, r.yMin, shown); Color scanLine = Accent; scanLine.a = .9f * (1 - shown * shown);
                Segment(vh, new Vector2(r.xMin - 6, y), new Vector2(r.xMax + 6, y), 2.2f, scanLine);
                Glow(vh, new Vector2(r.xMax + 6, y), new Vector2(r.xMin - 6, y), 12, new Color(Accent.r, Accent.g, Accent.b, .25f * (1 - shown)));
            }
            if (!Detailed) return;
            Color inset = Accent; inset.a = .10f * shown;
            Segment(vh, new Vector2(r.xMin + 7, r.yMin + 24), new Vector2(r.xMin + 7, r.yMax - 14), 1, inset);
            Segment(vh, new Vector2(r.xMin + 14, r.yMax - 7), new Vector2(r.xMax - 26, r.yMax - 7), 1, inset);
            Color warm = LabVisuals.Orange; warm.a = .75f * shown;
            for (int i = 0; i < 3; i++) Segment(vh, new Vector2(r.xMin + 24 + i * 9, r.yMin + 6), new Vector2(r.xMin + 29 + i * 9, r.yMin + 6), 2, warm);
            Color scan = Accent; scan.a = .02f * shown;
            for (float y = r.yMin + 28; y < r.yMax - 28; y += 28)
                Segment(vh, new Vector2(r.xMin + 12, y), new Vector2(r.xMax - 12, y), .7f, scan);
            // Ruler ticks along the right edge read as an instrument scale.
            Color tick = Accent; tick.a = .45f * shown;
            for (int i = 0; i < 9; i++)
                Segment(vh, new Vector2(r.xMax - (i % 4 == 0 ? 13 : 9), r.yMax - 46 - i * 9), new Vector2(r.xMax - 5, r.yMax - 46 - i * 9), 1.4f, tick);
        }
        static void Bracket(VertexHelper vh, Vector2 corner, float dx, float dy, float arm, float width, Color color)
        {
            Segment(vh, corner, corner + new Vector2(dx * arm, 0), width, color);
            Segment(vh, corner + new Vector2(0, dy * width * .5f), corner + new Vector2(0, dy * arm), width, color);
        }
        // Outward quad with alpha falloff; polygon vertices are counter-clockwise.
        static void Glow(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 d = (b - a).normalized; Vector2 n = new Vector2(d.y, -d.x) * width;
            Color clear = color; clear.a = 0; int start = vh.currentVertCount;
            vh.AddVert(a, color, Vector2.zero); vh.AddVert(b, color, Vector2.zero);
            vh.AddVert(b + n, clear, Vector2.zero); vh.AddVert(a + n, clear, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
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
