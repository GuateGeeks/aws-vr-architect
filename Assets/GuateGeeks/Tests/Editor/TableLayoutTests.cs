using NUnit.Framework;
using UnityEngine;

namespace GuateGeeks.AwsVr.Tests
{
    public sealed class TableLayoutTests
    {
        [Test]
        public void ThreeDiametersShareOneDesignSpace()
        {
            TableLayout small = TableLayout.For(TableSize.Small), medium = TableLayout.For(TableSize.Medium), full = TableLayout.For(TableSize.Large);
            Assert.AreEqual(1f, small.Diameter); Assert.AreEqual(3f, medium.Diameter); Assert.AreEqual(3.9f, full.Diameter);
            // The full table is exactly the table every earlier version drew.
            Assert.AreEqual(1f, full.Scale); Assert.AreEqual(SharedSpace.SharedDistance, full.StationDistance);
            Assert.AreEqual(1f, full.Ui, 1e-5f); Assert.AreEqual(1f, full.LabelScale, 1e-5f); Assert.AreEqual(1f, full.Stroke, 1e-5f); Assert.AreEqual(1f, full.ConsoleScale);
            Assert.AreEqual(TableSize.Large, TableLayout.Parse(0), "An older backend sends no size: the full table");
            Assert.AreEqual(TableSize.Large, TableLayout.Parse(9)); Assert.AreEqual(TableSize.Small, TableLayout.Parse(1)); Assert.AreEqual(TableSize.Medium, TableLayout.Parse(2));
            foreach (var size in TableLayout.All)
            {
                var t = TableLayout.For(size);
                Assert.Less(Vector3.Distance(TableLayout.Pivot, t.ToRoom(TableLayout.Pivot)), 1e-5f, "The centre of the glass never moves");
                var p = new Vector3(1.2f, 1.7f, 3.4f); Assert.Less(Vector3.Distance(p, t.ToDesign(t.ToRoom(p))), 1e-4f);
                var frame = new GameObject("frame").transform; t.ApplyTo(frame);
                Assert.Less(Vector3.Distance(t.ToRoom(p), frame.TransformPoint(p)), 1e-4f, "The scaled frame matches the mapping");
                Object.DestroyImmediate(frame.gameObject);
                // Every position the design (and the room backend) allows lands over this table, above the glass.
                foreach (var corner in new[] { new Vector3(-1.6f, 1.02f, 1.65f), new Vector3(1.6f, 2.1f, 3.65f), new Vector3(1.6f, 1.5f, 1.65f) })
                {
                    var room = t.ToRoom(corner);
                    Assert.LessOrEqual(new Vector2(room.x - TableLayout.Pivot.x, room.z - TableLayout.Pivot.z).magnitude, t.Radius, size + " " + corner);
                    Assert.GreaterOrEqual(room.y, TableLayout.Surface, "Holograms float above the glass");
                }
                // Stations keep the same gap to the rim; neighbouring safety circles never overlap.
                Assert.AreEqual(t.Radius + TableLayout.RimGap, t.StationDistance, .03f, size.ToString());
                Assert.Greater(t.StationDistance * Mathf.Sqrt(2), 2 * SharedSpace.ZoneRadius, size + ": neighbouring circles overlap");
                Assert.GreaterOrEqual(t.LabelScale, 1f, "Labels never shrink faster than the table");
                Assert.LessOrEqual(t.Stroke, 1f); Assert.GreaterOrEqual(t.Stroke, t.Scale);
            }
            Assert.Less(2 * (small.StationDistance + SharedSpace.ZoneRadius), 4f, "Four people fit around the small table in a 4 m booth");
            Assert.Less(2 * (medium.StationDistance + SharedSpace.ZoneRadius), 6f);
            Assert.AreEqual("Pequeña · 1 m", small.Label); Assert.AreEqual("Mediana · 3 m", medium.Label); Assert.AreEqual("Grande · 3.9 m", full.Label);
        }
        [Test]
        public void RoomTableTravelsInSnapshotsAndCommands()
        {
            Assert.AreEqual(TableSize.Small, TableLayout.Parse(JsonUtility.FromJson<RoomMessage>("{\"type\":\"snapshot\",\"tableSize\":1}").tableSize));
            Assert.AreEqual(TableSize.Large, TableLayout.Parse(JsonUtility.FromJson<RoomMessage>("{\"type\":\"snapshot\"}").tableSize), "Snapshot from an older backend");
            StringAssert.Contains("\"tableSize\":2", JsonUtility.ToJson(new RoomCommand { action = "table", requestId = "t1", tableSize = 2 }));
        }
    }
}
