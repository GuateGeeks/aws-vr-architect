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
        public static readonly Color Cyan = Hex("#54DCEC"), Orange = Hex("#FFAD4F"), Muted = Hex("#91A9BC"),
            White = Hex("#E6F6FF"), PanelColor = Hex("#102637"), LineColor = Hex("#214456"), Green = Hex("#7CF0BD");
        static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        static readonly Dictionary<string, Material> specialMaterials = new Dictionary<string, Material>();
        static TMP_FontAsset font;
        public static Color Hex(string s) { ColorUtility.TryParseHtmlString(s, out var c); return c; }
        public static void Release()
        {
            foreach (var material in materials.Values) if (material) UnityEngine.Object.Destroy(material);
            materials.Clear();
            foreach (var material in specialMaterials.Values) if (material) UnityEngine.Object.Destroy(material);
            specialMaterials.Clear();
        }
        public static Material SpecialMaterial(string shader, Color color)
        {
            string key = shader + ColorUtility.ToHtmlStringRGBA(color);
            if (specialMaterials.TryGetValue(key, out var existing) && existing) return existing;
            var material = new Material(Resources.Load<Shader>(shader)); material.color = color;
            specialMaterials[key] = material; return material;
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
            if (background) { var img = go.AddComponent<HoloPanelGraphic>(); img.color = new Color(.025f, .055f, .082f, .98f); img.raycastTarget = false; }
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
            return text;
        }
        public static LabTarget Button(Transform parent, string label, Vector2 pos, Vector2 size, Action action, Color? accent = null)
        {
            var rt = Rect(parent, label, pos, size); var img = rt.gameObject.AddComponent<HoloPanelGraphic>();
            img.color = Hex("#102F40"); img.raycastTarget = false; img.Detailed = false; img.Accent = accent ?? Cyan;
            var text = Text(rt, label, Vector2.zero, size - new Vector2(20, 4), 22, accent ?? White, TextAnchor.MiddleCenter);
            var collider = rt.gameObject.AddComponent<BoxCollider>(); collider.size = new Vector3(size.x, size.y, 12);
            var target = rt.gameObject.AddComponent<LabTarget>(); target.Action = action; target.Surface = img; target.Label = text; target.Accent = accent ?? White;
            return target;
        }
    }
}
