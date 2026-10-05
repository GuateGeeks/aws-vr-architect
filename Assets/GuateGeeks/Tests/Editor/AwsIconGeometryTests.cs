using NUnit.Framework;
using UnityEngine;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class AwsIconGeometryTests
    {
        static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
        [Test] public void EverySymbolIsCompletelyTriangulatedFromItsOfficialOutline()
        {
            foreach (var definition in ServiceCatalog.All)
            {
                var shape = AwsIconGeometry.Get(definition.Kind);
                Assert.IsNotNull(shape, definition.Name);
                Assert.AreEqual(0, shape.Triangles.Length % 3);
                float triangles = 0, outline = 0;
                for (int i = 0; i < shape.Triangles.Length; i += 3)
                {
                    float signed = Cross(shape.Point(shape.Triangles[i]), shape.Point(shape.Triangles[i + 1]), shape.Point(shape.Triangles[i + 2])) / 2;
                    Assert.LessOrEqual(signed, 1e-6f, "Clockwise as seen from the front (slivers excepted): " + definition.Name);
                    triangles -= signed;
                }
                for (int l = 0; l < shape.Loops.Length; l += 3)
                {
                    int first = shape.Loops[l], count = shape.Loops[l + 1], dir = shape.Loops[l + 2]; float loop = 0;
                    for (int i = 0; i < count; i++) { var a = shape.Point(first + i); var b = shape.Point(first + (i + 1) % count); loop += (a.x * b.y - b.x * a.y) / 2; }
                    outline += loop * dir; // outer loops count positive, holes negative
                }
                Assert.Greater(outline, .05f, definition.Name);
                Assert.AreEqual(outline, triangles, outline * .001f, "Triangulation covers exactly the filled symbol: " + definition.Name);
                foreach (var v in shape.Points) Assert.That(v, Is.InRange(-.5f, .5f));
            }
        }
        [Test] public void EmblemMeshesAreSolidAndUseTheOfficialCategoryColour()
        {
            foreach (var definition in ServiceCatalog.All)
            {
                var mesh = AwsIconGeometry.Mesh(definition.Kind);
                Assert.IsNotNull(mesh);
                Assert.AreEqual(AwsIconGeometry.Size, mesh.bounds.size.x, .001f);
                Assert.AreEqual(AwsIconGeometry.Depth + 2 * AwsIconGeometry.Relief, mesh.bounds.size.z, .001f, "Symbol in relief on both faces");
                Assert.AreNotEqual(Color.gray, AwsIconGeometry.Background(definition.Kind));
            }
            Assert.AreEqual("8C4FFF", ColorUtility.ToHtmlStringRGB(AwsIconGeometry.Background(ServiceKind.ApiGateway)));
            Assert.AreEqual("ED7100", ColorUtility.ToHtmlStringRGB(AwsIconGeometry.Background(ServiceKind.Lambda)));
            Assert.AreEqual("7AA116", ColorUtility.ToHtmlStringRGB(AwsIconGeometry.Background(ServiceKind.S3)));
        }
    }
}
