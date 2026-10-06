using UnityEngine;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    // The GuateGeeks wordmark inlaid in the projection table as emitted light (signed-distance hologram, LabLogo shader),
    // the live 3D GuateGeeks eyes lying in the glass and looking up at whoever leans over the table, and an inscription
    // engraved around the rim that reads correctly from every station of the shared room.
    public sealed class TableEmblem : MonoBehaviour
    {
        public const float LogoWidth = 2.25f;                               // metres across the letters (Asset 41.svg: 145.75 × 95.21)
        const float TextureWidth = 2048, LogoTexels = 1856, TextureHeight = 1408; // GuateGeeksSDF.png layout (letters + glow margin)
        public const string LogoName = "GuateGeeks table logo", EyesName = "GuateGeeks table eyes", InscriptionName = "Table rim inscription";
        const string Inscription = "AWS COMMUNITY DAY · GUATEMALA · GUATEGEEKS ARCHITECTURE LAB · LAT 14.63 N · LON 90.51 W · ";
        Material material;
        public GeekEyes Eyes { get; private set; }

        // centre: the table axis on the glass.
        public static TableEmblem Build(Transform world, Vector3 centre)
        {
            var root = new GameObject("GuateGeeks table emblem").transform; root.SetParent(world, false); root.localPosition = centre;
            var emblem = root.gameObject.AddComponent<TableEmblem>();
            float quadWidth = LogoWidth * TextureWidth / LogoTexels, quadHeight = quadWidth * TextureHeight / TextureWidth;
            var logo = Shape(root, LogoName, PrimitiveType.Quad, Vector3.zero, new Vector3(quadWidth, quadHeight, 1), Cyan);
            logo.transform.localRotation = Quaternion.Euler(90, 0, 0);       // faces up; reads upright from the main station
            emblem.material = new Material(Resources.Load<Shader>("LabLogo")) { name = LogoName };
            emblem.material.SetTexture("_MainTex", EventBranding.GuateGeeksSdf);
            logo.GetComponent<Renderer>().sharedMaterial = emblem.material;
            // Eyes from Asset 41.svg: eyes.svg is centred at (78.225, 52.325), the logo at (72.875, 47.605); svg y runs toward the viewer.
            float k = LogoWidth / 145.75f;
            emblem.Eyes = GeekEyes.Create(root, 68.85f * k, EyesName);
            emblem.Eyes.transform.localRotation = Quaternion.Euler(90, 0, 0);
            emblem.Eyes.transform.localPosition = new Vector3((78.225f - 72.875f) * k, .004f, -(52.325f - 47.605f) * k);
            Inscribe(root);
            return emblem;
        }
        // One glyph per character on the band between the 1.70 m and 1.81 m rings, upright toward the table centre.
        static void Inscribe(Transform root)
        {
            var go = new GameObject(InscriptionName, typeof(RectTransform), typeof(Canvas));
            var canvas = go.GetComponent<RectTransform>(); canvas.SetParent(root, false);
            canvas.localPosition = new Vector3(0, .008f, 0); canvas.localRotation = Quaternion.Euler(90, 0, 0); canvas.localScale = Vector3.one * .001f;
            canvas.sizeDelta = new Vector2(3800, 3800); go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var etch = Color.Lerp(EventBranding.Quetzal, Cyan, .45f); etch.a = .85f;
            string text = Inscription + Inscription; float step = 360f / text.Length; const float radius = 1755;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == ' ') continue;
                float theta = 180 - i * step, a = theta * Mathf.Deg2Rad;   // starts in front of station 1 and reads to the right
                var glyph = Text(canvas, text[i].ToString(), new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * radius, new Vector2(70, 70), 50, etch, TextAnchor.MiddleCenter);
                glyph.rectTransform.localRotation = Quaternion.Euler(0, 0, 180 - theta);
                glyph.characterSpacing = 0;
            }
        }
        void OnDestroy() { if (material) Destroy(material); }
    }
}
