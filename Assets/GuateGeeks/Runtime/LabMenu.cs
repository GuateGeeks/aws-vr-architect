using System;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // A separate handle leaves trigger clicks on the menu's buttons unchanged.
    public sealed class LabMenu : MonoBehaviour
    {
        [Serializable] sealed class Pose { public Vector3 position; public Quaternion rotation; }
        public const string PreferencePrefix = "GuateGeeks.Menu.v1.";
        public Transform Handle { get; private set; }
        public Action Moved;
        // Called by «Restaurar paneles» after the home pose is restored (follow panels use it to unpin).
        public Action Restored;
        // Transient surfaces (keyboards, head-following readers) are placed relative to the person each time,
        // so a pose dragged by hand is not remembered between sessions.
        public bool Persist = true;
        public bool Grabbed => owner != null;
        object owner;
        Vector3 defaultPosition, grabOffset;
        Vector3 nearOffset;
        readonly RectTransform[] gripEdges = new RectTransform[4];
        Quaternion defaultRotation, grabRotation, initialAim;
        float distance;
        bool initialized;
        // Saved poses are per layout: the solo console and the compact shared-room console keep separate poses.
        string layout = "";
        string Key => PreferencePrefix + layout + gameObject.name;

        public void CreateHandle(Vector2 size)
        {
            // A quiet grip bar instead of a repeated label: it names itself only while aimed at.
            var target = LabVisuals.Button(transform, "•  •  •  •  •", new Vector2(0, size.y / 2 + 21),
                new Vector2(Mathf.Min(size.x, 220), 30), null, LabVisuals.Muted);
            target.Label.fontSize = 16; target.Label.characterSpacing = 0;
            target.HoverLabel = "MOVER PANEL";
            LabVisuals.Ghost(target);
            target.Menu = this; Handle = target.transform;
            for (int i = 0; i < gripEdges.Length; i++)
            {
                bool side = i < 2;
                var edge = LabVisuals.Button(transform, "", side ? new Vector2((i == 0 ? -1 : 1) * (size.x / 2 + 8), 0)
                    : new Vector2(0, (i == 2 ? -1 : 1) * (size.y / 2 + 8)),
                    side ? new Vector2(18, size.y) : new Vector2(size.x, 18), null, LabVisuals.Muted);
                edge.Menu = this; edge.HoverLabel = "MOVER PANEL"; LabVisuals.Ghost(edge);
                gripEdges[i] = edge.transform as RectTransform;
            }
        }
        public bool IsGrip(Transform child) => child && (Handle && child.IsChildOf(Handle) ||
            Array.Exists(gripEdges, edge => edge && child.IsChildOf(edge)));
        public void RemoveGripControls()
        {
            if (Handle) { Handle.gameObject.SetActive(false); Destroy(Handle.gameObject); Handle = null; }
            for (int i = 0; i < gripEdges.Length; i++) if (gripEdges[i])
            { gripEdges[i].gameObject.SetActive(false); Destroy(gripEdges[i].gameObject); gripEdges[i] = null; }
        }
        public bool TryGrabNear(object grabber, Vector3 gripPosition, Quaternion handRotation)
        {
            if (grabber == null || Grabbed || !isActiveAndEnabled) return false;
            owner = grabber; initialAim = handRotation; grabRotation = transform.rotation;
            nearOffset = transform.position - gripPosition;
            return true;
        }
        public void MoveNear(object grabber, Vector3 gripPosition, Quaternion handRotation)
        {
            if (owner == null || !ReferenceEquals(owner, grabber)) return;
            var delta = handRotation * Quaternion.Inverse(initialAim);
            transform.SetPositionAndRotation(gripPosition + delta * nearOffset, delta * grabRotation);
        }
        void Start()
        {
            // Capture after callers have applied their panel tilt and scale (unless the shared space already did).
            if (initialized) return;
            defaultPosition = transform.localPosition; defaultRotation = transform.localRotation; initialized = true;
            if (Persist) LoadSaved();
        }
        public bool TryGetDefault(out Vector3 position, out Quaternion rotation)
        {
            position = defaultPosition; rotation = defaultRotation; return initialized;
        }
        // The personal console sets the home pose for the active layout, then restores a pose the user saved there.
        public void Rehome(Vector3 position, Quaternion rotation, string layoutKey)
        {
            layout = layoutKey ?? ""; defaultPosition = position; defaultRotation = rotation; initialized = true;
            if (owner != null) return;
            transform.SetLocalPositionAndRotation(position, rotation);
            LoadSaved();
        }
        void LoadSaved()
        {
            if (!Persist) return;
            if (!PlayerPrefs.HasKey(Key)) return;
            try
            {
                var pose = JsonUtility.FromJson<Pose>(PlayerPrefs.GetString(Key));
                if (pose == null || !Finite(pose.position) || pose.position.sqrMagnitude > 400 ||
                    !Finite(new Vector3(pose.rotation.x, pose.rotation.y, pose.rotation.z)) ||
                    float.IsNaN(pose.rotation.w) || float.IsInfinity(pose.rotation.w) ||
                    Quaternion.Dot(pose.rotation, pose.rotation) < .5f) return;
                transform.SetLocalPositionAndRotation(pose.position, pose.rotation.normalized);
            }
            catch (ArgumentException) { /* Ignore an invalid local preference. */ }
        }
        static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
            && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        public bool TryGrab(object grabber, Ray ray, float hitDistance, Quaternion aim)
        {
            if (grabber == null || Grabbed || !isActiveAndEnabled) return false;
            owner = grabber; distance = Mathf.Clamp(hitDistance, .2f, 10);
            grabOffset = transform.position - ray.GetPoint(distance);
            grabRotation = transform.rotation; initialAim = aim;
            return true;
        }
        public void Move(object grabber, Ray ray, Quaternion aim, float distanceDelta)
        {
            if (owner == null || !ReferenceEquals(owner, grabber)) return;
            distance = Mathf.Clamp(distance + distanceDelta, .2f, 10);
            var delta = aim * Quaternion.Inverse(initialAim);
            transform.SetPositionAndRotation(ray.GetPoint(distance) + delta * grabOffset, delta * grabRotation);
        }
        public void Release(object grabber)
        {
            if (!ReferenceEquals(owner, grabber) || owner == null) return;
            owner = null;
            if (Persist)
            {
                PlayerPrefs.SetString(Key, JsonUtility.ToJson(new Pose { position = transform.localPosition, rotation = transform.localRotation }));
                PlayerPrefs.Save();
            }
            Moved?.Invoke();
        }
        public void ResetPose()
        {
            owner = null;
            if (initialized) transform.SetLocalPositionAndRotation(defaultPosition, defaultRotation);
            if (Persist) { PlayerPrefs.DeleteKey(Key); PlayerPrefs.Save(); }
            Restored?.Invoke();
        }
        void OnDisable() { owner = null; }
    }
}
