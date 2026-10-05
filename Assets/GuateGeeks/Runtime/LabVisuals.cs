using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using TMPro;

namespace GuateGeeks.AwsVr
{
    public static class LabVisuals
    {
        // Holographic workshop palette: ice-cyan light for structure, amber for attention, red only for danger.
        // AWS service colours remain identifiers on their official icons, never the dominant surface.
        public static readonly Color Cyan = Hex("#5CE1F2"), Orange = Hex("#FFB24F"), Muted = Hex("#8FA9BC"),
            White = Hex("#E8F7FF"), PanelColor = Hex("#102637"), LineColor = Hex("#1F4B5E"), Green = Hex("#7CF0BD"),
            Ice = Hex("#C9F6FF"), Alert = Hex("#FF6474");
        public static readonly Color Glass = new Color(.018f, .06f, .088f, .9f); // ~10% linear ≈ 30% perceived see-through
        // Reading-heavy panels that float in front of others (code, keyboard, settings, assistant) need denser glass.
        public static readonly Color FocusGlass = new Color(.014f, .048f, .072f, 1);
        // They also sort after ordinary panels and holograms so content behind never bleeds through their text.
        public const int FocusSortingOrder = 20, ContextSortingOrder = 10;
        public static RectTransform Focus(RectTransform panel)
        {
            if (!panel) return panel;
            var glass = panel.GetComponent<HoloPanelGraphic>(); if (glass) glass.color = FocusGlass;
            var canvas = panel.GetComponent<Canvas>(); if (canvas) canvas.sortingOrder = FocusSortingOrder;
            return panel;
        }
        public static Color Rgba(string hex, float alpha) { var c = Hex(hex); c.a = alpha; return c; }
        static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        // Value-type keys: per-frame lookups (selection rings, traces, links) allocate nothing.
        static readonly Dictionary<(string, Color), Material> specialMaterials = new Dictionary<(string, Color), Material>();
        static readonly Dictionary<(Color, bool), Material> beams = new Dictionary<(Color, bool), Material>();
        static TMP_FontAsset font;
        public static Color Hex(string s) { ColorUtility.TryParseHtmlString(s, out var c); return c; }
        public static void Release()
        {
            foreach (var material in materials.Values) if (material) UnityEngine.Object.Destroy(material);
            materials.Clear();
            foreach (var material in specialMaterials.Values) if (material) UnityEngine.Object.Destroy(material);
            specialMaterials.Clear();
            foreach (var material in beams.Values) if (material) UnityEngine.Object.Destroy(material);
            beams.Clear();
            AwsIconGeometry.Release();
        }
        public static Material SpecialMaterial(string shader, Color color)
        {
            var key = (shader, color);
            if (specialMaterials.TryGetValue(key, out var existing) && existing) return existing;
            var material = new Material(Resources.Load<Shader>(shader)); material.color = color;
            specialMaterials[key] = material; return material;
        }
        // Additive beam for LineRenderers; static beams (observation edges, rings) have no travelling dashes.
        public static Material Beam(Color color, bool moving = true)
        {
            var key = (color, moving);
            if (beams.TryGetValue(key, out var existing) && existing) return existing;
            var material = new Material(Resources.Load<Shader>("LabFlow")); material.color = color;
            if (!moving) { material.SetFloat("_Speed", 0); material.SetFloat("_Dashes", 0); }
            beams[key] = material; return material;
        }
        public static Material Material(Color color)
        {
            if (materials.TryGetValue(color, out var existing) && existing) return existing;
            var mat = new Material(Resources.Load<Shader>("LabUnlit"));
            mat.color = color; materials[color] = mat; return mat;
        }
        public static GameObject Shape(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Color color, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = Material(color);
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            if (!collider) { var c = go.GetComponent<Collider>(); c.enabled = false; UnityEngine.Object.Destroy(c); }
            return go;
        }
        public static GameObject Metal(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Color color)
        {
            var go = Shape(parent, name, type, position, scale, color);
            go.GetComponent<Renderer>().sharedMaterial = SpecialMaterial("LabMetal", color);
            return go;
        }
        public static LineRenderer Line(Transform parent, string name, Vector3[] points, Color color, float width = .008f, bool loop = false)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false;
            line.positionCount = points.Length; line.SetPositions(points); line.loop = loop;
            line.widthMultiplier = width; line.sharedMaterial = Material(color); line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false; return line;
        }
        public static LineRenderer Ring(Transform parent, Vector3 center, float radius, Color color, float width = .009f, int count = 64)
        {
            var pts = new Vector3[count];
            for (int i = 0; i < count; i++) { float a = i * Mathf.PI * 2 / count; pts[i] = center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius; }
            return Line(parent, "Holographic ring", pts, color, width, true);
        }
        public static RectTransform Panel(Transform parent, string name, Vector3 pos, Vector2 size, float yaw = 0, bool background = true, bool movable = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas)); go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localRotation = Quaternion.Euler(0, yaw, 0); go.transform.localScale = Vector3.one * .002f;
            var rect = go.GetComponent<RectTransform>(); rect.sizeDelta = size;
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            if (background)
            {
                var img = go.AddComponent<HoloPanelGraphic>(); img.color = Glass; img.raycastTarget = false; img.Accent = Cyan;
                go.AddComponent<CanvasGroup>().interactable = false; go.AddComponent<HoloReveal>();
                EventBranding.Watermark(rect, size);
            }
            if (movable) go.AddComponent<LabMenu>().CreateHandle(size);
            return rect;
        }
        public static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)); var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false); rt.anchoredPosition = pos; rt.sizeDelta = size; return rt;
        }
        public static Image Block(Transform parent, Vector2 pos, Vector2 size, Color color)
        {
            var rt = Rect(parent, "Surface", pos, size); var img = rt.gameObject.AddComponent<Image>(); img.color = color; img.raycastTarget = false; return img;
        }
        public static TMP_Text Text(Transform parent, string value, Vector2 pos, Vector2 size, int fontSize = 24, Color? color = null, TextAnchor align = TextAnchor.MiddleLeft)
        {
            if (!font) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            var rt = Rect(parent, "Text", pos, size); var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.text = value; text.fontSize = fontSize; text.color = color ?? White;
            text.alignment = align == TextAnchor.MiddleCenter ? TextAlignmentOptions.Center : align == TextAnchor.MiddleRight ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false; text.richText = true; text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Truncate;
            Track(text, value, size);
            return text;
        }
        // Instrument-style labels: short uppercase strings get wide tracking when they still fit their box.
        public static void Track(TMP_Text text, string value, Vector2 size, float spacing = 7)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 60 || value.Contains("\n") || value.Contains("<")) return;
            bool letters = false;
            foreach (char c in value) { if (char.IsLower(c)) return; if (char.IsLetter(c)) letters = true; }
            if (!letters) return;
            text.characterSpacing = spacing;
            if (text.GetPreferredValues(value, float.PositiveInfinity, size.y).x > size.x - 6) text.characterSpacing = 0;
        }
        // Re-evaluate tracking after a label's text changes at runtime.
        public static void Retrack(TMP_Text text, float spacing = 7) { if (!text) return; text.characterSpacing = 0; Track(text, text.text, text.rectTransform.rect.size, spacing); }
        // Secondary actions: no slab, only a baseline until aimed at.
        public static LabTarget Ghost(LabTarget target) { if (target && target.Surface) { target.Surface.Ghost = true; target.SetAvailable(target.Available); } return target; }
        public static LabTarget Button(Transform parent, string label, Vector2 pos, Vector2 size, Action action, Color? accent = null)
        {
            var rt = Rect(parent, label, pos, size); var img = rt.gameObject.AddComponent<HoloPanelGraphic>();
            img.color = Rgba("#0D2B3B", .80f); img.raycastTarget = false; img.Detailed = false; img.Accent = accent ?? Cyan;
            var text = Text(rt, label, Vector2.zero, size - new Vector2(20, 4), 22, accent ?? White, TextAnchor.MiddleCenter);
            var collider = rt.gameObject.AddComponent<BoxCollider>(); collider.size = new Vector3(size.x, size.y, 12);
            var target = rt.gameObject.AddComponent<LabTarget>(); target.Action = action; target.Surface = img; target.Label = text; target.Accent = accent ?? White;
            return target;
        }
    }
}
