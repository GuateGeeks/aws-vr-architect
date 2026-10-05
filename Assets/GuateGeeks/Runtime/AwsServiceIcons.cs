using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GuateGeeks.AwsVr
{
    public static class AwsServiceIcons
    {
        static readonly Dictionary<ServiceKind, Texture2D> textures = new Dictionary<ServiceKind, Texture2D>();

        public static Texture2D Texture(ServiceKind kind)
        {
            if (!textures.TryGetValue(kind, out var texture) || !texture)
            {
                texture = Resources.Load<Texture2D>("AwsIcons/" + kind);
                if (texture) textures[kind] = texture;
            }
            return texture;
        }

        public static RawImage Add(Transform parent, ServiceKind kind, Vector2 position, float size)
        {
            var image = LabVisuals.Rect(parent, "AWS icon · " + kind, position, Vector2.one * size).gameObject.AddComponent<RawImage>();
            image.texture = Texture(kind);
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }
    }
}
