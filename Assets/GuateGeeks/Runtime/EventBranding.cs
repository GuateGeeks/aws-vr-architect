using UnityEngine;
using UnityEngine.UI;

namespace GuateGeeks.AwsVr
{
    // The title window shows the supplied AWS Community Day Guatemala logo (Resources/Branding) instead of text.
    // A generated radial backlight in the logo's teal and thin registration brackets project it like the other holograms.
    public static class EventBranding
    {
        public const string LogoResource = "Branding/AwsCommunityDayGuatemala";
        public static readonly Color Teal = LabVisuals.Hex("#0A6B6B"), Quetzal = LabVisuals.Hex("#0E8A8C"), Leaf = LabVisuals.Hex("#5DB54B");
        static Texture2D logo, glow, guateGeeks, guateGeeksFull, eyesMark;
        public static Texture2D Logo { get { if (!logo) logo = Resources.Load<Texture2D>(LogoResource); return logo; } }
        // GuateGeeks identity (Branding/Asset 41.svg) and its eyes mark (Branding/eyes.svg), rasterised with alpha bleed.
        public static Texture2D GuateGeeks { get { if (!guateGeeks) guateGeeks = Resources.Load<Texture2D>("Branding/GuateGeeks"); return guateGeeks; } }
        public static Texture2D GuateGeeksFull { get { if (!guateGeeksFull) guateGeeksFull = Resources.Load<Texture2D>("Branding/GuateGeeksFull"); return guateGeeksFull; } }
        public static Texture2D EyesMark { get { if (!eyesMark) eyesMark = Resources.Load<Texture2D>("Branding/GeekEyes"); return eyesMark; } }
        public const string WatermarkName = "GuateGeeks eyes mark";
        // Small eyes mark in a panel's top-right corner, drawn behind its content.
        public static void Watermark(RectTransform panel, Vector2 size)
        {
            if (size.y < 200 || !EyesMark) return;
            var mark = LabVisuals.Rect(panel, WatermarkName, new Vector2(size.x / 2 - 70, size.y / 2 - 28), new Vector2(70, 39)).gameObject.AddComponent<RawImage>();
            mark.texture = EyesMark; mark.color = new Color(1, 1, 1, .42f); mark.raycastTarget = false; mark.transform.SetAsFirstSibling();
        }
        // A floating GuateGeeks sign whose eyes are live 3D GeekEyes that follow the viewer.
        public static RectTransform GuateGeeksSign(Transform world, string name, Vector3 position, float widthMeters, Vector3 facing)
        {
            var size = new Vector2(1024, 669);
            var sign = LabVisuals.Panel(world, name, position, size, background: false, movable: false);
            sign.localScale = Vector3.one * (widthMeters / size.x); sign.localRotation = Quaternion.LookRotation(facing, Vector3.up);
            var back = LabVisuals.Rect(sign, "Sign backlight", new Vector2(0, 0), size * 1.15f).gameObject.AddComponent<RawImage>();
            back.texture = Glow(); back.color = new Color(.05f, .45f, .62f, .32f); back.raycastTarget = false;
            var frame = LabVisuals.Cyan; frame.a = .5f;
            float hx = size.x / 2 + 24, hy = size.y / 2 + 24;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2)
            {
                LabVisuals.Block(sign, new Vector2(x * (hx - 40), y * hy), new Vector2(80, 4), frame);
                LabVisuals.Block(sign, new Vector2(x * hx, y * (hy - 40)), new Vector2(4, 80), frame);
            }
            var image = LabVisuals.Rect(sign, "GuateGeeks logo", Vector2.zero, size).gameObject.AddComponent<RawImage>();
            image.texture = GuateGeeks; image.raycastTarget = false;
            // Eyes position from Asset 41.svg: eyes.svg spans x 43.8–112.65, y 33.35–71.3 of the 145.75 × 95.21 logo.
            float k = size.x / 145.75f;
            var eyes = GeekEyes.Create(sign, 68.85f * k);
            eyes.transform.localPosition = new Vector3((78.225f - 72.875f) * k, -(52.325f - 47.605f) * k, -4);
            return sign;
        }

        public static RawImage Build(RectTransform parent, Vector2 size)
        {
            var back = LabVisuals.Rect(parent, "Logo backlight", new Vector2(0, -20), new Vector2(size.x * 1.1f, size.y * 1.05f)).gameObject.AddComponent<RawImage>();
            back.texture = Glow(); back.color = new Color(Quetzal.r, Quetzal.g, Quetzal.b, .42f); back.raycastTarget = false;
            var frame = LabVisuals.Cyan; frame.a = .55f;
            float hx = size.x / 2 - 30, hy = size.y / 2 - 40;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2)
            {
                LabVisuals.Block(parent, new Vector2(x * (hx - 40), y * hy), new Vector2(80, 4), frame);
                LabVisuals.Block(parent, new Vector2(x * hx, y * (hy - 40)), new Vector2(4, 80), frame);
            }
            var image = LabVisuals.Rect(parent, "AWS Community Day Guatemala logo", Vector2.zero, size).gameObject.AddComponent<RawImage>();
            image.texture = Logo; image.raycastTarget = false; image.color = Color.white;
            if (!image.texture) LabVisuals.Text(parent, "AWS COMMUNITY DAY · GUATEMALA", Vector2.zero, new Vector2(size.x, 120), 64, LabVisuals.White, TextAnchor.MiddleCenter);
            return image;
        }
        // Soft radial falloff generated once (64 px, no asset): bright core, smooth edge, transparent corners.
        static Texture2D Glow()
        {
            if (glow) return glow;
            const int n = 64; glow = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Logo backlight" };
            var pixels = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dx = (x + .5f) / n * 2 - 1, dy = (y + .5f) / n * 2 - 1, r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Pow(Mathf.Clamp01(1 - r), 2.2f);
                pixels[y * n + x] = new Color(1, 1, 1, a);
            }
            glow.SetPixels(pixels); glow.Apply(false, true); return glow;
        }
    }
}
