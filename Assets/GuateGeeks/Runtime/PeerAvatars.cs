using System.Collections.Generic;
using UnityEngine;
using static GuateGeeks.AwsVr.LabVisuals;

namespace GuateGeeks.AwsVr
{
    // Teammates as light: a graphite visor wearing the GuateGeeks eyes (they look where the person looks),
    // a halo and a faint body in the station colour, a name tag that turns to you, and a pointer beam when
    // they point. Built from the active ICollabSession every frame; nothing here knows about networking.
    public sealed class PeerAvatars : MonoBehaviour
    {
        sealed class Avatar
        {
            public Transform root, head, body, hand, dot, gaze;
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
            a.head = new GameObject("Teammate head").transform; a.head.SetParent(a.root, false);
            Metal(a.head, "Visor shell", PrimitiveType.Sphere, Vector3.zero, new Vector3(.19f, .22f, .21f), Hex("#22303B"));
            Shape(a.head, "Visor glass", PrimitiveType.Sphere, new Vector3(0, .012f, .07f), new Vector3(.175f, .1f, .09f), Hex("#08131C"));
            var eyes = GeekEyes.Create(a.head, .13f, "Teammate eyes");
            eyes.transform.localPosition = new Vector3(0, .014f, .118f); eyes.transform.localRotation = Quaternion.Euler(0, 180, 0);
            a.gaze = new GameObject("Teammate gaze").transform; a.gaze.SetParent(a.root, false); eyes.Target = a.gaze;
            var halo = Ring(a.head, new Vector3(0, .07f, 0), .135f, color, .01f, 48); halo.sharedMaterial = Beam(color, false); halo.textureMode = LineTextureMode.Stretch;
            var glow = color; glow.a = .2f;
            a.body = Shape(a.root, "Teammate presence", PrimitiveType.Capsule, Vector3.zero, new Vector3(.36f, .34f, .22f), color).transform;
            a.body.GetComponent<Renderer>().sharedMaterial = SpecialMaterial("LabHologram", glow);
            a.hand = Shape(a.root, "Teammate hand", PrimitiveType.Sphere, Vector3.zero, Vector3.one * .05f, color).transform;
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
            a.head.SetPositionAndRotation(peer.Head, peer.HeadRotation);
            var yaw = Quaternion.Euler(0, peer.HeadRotation.eulerAngles.y, 0);
            a.body.SetPositionAndRotation(peer.Head + Vector3.down * .52f + yaw * Vector3.back * .04f, yaw);
            a.gaze.position = peer.PointAt;
            a.hand.position = peer.Hand;
            a.pointer.enabled = peer.Pointing; a.dot.gameObject.SetActive(peer.Pointing);
            if (peer.Pointing)
            {
                var direction = peer.PointAt - peer.Hand; float length = direction.magnitude;
                var end = peer.Hand + direction * Mathf.Max(0, (length - .16f) / Mathf.Max(length, .01f));
                pointerPoints[0] = a.root.InverseTransformPoint(peer.Hand); pointerPoints[1] = a.root.InverseTransformPoint(end);
                a.pointer.SetPositions(pointerPoints); a.dot.position = end;
            }
            a.tag.position = peer.Head + Vector3.up * .3f;
            if (cam) { var d = a.tag.position - cam.transform.position; d.y = 0; if (d.sqrMagnitude > .01f) a.tag.rotation = Quaternion.LookRotation(d); }
            if (!ReferenceEquals(a.shownActivity, peer.Activity)) { a.shownActivity = peer.Activity; a.activity.text = peer.Activity; Retrack(a.activity); }
        }
    }
}
