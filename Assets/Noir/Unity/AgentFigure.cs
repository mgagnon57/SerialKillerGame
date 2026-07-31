using UnityEngine;
using UnityEngine.Rendering;
using Noir.Core.People;

namespace Noir.Unity
{
    /// <summary>
    /// How one particular villager looks: height, build, clothing, decided once from their id.
    ///
    /// Every number here comes from a hash of the citizen id and never from Random or from
    /// anything that varies per frame. Recognising somebody is the entire reason for watching
    /// one person for ten minutes, and that fails the moment the tall man in the ochre jumper
    /// is a different man after a reload.
    /// </summary>
    public readonly struct AgentLook
    {
        /// <summary>Metres from the ground to the crown of the head.</summary>
        public readonly float Height;

        /// <summary>Width multiplier. 1 is average; the range is about a stone either way.</summary>
        public readonly float Breadth;

        /// <summary>Head size relative to the body. Children are all head, and it reads.</summary>
        public readonly float HeadScale;

        /// <summary>Degrees the torso leans forward. Elders lean; the young barely do.</summary>
        public readonly float Stoop;

        /// <summary>Where in the stride they start, so a hundred people do not step in unison.</summary>
        public readonly float Phase;

        public readonly Color Top, Bottom, Skin, Hair, Bag;

        private AgentLook(float height, float breadth, float headScale, float stoop, float phase,
                          Color top, Color bottom, Color skin, Color hair, Color bag)
        {
            Height = height; Breadth = breadth; HeadScale = headScale; Stoop = stoop; Phase = phase;
            Top = top; Bottom = bottom; Skin = skin; Hair = hair; Bag = bag;
        }

        /// <summary>Metres covered by one full stride - a left step and a right step together.</summary>
        public float Stride => Height * 0.82f;

        public static AgentLook Of(Citizen who)
        {
            int id = who.Id.Value;

            // Children are sized by age rather than by stage: a six-year-old and a ten-year-old
            // are not the same shape, and a school emptying should not look like one clone class.
            float baseline;
            switch (who.Stage)
            {
                case LifeStage.Child:
                    baseline = Mathf.Lerp(1.02f, 1.58f, Mathf.InverseLerp(5f, 15f, who.Age));
                    break;
                case LifeStage.Elder:
                    baseline = 1.63f;
                    break;
                default:
                    baseline = 1.71f;
                    break;
            }

            float height = baseline * (0.95f + Fraction(id, 11) * 0.10f);
            float breadth = 0.90f + Fraction(id, 23) * 0.28f;
            float headScale = who.IsChild
                ? Mathf.Lerp(1.24f, 1.04f, Mathf.InverseLerp(5f, 15f, who.Age))
                : 1f;
            float stoop = who.Stage == LifeStage.Elder
                ? 5f + Fraction(id, 31) * 8f
                : Fraction(id, 31) * 2.5f;

            // Grey is common in the old and not universal - a village full of white heads is as
            // wrong as a village with none.
            bool greying = who.Stage == LifeStage.Elder && Fraction(id, 67) < 0.8f;

            return new AgentLook(
                height, breadth, headScale, stoop, Fraction(id, 71) * Mathf.PI * 2f,
                Choose(Tops, id, 41),
                Choose(Bottoms, id, 47),
                Choose(Skins, id, 53),
                Choose(greying ? Greys : Hairs, id, 59),
                Choose(Bags, id, 61));
        }

        private static uint Hash(int id, int salt) => Materials3D.Scatter(id, salt, 0x5EE1);

        private static float Fraction(int id, int salt) => Hash(id, salt) * (1f / 4294967296f);

        private static Color Choose(Color[] from, int id, int salt) =>
            from[(int)(Hash(id, salt) % (uint)from.Length)];

        // ---- 1979, rural, and mostly bought some years earlier ----
        //
        // Muted and close in value on purpose: saturated clothing turns a street into a bag of
        // sweets, and a village that owns two dozen shades of nothing-in-particular is exactly
        // what a village owned. The lightness spread is what does the work of separating people
        // from the grass, not the hue.

        private static readonly Color[] Tops =
        {
            new Color32(0x6F, 0x74, 0x5A, 0xFF),   // olive drab
            new Color32(0x4E, 0x58, 0x66, 0xFF),   // faded navy
            new Color32(0xA0, 0x94, 0x7C, 0xFF),   // oatmeal
            new Color32(0x84, 0x53, 0x40, 0xFF),   // brick
            new Color32(0x6E, 0x4E, 0x54, 0xFF),   // dusty maroon
            new Color32(0x62, 0x72, 0x68, 0xFF),   // grey-green
            new Color32(0xA2, 0x88, 0x50, 0xFF),   // worn ochre
            new Color32(0x4E, 0x4C, 0x4A, 0xFF),   // charcoal
            new Color32(0x54, 0x6E, 0x72, 0xFF),   // dull teal
            new Color32(0xB6, 0xAC, 0x98, 0xFF),   // cream
            new Color32(0x8A, 0x86, 0x88, 0xFF),   // heather grey
            new Color32(0x7C, 0x6C, 0x76, 0xFF),   // plum grey
            new Color32(0x8E, 0x9A, 0x86, 0xFF),   // sage
            new Color32(0x93, 0x6B, 0x4E, 0xFF),   // tan wool
        };

        private static readonly Color[] Bottoms =
        {
            new Color32(0x48, 0x52, 0x62, 0xFF),   // denim
            new Color32(0x6C, 0x54, 0x3E, 0xFF),   // brown corduroy
            new Color32(0x40, 0x40, 0x42, 0xFF),   // charcoal
            new Color32(0x5C, 0x60, 0x48, 0xFF),   // olive
            new Color32(0x70, 0x6E, 0x6A, 0xFF),   // grey flannel
            new Color32(0x8C, 0x7A, 0x5E, 0xFF),   // tan
            new Color32(0x56, 0x4A, 0x44, 0xFF),   // dark brown
        };

        private static readonly Color[] Skins =
        {
            new Color32(0xE0, 0xBE, 0xA2, 0xFF),
            new Color32(0xD2, 0xAA, 0x8C, 0xFF),
            new Color32(0xEC, 0xD2, 0xB8, 0xFF),
            new Color32(0xBE, 0x92, 0x74, 0xFF),
            new Color32(0x9E, 0x76, 0x5A, 0xFF),
        };

        private static readonly Color[] Hairs =
        {
            new Color32(0x30, 0x28, 0x24, 0xFF),   // near black
            new Color32(0x4C, 0x38, 0x28, 0xFF),   // dark brown
            new Color32(0x6A, 0x4E, 0x36, 0xFF),   // mid brown
            new Color32(0x96, 0x78, 0x4E, 0xFF),   // sandy
            new Color32(0x7C, 0x46, 0x2E, 0xFF),   // auburn
            new Color32(0x3A, 0x34, 0x30, 0xFF),   // black-grey
            new Color32(0xB0, 0x9A, 0x74, 0xFF),   // fair
        };

        private static readonly Color[] Greys =
        {
            new Color32(0xA6, 0xA2, 0x9A, 0xFF),
            new Color32(0xC4, 0xC0, 0xB8, 0xFF),
            new Color32(0x86, 0x84, 0x80, 0xFF),
        };

        private static readonly Color[] Bags =
        {
            new Color32(0x8A, 0x74, 0x50, 0xFF),   // leather
            new Color32(0x9E, 0x98, 0x84, 0xFF),   // canvas
            new Color32(0x6E, 0x5C, 0x44, 0xFF),   // dark holdall
            new Color32(0xB2, 0xA6, 0x8C, 0xFF),   // paper carrier
        };
    }

