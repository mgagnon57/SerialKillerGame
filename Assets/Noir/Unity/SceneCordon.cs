using System.Collections.Generic;
using UnityEngine;

namespace Noir.Unity
{
    /// <summary>
    /// The dressing of a held scene: two striped sawhorses closing the body's half of the
    /// street, and a ring of tape on posts around the scene itself. Raised when the
    /// officer arrives, lowered when the case closes — the host owns both moments.
    /// Generated primitives in the flat-color style; an owner model replaces them later
    /// without touching this logic, the cruiser's own precedent. Spec:
    /// docs/superpowers/specs/2026-08-17-scene-cordon-design.md
    /// </summary>
    public sealed class SceneCordon
    {
        private readonly Transform _parent;
        private readonly Dictionary<int, GameObject> _roots = new Dictionary<int, GameObject>();

        public static SceneCordon Create(Transform parent) => new SceneCordon(parent);
        private SceneCordon(Transform parent) { _parent = parent; }

        public bool IsUp(int caseId) => _roots.ContainsKey(caseId);

        public void Raise(int caseId, Vector3 sceneWorld, CordonLayout layout)
        {
            if (_roots.ContainsKey(caseId)) return;
            var root = new GameObject("Scene Cordon " + caseId);
            root.transform.SetParent(_parent, false);
            _roots[caseId] = root;

            if (layout.TrafficControlled)
            {
                var facing = Quaternion.LookRotation(layout.RoadAxis, Vector3.up);
                Sawhorse(root.transform, layout.BarricadeNear, facing);
                Sawhorse(root.transform, layout.BarricadeFar, facing);
            }
            TapeRing(root.transform, sceneWorld);
        }

        public void Lower(int caseId)
        {
            if (!_roots.TryGetValue(caseId, out var root)) return;
            _roots.Remove(caseId);
            if (root != null) Object.Destroy(root);
        }

        /// <summary>A municipal sawhorse: two A-frame legs and a striped crossbar,
        /// 2.0 m wide, bar at 0.8 m. The ROOT carries one box collider so the player's
        /// existing BoxCast (Player.DriveStep) stops at it — the one prop family where
        /// the keep-the-collider rule inverts the Frontage.Piece convention.</summary>
        private static void Sawhorse(Transform parent, Vector3 at, Quaternion facing)
        {
            var horse = new GameObject("Sawhorse");
            horse.transform.SetParent(parent, false);
            horse.transform.position = at;
            horse.transform.rotation = facing;
            var box = horse.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.5f, 0f);
            box.size = new Vector3(2.0f, 1.0f, 0.3f);

            // striped crossbar: orange - white - orange
            Piece(horse.transform, "bar-l", new Vector3(-0.67f, 0.8f, 0f), new Vector3(0.66f, 0.22f, 0.05f), Materials3D.CordonStripe);
            Piece(horse.transform, "bar-m", new Vector3(0f, 0.8f, 0f), new Vector3(0.66f, 0.22f, 0.05f), Materials3D.CordonWood);
            Piece(horse.transform, "bar-r", new Vector3(0.67f, 0.8f, 0f), new Vector3(0.66f, 0.22f, 0.05f), Materials3D.CordonStripe);
            // A-frame legs, splayed in z
            Leg(horse.transform, new Vector3(-0.85f, 0f, 0.18f), -12f);
            Leg(horse.transform, new Vector3(-0.85f, 0f, -0.18f), 12f);
            Leg(horse.transform, new Vector3(0.85f, 0f, 0.18f), -12f);
            Leg(horse.transform, new Vector3(0.85f, 0f, -0.18f), 12f);
        }

        private static void Leg(Transform parent, Vector3 foot, float lean)
        {
            var leg = Piece(parent, "leg", foot + new Vector3(0f, 0.42f, 0f),
                            new Vector3(0.06f, 0.84f, 0.06f), Materials3D.CordonWood);
            leg.transform.localRotation = Quaternion.Euler(lean, 0f, 0f);
        }

        /// <summary>Four posts and four tape runs boxing the scene, 3.2 m half-width —
        /// wide enough to ring the body, narrow enough to stay off the open lane.</summary>
        private static void TapeRing(Transform parent, Vector3 centre)
        {
            var half = 3.2f;
            var corners = new[]
            {
                centre + new Vector3(-half, 0f, -half), centre + new Vector3(half, 0f, -half),
                centre + new Vector3(half, 0f, half), centre + new Vector3(-half, 0f, half),
            };
            foreach (var c in corners)
                Piece(parent, "post", c + new Vector3(0f, 0.5f, 0f),
                      new Vector3(0.05f, 1.0f, 0.05f), Materials3D.CordonWood);
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = corners[i], b = corners[(i + 1) % 4];
                Vector3 mid = (a + b) / 2f + new Vector3(0f, 0.9f, 0f);
                var tape = Piece(parent, "tape", mid,
                                 new Vector3(Vector3.Distance(a, b), 0.08f, 0.01f), Materials3D.CordonTape);
                tape.transform.rotation = Quaternion.LookRotation(Vector3.Cross(b - a, Vector3.up), Vector3.up);
            }
        }

        private static GameObject Piece(Transform parent, string name, Vector3 position,
                                        Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            Object.Destroy(go.GetComponent<Collider>());   // the ROOT's collider is the one that counts
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            return go;
        }
    }
}
