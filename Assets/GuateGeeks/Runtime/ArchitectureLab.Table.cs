using System;
using UnityEngine;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    // Table size: 1 m, 3 m or the full 3.9 m console, alone or in a shared room. The design keeps its full-table
    // coordinates; everything on the table lives in one scaled frame (the workspace) and the people move instead:
    // stations keep their gap to the rim and the personal console follows the station. In a real room the size is
    // part of the room state, chosen by the facilitator, so every headset draws the same table and the same circles.
    public sealed partial class ArchitectureLab
    {
        public TableLayout Table { get; private set; } = TableLayout.Full;
        public TableSize TableSize => Table.Size;
        public float TableScale => Table.Scale;
        // This headset's own choice. A real room overrides it while you are connected; leaving restores it.
        public TableSize LocalTableSize { get; private set; } = TableSize.Large;
        public Transform Workspace => workspace;
        Transform workspace;
        HoloEnvironment holoRoom;
        TMPro.TMP_Text tableSizeText;
        readonly LabTarget[] tableButtons = new LabTarget[3];
        string roomTableRequest;
        enum TableChange { Local, Room, Restore }

        void BuildTableWorkspace(HoloEnvironment room)
        {
            holoRoom = room;
            workspace = new GameObject("Table workspace").transform; workspace.SetParent(world, false);
            LocalTableSize = TableLayout.Parse(PlayerPrefs.GetInt(TableLayout.Preference, (int)TableSize.Large));
            Table = TableLayout.For(LocalTableSize);
            Table.ApplyTo(workspace);
            if (holoRoom) holoRoom.ApplyTable(Table);
        }
        // Design space (the full-table coordinates stored in the architecture) to the room and back.
        public Vector3 DesignPoint(Vector3 roomPoint) => workspace ? workspace.InverseTransformPoint(roomPoint) : roomPoint;
        public Vector3 RoomPoint(Vector3 designPoint) => workspace ? workspace.TransformPoint(designPoint) : designPoint;
        // A held or previewed object stays over the table, whatever its size.
        public Vector3 ClampToTable(Vector3 roomPoint) => RoomPoint(ClampWorkspace(DesignPoint(roomPoint)));
        // How far along a pointer to preview a placement or a pending connection: about the table centre.
        public float PointerReach(float fullTable) => fullTable * Table.Scale;

        public void SetTableSize(TableSize size)
        {
            if (roomConnecting) { SetStatus("Espera a que la sala termine de conectar."); return; }
            if (NetworkRoom != null) { RequestRoomTable(size); return; }
            LocalTableSize = TableLayout.Parse((int)size);
            PlayerPrefs.SetInt(TableLayout.Preference, (int)LocalTableSize); PlayerPrefs.Save();
            ApplyTable(LocalTableSize, TableChange.Local);
        }
        void ApplyTable(TableSize size, TableChange reason)
        {
            var layout = TableLayout.For(size);
            if (layout.Size == Table.Size) { RefreshTableSettings(); return; }
            if (Rig) Rig.ReleaseForConfiguration();
            Table = layout; Table.ApplyTo(workspace);
            if (holoRoom) holoRoom.ApplyTable(layout);
            foreach (var view in views.Values) if (view) view.ApplyTable(layout);
            ApplyVoiceMarkerTable();
            if (Space) Space.SetTable(layout);
            bool shared = Space && Space.SharedRoom;
            // Alone you are moved to the new stand. In a shared room people walk to their new circle instead, which
            // keeps the physical alignment between headsets (the table centre does not move).
            if (Rig && (!shared || !Rig.IsXR)) Rig.Recenter();
            RefreshTableSettings(); RefreshRoomSettings();
            if (reason == TableChange.Restore) return;
            string circle = shared ? " Camina a tu círculo " + (Space.Station + 1) + ", a " + layout.StationLabel + " del centro." : "";
            SetStatus(reason == TableChange.Room ? "La sala usa la mesa " + layout.Label + "." + circle
                : shared ? "Mesa " + layout.Label + " para la sala." + circle
                : layout.Size == TableSize.Large ? "Mesa " + layout.Label + ": consola completa frente a la mesa."
                : "Mesa " + layout.Label + ": te acercamos a la mesa con la consola compacta al alcance.");
            Feedback.Play(LabFeedback.Open);
        }
        void RequestRoomTable(TableSize size)
        {
            var room = NetworkRoom;
            if (!room.IsFacilitator) { SetStatus("Solo el facilitador cambia la mesa de la sala (ahora " + Table.Label + ")."); Feedback.Play(LabFeedback.Error); return; }
            if (!room.CanWrite) { SetStatus("Sala en lectura · reconecta antes de cambiar la mesa."); return; }
            if (TableLayout.Parse((int)size) == Table.Size) { RefreshTableSettings(); return; }
            roomTableRequest = Guid.NewGuid().ToString("N");
            if (room.Send(new RoomCommand { action = "table", requestId = roomTableRequest, tableSize = (int)size }))
                SetStatus("Cambiando la mesa de la sala a " + TableLayout.For(size).Label + "…");
            else roomTableRequest = null;
        }
        // Every snapshot carries the room's table. Older backends send none, which is the full table.
        void ReceiveRoomTable(RoomMessage message)
        {
            if (roomTableRequest != null && message.requestId == roomTableRequest)
            {
                roomTableRequest = null;
                if (!message.accepted) { SetStatus("La sala no cambió la mesa: " + message.message); Feedback.Play(LabFeedback.Error); }
            }
            ApplyTable(TableLayout.Parse(message.tableSize), TableChange.Room);
        }
        void RestoreLocalTable() { roomTableRequest = null; ApplyTable(LocalTableSize, TableChange.Restore); }

        void BuildTableSettings(Transform page)
        {
            tableSizeText = Text(page, "", new Vector2(0, -105), new Vector2(980, 35), 22, Cyan);
            for (int i = 0; i < TableLayout.All.Length; i++)
            {
                var size = TableLayout.All[i];
                tableButtons[i] = Button(page, TableLayout.For(size).Label, new Vector2((i - 1) * 330, -160), new Vector2(316, 58), () => SetTableSize(size));
            }
            Text(page, "Fondo y mesa se guardan en este visor. En una sala real, el facilitador elige la mesa para todos.", new Vector2(0, -222), new Vector2(980, 56), 20, Muted);
            RefreshTableSettings();
        }
        void RefreshTableSettings()
        {
            var room = NetworkRoom;
            if (tableSizeText) tableSizeText.text = "TAMAÑO DE MESA · " + Table.Name.ToUpperInvariant() + " · Ø " + Table.DiameterLabel
                + (room != null ? " · DE LA SALA" : Table.Size == TableSize.Large ? "" : " · ESTACIÓN A " + Table.StationLabel);
            bool canChange = !roomConnecting && (room == null || room.IsFacilitator);
            for (int i = 0; i < tableButtons.Length; i++)
            {
                if (!tableButtons[i]) continue;
                var size = TableLayout.All[i];
                tableButtons[i].Label.text = TableLayout.For(size).Label + (size == Table.Size ? "  ●" : "");
                tableButtons[i].SetAvailable(canChange);
            }
        }
    }
}