    /// <summary>
    /// One person, assembled from Unity primitives.
    ///
    /// This is a placeholder for real character models, so the whole of the construction lives
    /// here and nothing outside it knows what a person is made of. Swapping in models later
    /// means rewriting Build and Pose; the view above only ever says "stand here, face that
    /// way, you are this far into a stride, you are carrying something".
    ///
    /// The shape is chosen for one thing above all others: a front. A capsule has none, so two
    /// people stopped in the street to talk read as two dots. A torso nearly twice as wide as
    /// it is deep gives the shoulder line, and the hair sits back on the skull so the head
    /// tells you which end of that line is the face - from street level and from overhead.
    /// </summary>
    public sealed class AgentFigure
    {
        /// <summary>Renderers per person, counting the bag they are usually not carrying.</summary>
        public const int PartCount = 8;

        /// <summary>Peak leg swing in degrees, at a normal walking pace.</summary>
        public const float MaxSwing = 26f;

        // Proportions, as fractions of total height. Roughly life-drawing canon: legs a little
        // under half, head a seventh.
        private const float LegFraction = 0.46f;
        private const float TorsoFraction = 0.34f;
        private const float NeckFraction = 0.025f;
        private const float HeadFraction = 0.155f;
        private const float ShoulderFraction = 0.255f;
        private const float DepthFraction = 0.150f;
        private const float LegThickFraction = 0.078f;
        private const float ArmThickFraction = 0.060f;
        private const float ArmFraction = 0.300f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>The whole figure. Moved and turned once per frame; everything else is local.</summary>
        public Transform Root { get; private set; }

