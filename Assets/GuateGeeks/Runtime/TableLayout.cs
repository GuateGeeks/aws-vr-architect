using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // Wire and preference values are stable: they travel in room snapshots and live in PlayerPrefs.
    // 0 means "not sent" (an older backend or app), which is the full table every older headset uses.
    public enum TableSize { Small = 1, Medium = 2, Large = 3 }

    // One projection table in three diameters: 1 m, 3 m and the full 3.9 m console. The architecture keeps its
    // design-space coordinates (x ±1.6, y 1.02–2.1, z 1.65–3.65 over the full table), so saved designs, undo, the
    // room protocol and the backend bounds never change: the table and its holograms are drawn through one uniform
    // scale about the centre of the glass, which stays at 0.74 m. Stations keep the same 0.67 m gap to the rim, so a
    // smaller table also shrinks the booth (about 3.8 m across for the small table instead of 6.7 m).
    public readonly struct TableLayout
    {
        public const string Preference = "GuateGeeks.Table.Size.v1";
        public const float FullDiameter = 3.9f, Surface = .74f, RimGap = .67f;
        public static readonly Vector3 Pivot = new Vector3(0, Surface, 2.65f);
        public static readonly TableSize[] All = { TableSize.Small, TableSize.Medium, TableSize.Large };

        public readonly TableSize Size;
        public readonly float Diameter;
        // Shared stations, and the solo stand when the console is compact, sit this far from the table centre.
        public readonly float StationDistance;
        // The compact personal console scales about the eye: the same visual angle, closer to you, so on a small
        // table it stays out of the neighbours' space.
        public readonly float ConsoleScale;

        TableLayout(TableSize size, float diameter, float stationDistance, float consoleScale)
        { Size = size; Diameter = diameter; StationDistance = stationDistance; ConsoleScale = consoleScale; }

        public static TableLayout For(TableSize size)
        {
            switch (size)
            {
                case TableSize.Small: return new TableLayout(TableSize.Small, 1, .5f + RimGap, .62f);
                case TableSize.Medium: return new TableLayout(TableSize.Medium, 3, 1.5f + RimGap, 1);
                default: return new TableLayout(TableSize.Large, FullDiameter, SharedSpace.SharedDistance, 1);
            }
        }
        public static TableSize Parse(int value) => value >= 1 && value <= 3 ? (TableSize)value : TableSize.Large;
        public static TableLayout Full => For(TableSize.Large);

        public float Scale => Diameter / FullDiameter;
        public float Radius => Diameter * .5f;
        // LineRenderer widths ignore transform scale: lines get thinner with the table, but never hair-thin.
        public float Stroke => Mathf.Sqrt(Scale);
        // Labels and controls next to the holograms keep their visual angle from the station: their physical size
        // follows the distance from the eye to the holograms rather than the table.
        public float Ui => EyeDistance / Full.EyeDistance;
        // From a standing eye at the station to the default hologram height over the table centre.
        float EyeDistance => new Vector2(StationDistance, SharedSpace.EyeHeight - (Surface + (1.5f - Surface) * Scale)).magnitude;
        // The same, relative to the scaled holograms they are attached to.
        public float LabelScale => Ui / Scale;
        public string Name => Size == TableSize.Small ? "Pequeña" : Size == TableSize.Medium ? "Mediana" : "Grande";
        public string DiameterLabel => Size == TableSize.Small ? "1 m" : Size == TableSize.Medium ? "3 m" : "3.9 m";
        public string Label => Name + " · " + DiameterLabel;
        public string StationLabel => StationDistance.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) + " m";

        public Vector3 ToRoom(Vector3 design) => Pivot + (design - Pivot) * Scale;
        public Vector3 ToDesign(Vector3 room) => Pivot + (room - Pivot) / Scale;
        // A frame whose children are authored for the full table (design space).
        public void ApplyTo(Transform frame)
        {
            frame.localRotation = Quaternion.identity;
            frame.localScale = Vector3.one * Scale;
            frame.localPosition = Pivot * (1 - Scale);
        }
    }
}
