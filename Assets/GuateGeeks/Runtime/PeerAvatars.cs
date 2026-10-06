using System.Collections.Generic;
using UnityEngine;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    // Teammates as holographic androids in their station colour (TeammateAndroid): a face screen wearing the
    // GuateGeeks eyes (they look where the person looks), an upper body hovering on a light emitter and arms
    // that follow the tracked hand. Plus a name tag that turns to you and a pointer beam from the fingertip
    // when they point. Built from the active ICollabSession every frame; nothing here knows about networking.
    public sealed class PeerAvatars : MonoBehaviour
    {
        sealed class Avatar
        {
            public TeammateAndroid android;
            public Transform root, dot, gaze;
            public LineRenderer pointer;
            public RectTransform tag;
            public TMPro.TMP_Text activity;
            public string shownActivity;
            public bool seen;
        }
        readonly Dictionary<string, Avatar> avatars = new Dictionary<string, Avatar>();
        readonly List<string> stale = new List<string>();
        readonly Vector3[] pointerPoints = new Vector3[2];
        ArchitectureLab lab;
        public int Count => avatars.Count;
        public int PointingCount { get; private set; }
        public bool TryGetAndroid(string peerId, out TeammateAndroid android)
        {
            android = peerId != null && avatars.TryGetValue(peerId, out var avatar) ? avatar.android : null; return android != null;
        }

        public static PeerAvatars Create(Transform world, ArchitectureLab owner)
        {
            var root = new GameObject("Teammates · presence").transform; root.SetParent(world, false);
            var avatars = root.gameObject.AddComponent<PeerAvatars>(); avatars.lab = owner; return avatars;
        }
        void LateUpdate()
        {
            var session = lab ? lab.Collab : null;
            var peers = session != null && SharedSpace.SharedRoomActive ? session.Peers : null;
            foreach (var avatar in avatars.Values) avatar.seen = false;
            var cam = lab && lab.Rig ? lab.Rig.ViewCamera : Camera.main;
            int pointing = 0;
            if (peers != null)
                for (int i = 0; i < peers.Count; i++)
                {
                    var peer = peers[i];
                    if (!avatars.TryGetValue(peer.Id, out var avatar)) avatars[peer.Id] = avatar = Build(peer);
                    avatar.seen = true; Pose(avatar, peer, cam);
                    if (peer.Pointing) pointing++;
                }
            PointingCount = pointing;
            stale.Clear();
            foreach (var pair in avatars) if (!pair.Value.seen) stale.Add(pair.Key);
            foreach (var id in stale) { var avatar = avatars[id]; avatars.Remove(id); if (avatar.root) Destroy(avatar.root.gameObject); }
        }
        Avatar Build(PeerState peer)
        {
            var color = peer.Color; var a = new Avatar();
            a.root = new GameObject("Teammate " + peer.Label).transform; a.root.SetParent(transform, false);
            a.android = TeammateAndroid.Build(a.root, color, peer.Station * 1.7f);
            a.gaze = new GameObject("Teammate gaze").transform; a.gaze.SetParent(a.root, false); a.android.Eyes.Target = a.gaze;
            a.pointer = Line(a.root, "Teammate pointer", pointerPoints, color, .012f);
            a.pointer.sharedMaterial = Beam(color); a.pointer.textureMode = LineTextureMode.Stretch; a.pointer.numCapVertices = 0;
            a.dot = Shape(a.root, "Teammate pointer target", PrimitiveType.Sphere, Vector3.zero, Vector3.one * .04f, color).transform;
            a.tag = Panel(a.root, "Teammate name tag", Vector3.zero, new Vector2(420, 100), background: false, movable: false);
            a.tag.localScale = Vector3.one * .0018f; // readable across the table (~5 m)
            Text(a.tag, peer.Label, new Vector2(0, 18), new Vector2(420, 54), 36, color, TextAnchor.MiddleCenter);
            a.activity = Text(a.tag, "", new Vector2(0, -28), new Vector2(420, 32), 17, Muted, TextAnchor.MiddleCenter);
            return a;
        }
        void Pose(Avatar a, PeerState peer, Camera cam)
        {
            a.android.Pose(peer, Time.unscaledDeltaTime, Time.unscaledTime);
            a.gaze.position = peer.PointAt;
            a.pointer.enabled = peer.Pointing; a.dot.gameObject.SetActive(peer.Pointing);
            if (peer.Pointing)
            {
                var origin = a.android.PointerOrigin;
                var direction = peer.PointAt - origin; float length = direction.magnitude;
                var end = origin + direction * Mathf.Max(0, (length - .16f) / Mathf.Max(length, .01f));
                pointerPoints[0] = a.root.InverseTransformPoint(origin); pointerPoints[1] = a.root.InverseTransformPoint(end);
                a.pointer.SetPositions(pointerPoints); a.dot.position = end;
            }
            a.tag.position = peer.Head + Vector3.up * .32f; // clears the crest
            if (cam) { var d = a.tag.position - cam.transform.position; d.y = 0; if (d.sqrMagnitude > .01f) a.tag.rotation = Quaternion.LookRotation(d); }
            if (!ReferenceEquals(a.shownActivity, peer.Activity)) { a.shownActivity = peer.Activity; a.activity.text = peer.Activity; Retrack(a.activity); }
        }
    }
}