        /// <summary>
        /// The figure's Animator, or NULL - which is what a figure built from primitives has and
        /// always will have.
        ///
        /// It exists so `AgentMeshView` can hand every person to `AgentAnimation.Drive` without
        /// caring which sort of figure it is. A primitive swings its legs off a phase angle and
        /// answers null here; a rigged character bought from the pack answers with its Animator
        /// and gets a clip. The seam is one property rather than a branch in the render loop,
        /// which is what makes swapping the body a swap rather than a rewrite.
        /// </summary>
        public Animator Animator { get; private set; }

        private Transform _legL, _legR, _armL, _armR, _bag;
        private GameObject _bagObject;
        private MeshRenderer _torsoRenderer, _legLRenderer, _legRRenderer, _armLRenderer, _armRRenderer;

        private Vector3 _hipL, _hipR, _shoulderL, _shoulderR;
        private float _legLength, _armLength, _bagDrop, _bagOut, _bobHeight;

        public static AgentFigure Build(Transform parent, string name, in AgentLook look,
                                        MaterialPropertyBlock block)
        {
            float h = look.Height;
            float legLength = h * LegFraction;
            float torsoLength = h * TorsoFraction;
            float headSize = h * HeadFraction * look.HeadScale;
            float shoulders = h * ShoulderFraction * look.Breadth;
            float depth = h * DepthFraction * look.Breadth;
            float legThick = h * LegThickFraction * look.Breadth;
            float armThick = h * ArmThickFraction * look.Breadth;
            float armLength = h * ArmFraction;

            var root = new GameObject(name);
            root.transform.SetParent(parent, false);

            var figure = new AgentFigure
            {
                Root = root.transform,
                _legLength = legLength,
                _armLength = armLength,
                _bagDrop = h * 0.09f,
                // Held a little away from the body, or a bag this size is carried inside the thigh.
                _bagOut = h * 0.06f,
                _bobHeight = h * 0.018f,
            };

            // A stoop is applied once, here, as a rotation of everything above the hip. Doing it
            // by leaning the parts rather than by parenting them to a leaning torso avoids
            // handing the head the torso's very non-uniform scale, which would flatten it.
            var lean = Quaternion.Euler(look.Stoop, 0f, 0f);
            var hip = new Vector3(0f, legLength, 0f);

            // Unity's capsule is 1 unit across and 2 tall before scaling, so Y takes half.
            figure._torsoRenderer = Part(root.transform, "torso", PrimitiveType.Capsule,
                                         new Vector3(shoulders, torsoLength * 0.5f, depth));
            figure._torsoRenderer.transform.SetLocalPositionAndRotation(
                hip + lean * new Vector3(0f, torsoLength * 0.5f, 0f), lean);

            var headAt = hip + lean * new Vector3(0f, torsoLength + h * NeckFraction + headSize * 0.5f,
                                                  headSize * 0.05f);
            var head = Part(root.transform, "head", PrimitiveType.Sphere,
                            new Vector3(headSize * 0.94f, headSize, headSize * 0.94f));
            head.transform.SetLocalPositionAndRotation(headAt, lean);

            // The hair is set back on the skull and left uncovering the face. It is the cue that
            // resolves which way a head is pointed when it is eight pixels tall, and it costs one
            // sphere. It casts no shadow: it sits inside the head's own.
            var hair = Part(root.transform, "hair", PrimitiveType.Sphere,
                            new Vector3(headSize, headSize * 0.86f, headSize * 0.96f));
            hair.transform.SetLocalPositionAndRotation(
                headAt + lean * new Vector3(0f, headSize * 0.06f, -headSize * 0.20f), lean);
            hair.shadowCastingMode = ShadowCastingMode.Off;

            // +X is the figure's right, given that +Z is the way it faces.
            figure._hipL = new Vector3(-legThick * 0.62f, legLength, 0f);
            figure._hipR = new Vector3(legThick * 0.62f, legLength, 0f);

            // Shoulders sit at three quarters of the torso, which is where a Unity capsule stops
            // being a cylinder and starts rounding off - any higher and the arms hang off nothing.
            float armOut = shoulders * 0.5f + armThick * 0.05f;
            figure._shoulderL = hip + lean * new Vector3(-armOut, torsoLength * 0.75f, 0f);
            figure._shoulderR = hip + lean * new Vector3(armOut, torsoLength * 0.75f, 0f);

            figure._legLRenderer = Part(root.transform, "leg.l", PrimitiveType.Capsule,
                                        new Vector3(legThick, legLength * 0.5f, legThick * 1.05f));
            figure._legRRenderer = Part(root.transform, "leg.r", PrimitiveType.Capsule,
                                        new Vector3(legThick, legLength * 0.5f, legThick * 1.05f));
            figure._armLRenderer = Part(root.transform, "arm.l", PrimitiveType.Capsule,
                                        new Vector3(armThick, armLength * 0.5f, armThick));
            figure._armRRenderer = Part(root.transform, "arm.r", PrimitiveType.Capsule,
                                        new Vector3(armThick, armLength * 0.5f, armThick));

            figure._legL = figure._legLRenderer.transform;
            figure._legR = figure._legRRenderer.transform;
            figure._armL = figure._armLRenderer.transform;
            figure._armR = figure._armRRenderer.transform;

            // A bag each, parked inactive. Cheaper than creating and destroying them as journeys
            // begin and end, and there are only a hundred of them.
            var bag = Part(root.transform, "bag", PrimitiveType.Cube,
                           new Vector3(h * 0.155f, h * 0.175f, h * 0.115f));
            figure._bag = bag.transform;
            figure._bagObject = bag.gameObject;
            Paint(bag, block, look.Bag);
            figure._bagObject.SetActive(false);

            Paint(head, block, look.Skin);
            Paint(hair, block, look.Hair);
            figure.SetClothing(block, look.Top, look.Bottom);

            figure.Pose(Vector3.zero, 0f, look.Phase, 0f, false);
            return figure;
        }

