using System.Collections.Generic;
using UnityEngine;
using Noir.Core.Contracts;
using Noir.Core.People;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Noir.Unity
{
    /// <summary>
    /// What a person is drawn as. Either shape answers this, and nothing above it cares which.
    ///
    /// A primitive `AgentFigure` swings its own legs off a phase angle and has no Animator; a
    /// bought `AgentBody` has an Animator and ignores the phase entirely. Keeping both behind one
    /// surface is what let the rigged people arrive without touching a line of AgentMeshView's
    /// arithmetic - it still works out where everybody is, which way they face and how far into a
    /// stride they are, and hands those numbers over.
    /// </summary>
    public interface IAgentBody
    {
        Transform Root { get; }

        /// <summary>The Animator, or null. See AgentAnimation.Drive, which accepts null.</summary>
        Animator Animator { get; }

        void Pose(Vector3 ground, float yaw, float phase, float swing, bool carrying);

        /// <summary>
        /// Repaint, which in practice means the selection highlight and nothing else.
        ///
        /// A primitive figure has a torso and legs to paint separately; a bought one is a single
        /// skinned mesh on a shared atlas, so the two colours arrive and only the first is used.
        /// The signature is the primitive's because it was here first and there is no gain in
        /// widening it for a case that does not want it.
        /// </summary>
        void SetClothing(MaterialPropertyBlock block, Color top, Color bottom);
    }

    /// <summary>
    /// A person, as one of the pack's own rigged characters.
    ///
    /// NO TWO PEOPLE LOOK THE SAME, and that is not done with 79 prefabs - there are only about
    /// twenty in register for an ordinary town, against a population of hundreds. It is done with
    /// the atlas. `Universal_A_Alb` is 4096 square and 428KB, which is the compression signature
    /// of flat colour blocks rather than a texture, and it IS one: a labelled swatch grid where
    /// each ROW is a role - primary, secondary, tertiary, hair, skin, hide - and each row is a
    /// ramp of shades across its columns. A character's coat colour is therefore a UV coordinate,
    /// not a texture, and moving a vertex along its own row recolours that garment and nothing
    /// else. Measured: `Man_Slavic_Summer_Hair` puts 2,841 vertices on 27 cells across 10 roles.
    ///
    /// SHIFTED ALONG THE ROW, NEVER ACROSS IT. Staying on the row is what makes this safe without
    /// knowing which row is which: skin stays somewhere in the skin ramp, hair in the hair ramp,
    /// a coat in its own. Moving DOWN a row would need the atlas mapped first and would otherwise
    /// give somebody a green face. So this varies shade rather than hue, which is plenty to make
    /// a street of people who are all different, and the hue is a later job.
    ///
    /// The mesh is cloned per person because the UVs differ per person. They are small - about
    /// 2,800 vertices - and there is one per citizen rather than one per frame.
    /// </summary>
    public sealed class AgentBody : IAgentBody
    {
        public Transform Root { get; private set; }
        public Animator Animator { get; private set; }

        private SkinnedMeshRenderer _skin;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// The selection highlight, over the whole person.
        ///
        /// A primitive figure gets its jumper repainted and keeps its face; this cannot, because
        /// the character is one mesh on one material and there is nothing to paint separately.
        /// Tinting all of them is the honest version of the same idea - it says THIS ONE, which
        /// is the entire job, and a highlight that only caught the coat would be harder to see in
        /// a crowd rather than easier.
        /// </summary>
        public void SetClothing(MaterialPropertyBlock block, Color top, Color bottom)
        {
            if (_skin == null) return;

            _skin.GetPropertyBlock(block);
            // Selected is a colour nothing in the pack's palette is, so anything else means
            // "not selected" and the tint goes back to white, which multiplies to no change.
            block.SetColor(BaseColorId, top == Palette.Selected ? top : Color.white);
            _skin.SetPropertyBlock(block);
        }

        public void Pose(Vector3 ground, float yaw, float phase, float swing, bool carrying)
        {
            // The animator owns the legs, the arms and the bob. All that is left is where the
            // person is and which way they are pointing - which is the simulation's business and
            // the only part of a walk cycle an in-place clip deliberately does not have.
            Root.position = ground;
            Root.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

#if UNITY_EDITOR
        private const string Folk = "Assets/polyperfect/Poly Universal Pack/Prefabs/People";
        private const string Controller = "Assets/Noir/Animations/Townsfolk.controller";

        /// <summary>
        /// How far along its row a vertex may be moved, as a fraction of the atlas width.
        ///
        /// The grid is 64 cells across, so a sixteenth of the sheet is four swatches - enough for
        /// a coat to be visibly a different coat, and not so much that a row's whole ramp is
        /// crossed and a pale shirt comes out black. It is added, then wrapped inside the row, so
        /// nothing ever lands on a neighbouring role.
        /// </summary>
        private const float Along = 1f / 16f;

        private static List<string> _men, _women, _boys, _girls;
        private static RuntimeAnimatorController _controller;
        private static readonly Dictionary<CitizenKey, Mesh> _dressed =
            new Dictionary<CitizenKey, Mesh>();

        /// <summary>
        /// One person, or null if the pack has nobody who could be them.
        ///
        /// WHO THEY LOOK LIKE COMES FROM WHO THEY ARE: the forename list they were drawn from and
        /// whether they are a child. Elders are drawn from the adult sets and stooped and shrunk
        /// by AgentLook, because there is no elderly figure anywhere in the pack - a fact worth
        /// knowing rather than working around, since it is also why `AgeBand` can only ever say
        /// adult or child.
        /// </summary>
        public static AgentBody Build(Transform parent, Citizen who, in AgentLook look)
        {
            var set = who.IsChild ? (who.Male ? Boys() : Girls())
                                  : (who.Male ? Men() : Women());
            if (set.Count == 0) return null;

            ulong seed = who.Key.Value;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                set[(int)(Mix(seed, 0x9E37) % (ulong)set.Count)]);
            if (prefab == null) return null;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = who.FullName;
            go.transform.SetParent(parent, false);

            var body = new AgentBody { Root = go.transform };

            // ---- height and build ----
            //
            // The pack's own adults are 1.86 to 2.03m, which is tall, and its children are sized
            // for a different game. AgentLook already decides how tall THIS person is from their
            // age and their id, so the model is scaled to agree with it rather than the other way
            // round - the simulation's idea of a person is the one that has to win, because it is
            // the one the door heights and the queue spacing were built against.
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                if (b.size.y > 0.01f)
                {
                    float tall = look.Height / b.size.y;
                    // Breadth is a touch of width without making anybody a cylinder.
                    float wide = tall * Mathf.Lerp(0.94f, 1.08f, Mathf.InverseLerp(0.9f, 1.18f, look.Breadth));
                    go.transform.localScale = new Vector3(wide, tall, wide);
                }
            }

            // ---- their own clothes ----
            var skin = go.GetComponentInChildren<SkinnedMeshRenderer>();

            // WHERE THE MESH CAME FROM, read BEFORE it is replaced. The avatar lives beside the
            // mesh as a sub-asset of the same .fbx, and once sharedMesh points at our own clone
            // there is no longer anything on this object that knows which model it came from.
            string model = skin != null && skin.sharedMesh != null
                ? AssetDatabase.GetAssetPath(skin.sharedMesh)
                : null;

            if (skin != null && skin.sharedMesh != null)
                skin.sharedMesh = Dressed(skin.sharedMesh, who.Key);
            body._skin = skin;

            // ---- and something to play ----
            var animator = go.GetComponentInChildren<Animator>();
            if (animator == null) animator = go.AddComponent<Animator>();

            _controller ??= AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Controller);
            if (_controller != null) animator.runtimeAnimatorController = _controller;

            // NO AVATAR MEANS NO RETARGETING, AND IT FAILS SILENTLY. A humanoid clip played
            // through an animator with no avatar does not throw and does not warn - the figure
            // simply stands in its bind pose and slides about, which looks exactly like a person
            // who has not been told to walk. Seventy-four of the 365 arrived this way, because
            // not every prefab in the pack carries an Animator of its own and one added here has
            // nothing on it. The avatar is a sub-asset of the model, so it is fetched from there.
            if (animator.avatar == null && model != null) animator.avatar = AvatarFor(model);

            // The clips are in place by design, so the animator must never move anybody: the
            // simulation decides where people are and this would fight it for the same number.
            animator.applyRootMotion = false;

            // NOT CullCompletely, WHICH STOPS THEM DEAD. That disables the animator outright when
            // its renderers are not judged visible - and a disabled animator does not update the
            // bounds that decide visibility, so a figure that falls out of view can stay stopped.
            // Measured with it on: of forty animators watched over a whole second, forty had not
            // advanced their clip by a single frame. CullUpdateTransforms keeps the state machine
            // running and only skips writing the bones, which is the saving actually wanted.
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            body.Animator = animator;
            return body;
        }

        /// <summary>
        /// This person's copy of the mesh, with every vertex nudged along its own atlas row.
        ///
        /// Cached per citizen KEY rather than per id, so the same person is dressed the same way
        /// in any village built from the same seed - the key outlives the arrays, which is the
        /// whole reason it exists.
        /// </summary>
        private static Mesh Dressed(Mesh source, CitizenKey key)
        {
            if (_dressed.TryGetValue(key, out var had) && had != null) return had;

            var copy = Object.Instantiate(source);
            copy.name = source.name + "_" + key.Value.ToString("x8");

            var uv = copy.uv;
            if (uv == null || uv.Length == 0) { _dressed[key] = copy; return copy; }

            // The grid is 64 x 64 across the sheet. A vertex keeps its ROW - which is its role -
            // and moves within it, wrapping at the row's edges so it can never step onto the
            // role above or below.
            const float Cell = 1f / 64f;
            float shift = (Mix(key.Value, 0x51ED) % 1000UL) / 1000f * Along;

            for (int i = 0; i < uv.Length; i++)
            {
                float row = Mathf.Floor(uv[i].y / Cell) * Cell;   // the role, untouched
                float into = uv[i].y - row;

                // Along the row and wrapped inside the sheet. Roles run the full width, so
                // wrapping at 1 stays on the same row.
                float u = Mathf.Repeat(uv[i].x + shift, 1f);
                uv[i] = new Vector2(u, row + into);
            }

            copy.uv = uv;
            copy.UploadMeshData(false);
            _dressed[key] = copy;
            return copy;
        }

        // ---- who the pack can offer ----
        //
        // In register for an ordinary town, and nothing else: the Slavic set is the backbone,
        // Farm is the two in wellies, and a handful of the film crew pass as townspeople. The
        // Fantasy knights, the Primeval tribe and the Seasons costumes are all deliberately out.
        private static List<string> Men() => _men ??= Cast(
            "Man_Slavic_Summer_Hair", "Man_Slavic_Summer_Hat", "Man_Slavic_Winter",
            "Man_Farm_Wellies", "Man_Director_Movie", "Man_Sound_Movie", "Man_Camera_Movie",
            "Man_Glasses_Steampunk", "Man_Worker_Steampunk", "Man_Poor_Steampunk");

        private static List<string> Women() => _women ??= Cast(
            "Woman_Slavic_Summer_Hair", "Woman_Slavic_Summer_Scarf", "Woman_Slavic_Winter",
            "Woman_Farm_Wellies", "Woman_Stylist_Movie",
            "Woman_Hat_Small_Steampunk", "Woman_Poor_Steampunk");

        private static List<string> Boys() => _boys ??= Cast(
            "Boy_Slavic_Summer", "Boy_Slavic_Winter", "Boy_Poor_Steampunk", "Boy_Rich_Steampunk");

        private static List<string> Girls() => _girls ??= Cast(
            "Girl_Slavic_Summer", "Girl_Slavic_Winter", "Girl_Poor_Steampunk", "Girl_Rich_Steampunk");

        private static List<string> Cast(params string[] wanted)
        {
            var found = new List<string>();
            foreach (var name in wanted)
            {
                foreach (var guid in AssetDatabase.FindAssets($"{name} t:Prefab", new[] { Folk }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (System.IO.Path.GetFileNameWithoutExtension(path) != name) continue;
                    found.Add(path);
                    break;
                }
            }
            found.Sort(System.StringComparer.Ordinal);   // stable, so a crowd looks the same twice
            return found;
        }

        /// <summary>
        /// The avatar that belongs to a model, cached by path - there are about twenty distinct
        /// characters behind three hundred and sixty-five people.
        /// </summary>
        private static Avatar AvatarFor(string model)
        {
            if (_avatars.TryGetValue(model, out var had)) return had;

            Avatar found = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(model))
                if (asset is Avatar avatar) { found = avatar; break; }

            _avatars[model] = found;
            return found;
        }

        private static readonly Dictionary<string, Avatar> _avatars =
            new Dictionary<string, Avatar>(System.StringComparer.Ordinal);

        private static ulong Mix(ulong v, ulong salt)
        {
            v ^= salt;
            v ^= v >> 33; v *= 0xFF51AFD7ED558CCDUL;
            v ^= v >> 33; v *= 0xC4CEB9FE1A85EC53UL;
            return v ^ (v >> 33);
        }
#endif
    }
}
