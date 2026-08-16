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
    /// each ROW is a role. A character's coat colour is therefore a UV coordinate, not a texture,
    /// and moving a vertex along its own row recolours that garment and nothing else. Measured:
    /// `Man_Slavic_Summer_Hair` puts 2,841 vertices on 27 cells across 10 roles.
    ///
    /// THE GRID IS 32 x 32 CELLS OF 128px, MEASURED OFF THE SHEET 2026-08-09. Four comments in
    /// this file and two documents said sixteen or sixty-four, and a safety argument was built on
    /// top of the wrong number. What is actually there, per row:
    ///
    ///     columns  0-19   the role's twenty-step shade ramp, near-black to white
    ///     columns 20-29   ONE flat colour, repeated ten times
    ///     column     30   the emission key - the only non-black column in Universal_A_Emit
    ///     column     31   an accent
    ///
    /// The rows are labelled on the sheet: 1 skin, 2 hide, 3 hair, 9 stone, 13 tertiary,
    /// 14 secondary, 15 primary, and so on.
    ///
    /// SHIFTED ALONG THE ROW, NEVER ACROSS IT. Staying on the row is what makes this safe without
    /// knowing which row is which: skin stays somewhere in the skin ramp, hair in the hair ramp,
    /// a coat in its own. Moving DOWN a row would need the atlas mapped first and would otherwise
    /// give somebody a green face. So this varies shade rather than hue, which is plenty to make
    /// a street of people who are all different, and the hue is a later job.
    ///
    /// WHAT STAYING ON THE ROW DOES NOT MAKE SAFE IS WRAPPING, and that is UVX-A5, which belongs
    /// to `docs/ANIMATION-FIXES` because this file does. A shift that runs past column 19 leaves
    /// the ramp for the flat block or the emission key; at the current `Along` it can reach column
    /// 21 from the far end of a ramp, which is the flat block rather than a shade. The shift wants
    /// bounding rather than wrapping. It is named here so the next session does not re-derive the
    /// same bug from the same wrong comment, which is exactly what happened last time.
    ///
    /// The mesh is cloned per person because the UVs differ per person. They are small - about
    /// 2,800 vertices - and there is one per citizen rather than one per frame.
    /// </summary>
    public sealed class AgentBody : IAgentBody
    {
        public Transform Root { get; private set; }
        public Animator Animator { get; private set; }

        private SkinnedMeshRenderer _skin;

        /// <summary>
        /// The thing in their hand, parented to the right-hand BONE so the animation carries it.
        ///
        /// Built on first use rather than for all 1,385 people up front: most of the town is not
        /// carrying anything at any moment, and a shopping bag per citizen is 1,385 renderers for
        /// scenery. Kept once made, because somebody who has shopped once will shop again.
        /// </summary>
        private GameObject _bag;
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

            // SIM-8. THE BAG NEVER REACHED A BOUGHT BODY. `carrying` arrived here and was
            // ignored: the primitive figure hangs a box off its arm transform and the rigged one
            // did nothing at all, so the moment the town got real people, every shopper walked
            // home from the shop empty-handed - and `PersonDescription.CarriedThing` is a WITNESS
            // property. A watcher is supposed to be able to say "he had something in his hand".
            //
            // A PROP ON THE HAND BONE, NOT A CLIP. Parenting to `HumanBodyBones.RightHand` means
            // the existing animation carries it for free and it needs no bespoke carry cycle -
            // which is the difference between one object and re-animating eighty-seven clips.
            if (!carrying) { if (_bag != null && _bag.activeSelf) _bag.SetActive(false); return; }

            if (_bag == null && !MakeBag()) return;
            if (!_bag.activeSelf) _bag.SetActive(true);
        }

        /// <summary>
        /// Hang a bag off the right hand, once. False if this rig has no mapped right hand - the
        /// pack's figures are humanoid, but a figure that is not must not take the whole frame
        /// down for a shopping bag.
        /// </summary>
        private bool MakeBag()
        {
            var hand = Animator != null && Animator.isHuman
                ? Animator.GetBoneTransform(HumanBodyBones.RightHand)
                : null;
            if (hand == null) return false;

            _bag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _bag.name = "Bag";
            Object.DestroyImmediate(_bag.GetComponent<Collider>());

            var r = _bag.GetComponent<MeshRenderer>();
            r.sharedMaterial = Materials3D.Bag;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            _bag.transform.SetParent(hand, false);
            // A carrier bag, held at the side: small, a little below the grip, hanging clear of
            // the leg. In the hand's own space, so it swings with the arm the animator is moving.
            _bag.transform.localScale = new Vector3(0.16f, 0.22f, 0.10f);
            _bag.transform.localPosition = new Vector3(0.02f, -0.14f, 0.02f);
            _bag.transform.localRotation = Quaternion.identity;
            return true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Where the figures live. PUBLIC so MeshReadable can walk the same folder rather than
        /// keep a second copy of this path - the tool that makes their meshes readable and the
        /// code that instantiates them must never disagree about where they are. It has to be
        /// public rather than internal because MeshReadable is in Noir.Editor and this is in
        /// Noir.Unity, and `internal` does not cross an assembly.
        /// </summary>
        public const string Folk = "Assets/polyperfect/Poly Universal Pack/Prefabs/People";
        private const string Controller = "Assets/Noir/Animations/Townsfolk.controller";

        /// <summary>
        /// How far along its row a vertex may be moved, as a fraction of the atlas width.
        ///
        /// The grid is 32 cells across - measured, see the class docstring - so a sixteenth of
        /// the sheet is TWO swatches, not the four this said. Enough for a coat to be visibly a
        /// different coat, and nowhere near enough to cross a row's whole twenty-step ramp. It is
        /// added and then wrapped inside the sheet, so it never lands on a neighbouring ROLE -
        /// but see the class docstring: wrapping can still leave the ramp for the flat block.
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
        public static AgentBody Build(Transform parent, Citizen who, in AgentLook look,
                                      bool uniformed = false)
        {
            var set = who.IsChildIn(VillageHost.Year) ? (who.Male ? Boys() : Girls())
                                  : (who.Male ? Men() : Women());
            if (set.Count == 0) return null;

            // POLICE LOOK ALIKE — that is what a uniform is. A precinct worker bypasses the
            // hash pick for the one pinned figure whose garment cells PoliceCells was measured
            // against — BY NAME, because Cast() sorts its paths (the crowd-stability rule) and
            // "first of the declared list" is really "alphabetically first folder", which is how
            // the first officer ever drawn wore farm wellies and a straw hat (measured
            // 2026-08-16). Adult men only for now: PoliceCells is a fact about ONE mesh, and
            // the roster has yet to produce a woman officer to probe a second figure for — if
            // the look step ever shows one in civvies, that is the signal to extend, not a fault.
            string pinPath = null;
            if (uniformed && !who.IsChildIn(VillageHost.Year) && who.Male)
                foreach (var candidate in set)
                    if (candidate.EndsWith("/" + OfficerFigure + ".prefab"))
                    { pinPath = candidate; break; }

            ulong seed = who.Key.Value;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                pinPath ?? set[(int)(Mix(seed, 0x9E37) % (ulong)set.Count)]);
            if (prefab == null) return null;
            bool pin = pinPath != null;

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

                    // UNIFORM, AND THE WIDTH VARIATION IS DELIBERATELY GONE.
                    //
                    // This was `new Vector3(wide, tall, wide)` with X and Z off Y by -6%/+8%.
                    // Non-uniform scale above a SKINNED hierarchy shears: bones rotate inside it,
                    // and a limb rotating under anisotropic scale changes thickness as it swings,
                    // so a short wide person gets forearms the wrong width for their length. It
                    // reads as a rendering fault from twenty feet; the ±7% silhouette it bought
                    // reads as nothing at all at that distance. `AgentFigure` already refuses to
                    // do this to the primitives and says so.
                    //
                    // NOT A DETERMINISM CHANGE. `look.Breadth` is still hashed per citizen and is
                    // still used by the primitive figures, so no seed reproduces a different
                    // village - the rigged people simply stop being sheared by it. Build variety
                    // comes from 25 cast models and the per-citizen UV shift, which is where it
                    // was always doing the real work. Owner's decision, 2026-08-08.
                    go.transform.localScale = new Vector3(tall, tall, tall);
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
                skin.sharedMesh = pin ? Uniformed(skin.sharedMesh)
                                      : Dressed(skin.sharedMesh, who.Key);
            body._skin = skin;

            // ---- and something to play ----
            var animator = go.GetComponentInChildren<Animator>();
            if (animator == null) animator = go.AddComponent<Animator>();

            // `if (== null)`, NOT `??=`. Unity's fake null is the trap: a destroyed UnityEngine
            // .Object compares equal to null through its overloaded operator, but it is NOT a null
            // reference, and `??=` / `?.` / `??` are compiled to a reference check that the
            // overload never gets to see. So after a domain reload or an asset reimport this
            // would keep a dead controller forever and every figure would silently lose its
            // animation - while the field reads as "not null" to the null-coalescing operator and
            // as "null" to everything else in the file.
            if (_controller == null)
                _controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Controller);
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

            // THE SIM RUNS UNSCALED AND SO MUST THE LEGS.
            //
            // `Simulation` steps on `Time.unscaledDeltaTime` deliberately - how fast a day passes
            // is a property of the game, not of Unity, and CLAUDE.md says so. The animator was on
            // the default `Normal`, which is scaled. So the moment anything touched `timeScale`
            // the people's legs and the people's POSITIONS ran on different clocks, and the walk
            // stopped matching the ground under it - which is the same skating fault the pace
            // ratio exists to remove, arriving by a different door. The PlayMode suite sets
            // timeScale to 8, so every animation measurement ever taken here was taken through it.
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;

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

            // A VERTEX KEEPS ITS ROW - which is its role - and moves along it.
            //
            // The row arithmetic that used to be here was dead: it computed `row` and `into` from
            // a cell size and then wrote back `row + into`, which is `uv[i].y` identically. It
            // also declared the grid 64 x 64, which it is not. Both are gone rather than
            // corrected, because arithmetic that cannot change its input is not documentation of
            // an invariant - it is something for the next reader to verify and then discard.
            // V IS SIMPLY NOT TOUCHED, which is the invariant, stated once.
            float shift = (Mix(key.Value, 0x51ED) % 1000UL) / 1000f * Along;

            for (int i = 0; i < uv.Length; i++)
                uv[i] = new Vector2(Mathf.Repeat(uv[i].x + shift, 1f), uv[i].y);

            copy.uv = uv;
            copy.UploadMeshData(false);
            _dressed[key] = copy;
            return copy;
        }

        /// <summary>
        /// Atlas cells (x,y in the 32-grid) that make the pinned officer figure a uniform:
        /// source garment cell -> the navy cell. Found empirically 2026-08-16 by probing
        /// man-slavic-summer-hair's 12 distinct UV cells against Universal_A_Alb (the probe is
        /// in the plan, Task 3 Step 1; the numbers are the keeper): the off-white shirt at
        /// (19,26)/(18,26) and its red accent at (6,15) go to navy (24,10) rgb(0.16,0.25,0.39);
        /// the khaki trousers at (10,28)/(8,28) go to the darker navy (24,11)
        /// rgb(0.12,0.16,0.25). Skin (11,1), hair and shoes are untouched. A cell pair here is
        /// a FACT about ONE mesh — do not reuse across models.
        /// </summary>
        /// <summary>The one figure PoliceCells was measured against. A NAME, matched against
        /// Cast()'s sorted paths — never an index into them.</summary>
        private const string OfficerFigure = "Man_Slavic_Summer_Hair";

        private static readonly (Vector2Int from, Vector2Int to)[] PoliceCells =
        {
            (new Vector2Int(19, 26), new Vector2Int(24, 10)),
            (new Vector2Int(18, 26), new Vector2Int(24, 10)),
            (new Vector2Int(6, 15),  new Vector2Int(24, 10)),
            (new Vector2Int(10, 28), new Vector2Int(24, 11)),
            (new Vector2Int(8, 28),  new Vector2Int(24, 11)),
        };

        private static readonly Dictionary<Mesh, Mesh> _uniformed = new Dictionary<Mesh, Mesh>();

        /// <summary>
        /// The pinned figure's mesh in navy — Dressed()'s clone shape, but a targeted cell
        /// REMAP and no per-citizen shift: a uniform is the same coat on every officer, which
        /// is what a uniform is. Cached per SOURCE mesh, not per citizen.
        /// </summary>
        private static Mesh Uniformed(Mesh source)
        {
            if (_uniformed.TryGetValue(source, out var had) && had != null) return had;

            var copy = Object.Instantiate(source);
            copy.name = source.name + "_police";

            var uv = copy.uv;
            if (uv == null || uv.Length == 0) { _uniformed[source] = copy; return copy; }

            for (int i = 0; i < uv.Length; i++)
            {
                var cell = new Vector2Int(Mathf.FloorToInt(Mathf.Repeat(uv[i].x, 1f) * 32f),
                                          Mathf.FloorToInt(Mathf.Repeat(uv[i].y, 1f) * 32f));
                foreach (var (from, to) in PoliceCells)
                {
                    if (cell != from) continue;
                    float fx = Mathf.Repeat(uv[i].x, 1f) * 32f - cell.x;
                    float fy = Mathf.Repeat(uv[i].y, 1f) * 32f - cell.y;
                    uv[i] = new Vector2((to.x + fx) / 32f, (to.y + fy) / 32f);
                    break;
                }
            }

            copy.uv = uv;
            copy.UploadMeshData(false);
            _uniformed[source] = copy;
            return copy;
        }

        /// <summary>
        /// The county officer actor's version of the same treatment: CityResponse instantiates
        /// its own prefab (he has no Citizen), so the navy goes on after the fact. Safe on any
        /// instance; a no-op when there is no skinned mesh to dress.
        /// </summary>
        public static void UniformThisInstance(GameObject go)
        {
            var skin = go != null ? go.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (skin == null || skin.sharedMesh == null) return;
            skin.sharedMesh = Uniformed(skin.sharedMesh);
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

        /// <summary>
        /// EVERY FIGURE THE TOWN CAN PLACE, and nothing else.
        ///
        /// `MeshReadable` walked all 79 prefabs under `Folk` to decide which meshes needed
        /// Read/Write - which is 79 model imports and 79 meshes held in memory for the lifetime of
        /// the process, for a town that places 25 of them. The rest are the Fantasy knights, the
        /// Primeval tribe and the Seasons costumes, deliberately out of register and never drawn.
        ///
        /// Derived from the four lists rather than maintained beside them, so a figure added to
        /// the cast is covered on the day it is added - the same argument `AnimationCheck` makes
        /// for enumerating the `Activity` enum instead of sweeping the rows.
        /// </summary>
        public static IEnumerable<string> EveryCastPrefabPath()
        {
            foreach (var path in Men()) yield return path;
            foreach (var path in Women()) yield return path;
            foreach (var path in Boys()) yield return path;
            foreach (var path in Girls()) yield return path;
        }

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