        /// <summary>
        /// Recolour the clothed parts. Called at build, and again only when the selection moves -
        /// a hundred property blocks a frame for a colour that changes twice a minute is waste.
        /// </summary>
        public void SetClothing(MaterialPropertyBlock block, Color top, Color bottom)
        {
            Paint(_torsoRenderer, block, top);
            Paint(_armLRenderer, block, top);
            Paint(_armRRenderer, block, top);
            Paint(_legLRenderer, block, bottom);
            Paint(_legRRenderer, block, bottom);
        }

        /// <summary>
        /// Put the figure somewhere, facing somewhere, that far into a stride.
        /// </summary>
        /// <param name="phase">Position in the stride, in radians. Comes from distance walked.</param>
        /// <param name="swing">Peak leg swing in degrees. Zero stands them still.</param>
        public void Pose(Vector3 ground, float yaw, float phase, float swing, bool carrying)
        {
            float legSwing = swing * Mathf.Sin(phase);

            // The bob runs at twice the stride: the body rises once as each leg passes under it.
            float bob = _bobHeight * (swing * (1f / MaxSwing)) * (1f - Mathf.Cos(phase * 2f)) * 0.5f;

            Root.SetPositionAndRotation(new Vector3(ground.x, ground.y + bob, ground.z),
                                        Quaternion.Euler(0f, yaw, 0f));

            SetLimb(_legL, _hipL, _legLength, legSwing);
            SetLimb(_legR, _hipR, _legLength, -legSwing);

            // Arms counter the legs, and the arm holding the bag stops swinging - which is most
            // of what makes a laden walk look different from an unladen one.
            SetLimb(_armL, _shoulderL, _armLength, -legSwing * 0.62f);
            float rightArm = legSwing * (carrying ? 0.10f : 0.62f);
            SetLimb(_armR, _shoulderR, _armLength, rightArm);

            if (_bagObject.activeSelf != carrying) _bagObject.SetActive(carrying);
            if (carrying)
            {
                var hand = _shoulderR + Quaternion.Euler(rightArm, 0f, 0f)
                                      * new Vector3(0f, -_armLength, 0f);
                _bag.localPosition = new Vector3(hand.x + _bagOut, hand.y - _bagDrop, hand.z);
            }
        }

        /// <summary>A limb hangs from a pivot, so it swings about its top end rather than its middle.</summary>
        private static void SetLimb(Transform limb, Vector3 pivot, float length, float degrees)
        {
            var rotation = Quaternion.Euler(degrees, 0f, 0f);
            limb.SetLocalPositionAndRotation(
                pivot + rotation * new Vector3(0f, -length * 0.5f, 0f), rotation);
        }

        private static MeshRenderer Part(Transform parent, string name, PrimitiveType shape, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            Discard(go.GetComponent<Collider>());

            var renderer = go.GetComponent<MeshRenderer>();
            // One material for every part of every person: colour arrives per-renderer through a
            // property block, which is what keeps a hundred and sixteen figures instanced.
            renderer.sharedMaterial = Materials3D.Agent;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return renderer;
        }

        private static void Paint(MeshRenderer renderer, MaterialPropertyBlock block, Color colour)
        {
            block.Clear();
            block.SetColor(BaseColorId, colour);
            block.SetColor(ColorId, colour);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// Throw away a component. Object.Destroy is a no-op outside play mode - it logs
        /// "Destroy may not be called from edit mode" and leaves the object alone - so anything
        /// that builds the village without pressing Play would keep every collider it asked to
        /// be rid of, one per limb per villager.
        /// </summary>
        private static void Discard(UnityEngine.Object doomed)
        {
            if (doomed == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(doomed);
            else UnityEngine.Object.DestroyImmediate(doomed);
        }
    }
}
