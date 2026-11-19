// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System.Collections;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Capri from capri-desktop, as a flat cut-out floating in the room: she teleports in on the floor in front of you,
    /// always faces you, floats, glitches now and then, cocks her head when you look at her and dozes off when you
    /// ignore her. Drawn by the NewVision/Capri shader (ported from capri-desktop) from one picture, posed on twos
    /// like cel animation: the expensive drawing goes into a render texture only when it changes (12-24 times a second),
    /// the quad in the room just shows it. Every so often she goes to sit on the edge of the bed from Space Setup,
    /// legs kicking, and later comes back. Right thumbstick click: come here; left thumbstick click: go sit on the bed.
    /// Needs Companion/Resources/CapriCutout.bytes (capri-desktop's assets/capri/capri-full.png, not in git).
    /// </summary>
    public class CompanionController : MonoBehaviour
    {
        private const string CutoutResource = "CapriCutout";
        private const string ShaderResource = "NewVisionCapri";
        private const string CanvasShaderResource = "NewVisionCapriCanvas";

        // The picture, in sprite pixels (y down)
        private const float SpriteWidth = 240f;
        private const float SpriteHeight = 420f;
        private const float SpritePad = 56f;
        private static readonly Vector2 s_feet = new(128f, 418f);
        private static readonly Vector2 s_seat = new(128f, 232f); // where she sits: her hips
        private static readonly Vector2 s_face = new(124f, 132f);

        private const float SheetFps = 12f;
        private const float EffectFps = 24f; // teleports and glitches are drawn on ones

        [Header("Placement")]
        [Tooltip("Height of the whole picture, mane included (meters). At 1.7 m her legs hang ~0.55 m: a bed's height.")]
        [SerializeField] private float m_height = 1.7f;
        [Tooltip("How high she floats above the floor (meters).")]
        [SerializeField] private float m_hover = 0.12f;
        [Tooltip("How far in front of you she appears (meters).")]
        [SerializeField] private float m_spawnDistance = 1.6f;
        [Tooltip("Seconds to wait for the depth sensor before falling back to the floor height from the boundary setup.")]
        [SerializeField] private float m_floorSearchSeconds = 1f;
        [Tooltip("Render texture pixels per sprite pixel.")]
        [SerializeField] private float m_canvasScale = 2f;

        [Header("Behaviour")]
        [Tooltip("She notices you looking at her within this angle (degrees).")]
        [SerializeField] private float m_noticeGazeDeg = 10f;
        [Tooltip("Seconds without a look at her before she dozes off.")]
        [SerializeField] private float m_sleepAfter = 180f;
        [Tooltip("Seconds of standing (min, max) before she goes to sit on the bed by herself.")]
        [SerializeField] private Vector2 m_standSeconds = new(25f, 50f);
        [Tooltip("Seconds of sitting (min, max) before she comes back to you, unless she fell asleep there.")]
        [SerializeField] private Vector2 m_sitSeconds = new(60f, 120f);
        [Tooltip("How far in from the edge of the bed she sits (meters).")]
        [SerializeField] private float m_seatInset = 0.08f;

        private Transform m_head;
        private Transform m_trackingSpace;
        private EnvironmentRayCastSampleManager m_raycast;

        private readonly CompanionSeats m_seats = new();

        private Transform m_root;   // on the floor (or the bed), turned to you
        private Transform m_body;   // floats, leans, squashes; pivot at her feet (her hips when sitting)
        private Transform m_quad;
        private Texture2D m_cutout;
        private Material m_material;        // draws her into the canvas
        private RenderTexture m_canvas;
        private Material m_canvasMaterial;  // shows the canvas in the room
        private float m_metersPerPixel;
        private Vector3 m_spot;
        private bool m_redraw;
        private int m_lastEffectDrawing = -1;

        private Act m_stance = Acts.Stand;
        private float m_stanceStart;
        private Act m_act;
        private float m_actStart;
        private int m_actDir;
        private int m_lastDrawing = -1;

        private bool m_asleep;
        private bool m_sitting;
        private bool m_busy;
        private float m_nextMove;   // standing: when she goes to sit; sitting: when she gets up
        private float m_lastNoticed;
        private float m_gazeTime;
        private float m_nextTwitch;
        private float m_nextFlourish;
        private float m_nextCurious;

        // what the whole drawing does, outside the acts
        private float m_boil = 0.9f, m_sparkle = 1f, m_alpha = 1f;
        private float m_glitch, m_glitchSeed, m_split, m_collapse, m_dissolve;

        // ------------------------------------------------------------------ wrist panel controls

        /// <summary>She exists in this build (her picture is in Resources) and has been set up.</summary>
        public bool Available => enabled && m_root != null;

        /// <summary>Shown in the room; hiding keeps her where she is for the next time.</summary>
        public bool Visible => m_root != null && m_root.gameObject.activeSelf;

        /// <summary>Come here: appears on the floor in front of you (also brings her back when hidden).</summary>
        public void Recall()
        {
            if (Available && !m_busy)
            {
                _ = StartCoroutine(Teleport(false));
            }
        }

        /// <summary>Go and sit on the edge of the bed (needs a bed in the room scan).</summary>
        public void Sit()
        {
            if (Available && !m_busy)
            {
                _ = StartCoroutine(Teleport(true));
            }
        }

        public void Hide()
        {
            if (Available && !m_busy)
            {
                m_root.gameObject.SetActive(false);
            }
        }

        private IEnumerator Start()
        {
            var cutout = Resources.Load<TextAsset>(CutoutResource);
            var shader = Resources.Load<Shader>(ShaderResource);
            var canvasShader = Resources.Load<Shader>(CanvasShaderResource);
            if (cutout == null || shader == null || canvasShader == null)
            {
                Debug.LogWarning($"[NewVision] companion: no Resources/{CutoutResource}.bytes or her shaders, companion is off");
                enabled = false;
                yield break;
            }

            var cameraRig = FindAnyObjectByType<OVRCameraRig>();
            m_head = cameraRig != null ? cameraRig.centerEyeAnchor : Camera.main != null ? Camera.main.transform : null;
            m_trackingSpace = cameraRig != null ? cameraRig.trackingSpace : null;
            m_raycast = FindAnyObjectByType<EnvironmentRayCastSampleManager>();
            if (m_head == null)
            {
                Debug.LogWarning("[NewVision] companion: no camera found, companion is off");
                enabled = false;
                yield break;
            }

            // Linear texture: the shader grades raw sRGB values the way the browser does, then converts
            m_cutout = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _ = m_cutout.LoadImage(cutout.bytes, true);
            m_material = new Material(shader);
            m_material.SetVector("uSize", new Vector4(SpriteWidth, SpriteHeight));
            m_material.SetFloat("uPad", SpritePad);
            m_material.SetVector("uRim", new Vector4(0.48f, 0.24f, 1f));
            m_material.SetVector("uSil", new Vector4(0.08f, 0.06f, 0.11f, 0f));
            m_material.SetVector("uMisreg", new Vector4(-2.6f, 2.2f));
            m_material.SetFloat("uGrade", 1f);
            m_material.SetFloat("uAlpha", 1f); // fading is done when showing the canvas

            // sRGB canvas: the drawing shader writes linear values, mips keep her clean from across the room
            var view = new Vector2(SpriteWidth + 2f * SpritePad, SpriteHeight + 2f * SpritePad) * m_canvasScale;
            m_canvas = new RenderTexture(Mathf.RoundToInt(view.x), Mathf.RoundToInt(view.y), 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                useMipMap = true,
                autoGenerateMips = true,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                name = "Capri Canvas",
            };
            m_canvasMaterial = new Material(canvasShader) { mainTexture = m_canvas };

            BuildFigure();
            m_seats.Load();

            // Head pose is (0,0,0) for the first frames until tracking starts
            while (m_head.localPosition == Vector3.zero)
            {
                yield return null;
            }
            // She doesn't pop up at launch: she comes when called from the wrist panel (GENERAL > CAPRI)

            m_lastNoticed = Time.time;
            m_nextTwitch = Time.time + Random.Range(4f, 9f);
            m_nextFlourish = Time.time + Random.Range(8f, 16f);
        }

        private void BuildFigure()
        {
            m_metersPerPixel = m_height / SpriteHeight;

            m_root = new GameObject("Companion").transform;
            m_body = new GameObject("Body").transform;
            m_body.SetParent(m_root, false);

            // The quad covers the picture and the padding around it
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Capri";
            Destroy(quad.GetComponent<Collider>());
            m_quad = quad.transform;
            m_quad.SetParent(m_body, false);
            var view = new Vector2(SpriteWidth + 2f * SpritePad, SpriteHeight + 2f * SpritePad);
            m_quad.localScale = new Vector3(view.x, view.y, 1f) * m_metersPerPixel;
            SetPivot(s_feet);
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = m_canvasMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            AddBlobShadow();
            m_root.gameObject.SetActive(false);
        }

        // This point of the picture (sprite pixels) sits on the body's pivot: her feet, or her hips when sitting
        private void SetPivot(Vector2 pivot)
        {
            var center = new Vector2(SpriteWidth, SpriteHeight) / 2f;
            m_quad.localPosition = new Vector3(center.x - pivot.x, pivot.y - center.y, 0f) * m_metersPerPixel;
        }

        private Vector2 Pivot => m_sitting ? s_seat : s_feet;

        // Teleport: find where to land while still there, squeeze into a line of light, move, open up again
        // standing on the floor in front of you, or sitting on the edge of the bed
        private IEnumerator Teleport(bool toBed)
        {
            m_busy = true;
            if (toBed)
            {
                if (!m_seats.TryGetSeat(m_head.position, m_seatInset, out m_spot))
                {
                    m_busy = false;
                    yield break;
                }
                Debug.Log($"[NewVision] companion sits on the bed at {m_spot}");
            }
            else
            {
                yield return FindSpot();
            }
            if (m_root.gameObject.activeSelf)
            {
                yield return Tween(t =>
                {
                    m_collapse = t;
                    m_glitch = 0.3f * t;
                    m_alpha = 1f - t * t;
                }, 0.3f);
            }

            m_root.position = m_spot;
            FaceUser();
            m_sitting = toBed;
            SetPivot(Pivot);
            SetStance(m_sitting ? Acts.Sit : m_asleep ? Acts.Sleep : Acts.Stand);
            m_nextMove = Time.time + (m_sitting ? Random.Range(m_sitSeconds.x, m_sitSeconds.y) : Random.Range(m_standSeconds.x, m_standSeconds.y));
            m_lastDrawing = -1; // land already in the new pose
            m_root.gameObject.SetActive(true);
            m_glitchSeed = Random.value * 100f;
            yield return Tween(t =>
            {
                m_collapse = 0.6f * (1f - t);
                m_dissolve = 0.5f * (1f - t);
                m_glitch = 0f;
                m_alpha = t;
            }, 0.45f);
            m_busy = false;
        }

        private IEnumerator FindSpot()
        {
            var forward = Vector3.ProjectOnPlane(m_head.forward, Vector3.up);
            forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;

            // Floor-level tracking origin: the floor from the boundary setup, usually within a few cm
            var floorY = m_trackingSpace != null ? m_trackingSpace.position.y : m_head.position.y - 1.6f;
            var spot = m_head.position + forward * m_spawnDistance;
            spot.y = floorY;

            // Refine with the depth sensor: aim at the expected spot, accept only hits that really are the floor
            // (a table or a sofa in the way is skipped by trying a bit to the left and right)
            var deadline = Time.time + m_floorSearchSeconds;
            var found = false;
            while (!found && m_raycast != null && Time.time < deadline)
            {
                foreach (var yaw in new[] { 0f, -25f, 25f, -50f, 50f })
                {
                    var target = m_head.position + Quaternion.AngleAxis(yaw, Vector3.up) * forward * m_spawnDistance;
                    target.y = floorY;
                    var hit = m_raycast.Raycast(new Ray(m_head.position, target - m_head.position));
                    if (hit.HasValue && Mathf.Abs(hit.Value.y - floorY) < 0.25f)
                    {
                        spot = hit.Value;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    yield return new WaitForSeconds(0.2f);
                }
            }
            Debug.Log($"[NewVision] companion placed at {spot} ({(found ? "depth" : "boundary floor")})");
            m_spot = spot;
        }

        private void Update()
        {
            if (m_root == null || !m_root.gameObject.activeSelf)
            {
                return;
            }

            FaceUser();
            Behave();
            Draw();
        }

        // A flat cut-out: always turned to you, only around the vertical axis
        private void FaceUser()
        {
            var away = Vector3.ProjectOnPlane(m_root.position - m_head.position, Vector3.up);
            if (away.sqrMagnitude > 0.0001f)
            {
                m_root.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
        }

        private void Behave()
        {
            var now = Time.time;

            // Looking at her face: she notices after a moment, and wakes up if she was asleep
            var face = m_body.TransformPoint(new Vector3(s_face.x - Pivot.x, Pivot.y - s_face.y, 0f) * m_metersPerPixel);
            var lookedAt = Vector3.Angle(m_head.forward, face - m_head.position) < m_noticeGazeDeg;
            m_gazeTime = lookedAt ? m_gazeTime + Time.deltaTime : 0f;
            if (m_gazeTime > 0.6f)
            {
                m_lastNoticed = now;
                if (m_asleep)
                {
                    WakeUp();
                }
                else if (now > m_nextCurious && m_act == null)
                {
                    m_nextCurious = now + Random.Range(6f, 12f);
                    Perform(Acts.Curious, Random.value < 0.5f ? -1 : 1);
                }
            }

            if (!m_asleep && now - m_lastNoticed > m_sleepAfter)
            {
                FallAsleep();
            }

            // After a while standing she goes to sit on the bed (if the room has one), later she comes back;
            // asleep on the bed she stays there until you wake her up
            if (now > m_nextMove && !m_busy && !m_asleep && m_act == null)
            {
                if (m_sitting || m_seats.Count > 0)
                {
                    _ = StartCoroutine(Teleport(!m_sitting));
                }
                else
                {
                    m_nextMove = now + Random.Range(m_standSeconds.x, m_standSeconds.y);
                }
            }

            // a glitch now and then: slices and a chromatic split for two drawings
            if (now > m_nextTwitch && !m_asleep && !m_busy)
            {
                m_nextTwitch = now + Random.Range(5f, 13f);
                _ = StartCoroutine(Twitch());
            }

            if (now > m_nextFlourish && !m_asleep && m_act == null)
            {
                m_nextFlourish = now + Random.Range(10f, 25f);
                if (Random.value < 0.5f)
                {
                    Perform(Acts.Look, 0);
                }
                else
                {
                    Perform(Acts.Curious, Random.value < 0.5f ? -1 : 1);
                }
            }
        }

        private IEnumerator Twitch()
        {
            m_glitchSeed = Random.value * 100f;
            m_glitch = Random.Range(0.15f, 0.3f);
            m_split = 2f;
            yield return new WaitForSeconds(2f / SheetFps);
            m_glitch = 0f;
            m_split = 0f;
        }

        private void FallAsleep()
        {
            m_asleep = true;
            if (!m_sitting)
            {
                SetStance(Acts.Sleep); // on the bed she keeps sitting and dozes (see Draw)
            }
            _ = StartCoroutine(TweenLook(0.55f, 0.35f, 0.4f, 0.9f));
            Debug.Log("[NewVision] companion fell asleep");
        }

        private void WakeUp()
        {
            m_asleep = false;
            SetStance(m_sitting ? Acts.Sit : Acts.Stand);
            m_nextMove = Time.time + Random.Range(m_standSeconds.x, m_standSeconds.y);
            _ = StartCoroutine(TweenLook(1f, 1f, 0.9f, 0.25f));
            Perform(Acts.Flinch, 0);
            m_nextCurious = Time.time + 3f;
        }

        private IEnumerator TweenLook(float alpha, float sparkle, float boil, float seconds)
        {
            float a0 = m_alpha, s0 = m_sparkle, b0 = m_boil;
            yield return Tween(t =>
            {
                m_alpha = Mathf.Lerp(a0, alpha, t);
                m_sparkle = Mathf.Lerp(s0, sparkle, t);
                m_boil = Mathf.Lerp(b0, boil, t);
            }, seconds);
        }

        private static IEnumerator Tween(System.Action<float> set, float seconds)
        {
            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                var k = t / seconds;
                set(k * k * (3f - 2f * k));
                yield return null;
            }
            set(1f);
        }

        private void SetStance(Act stance)
        {
            m_stance = stance;
            m_stanceStart = Time.time;
        }

        private void Perform(Act act, int dir)
        {
            if (m_act != null && m_act.Priority > act.Priority)
            {
                return;
            }
            m_act = act;
            m_actStart = Time.time;
            m_actDir = dir;
        }

        // A new drawing 12 times a second: the stance's cycle underneath, the current act on top.
        // Nothing in between is tweened, that's what makes it read as hand-drawn.
        private void Draw()
        {
            var drawing = Mathf.FloorToInt(Time.time * SheetFps);
            if (drawing != m_lastDrawing)
            {
                m_lastDrawing = drawing;
                var pose = Pose.Neutral;
                m_stance.Sample(Time.time - m_stanceStart, 0)?.ApplyTo(ref pose);
                if (m_act != null)
                {
                    var key = m_act.Sample(Time.time - m_actStart, m_actDir);
                    if (key != null)
                    {
                        key.ApplyTo(ref pose);
                    }
                    else
                    {
                        m_act = null;
                    }
                }
                if (m_asleep && m_sitting && m_act == null)
                {
                    // dozing on the edge of the bed
                    pose.Head = 0.12f;
                    pose.Mane = -0.1f;
                    pose.KickL = pose.KickR = 0.03f;
                    pose.ArmL = pose.ArmR = -0.45f;
                }
                Apply(pose);
                m_redraw = true;
            }

            // Teleports and glitches change between drawings: while one is on, redraw on ones
            if (m_glitch > 0f || m_split > 0f || m_collapse > 0f || m_dissolve > 0f)
            {
                var effectDrawing = Mathf.FloorToInt(Time.time * EffectFps);
                m_redraw |= effectDrawing != m_lastEffectDrawing;
                m_lastEffectDrawing = effectDrawing;
            }

            if (m_redraw)
            {
                m_redraw = false;
                m_material.SetFloat("uBoil", m_boil);
                m_material.SetFloat("uSparkle", m_sparkle);
                m_material.SetFloat("uGlitch", m_glitch);
                m_material.SetFloat("uGlitchSeed", m_glitchSeed);
                m_material.SetFloat("uSplit", m_split);
                m_material.SetFloat("uCollapse", m_collapse);
                m_material.SetFloat("uDissolve", m_dissolve);

                var active = RenderTexture.active;
                RenderTexture.active = m_canvas;
                GL.Clear(false, true, Color.clear);
                RenderTexture.active = active;
                Graphics.Blit(m_cutout, m_canvas, m_material);
            }

            m_canvasMaterial.SetFloat("_Alpha", m_alpha);
        }

        private void Apply(in Pose pose)
        {
            var m = m_material;
            m.SetFloat("uSeed", Random.value * 100f); // a fresh drawing, so the line boils
            m.SetFloat("uHead", pose.Head);
            m.SetFloat("uMane", pose.Mane);
            m.SetFloat("uHair", pose.Hair);
            m.SetFloat("uArmL", pose.ArmL);
            m.SetFloat("uArmR", pose.ArmR);
            m.SetFloat("uLongL", pose.LongL);
            m.SetFloat("uLongR", pose.LongR);
            m.SetFloat("uKickL", pose.KickL);
            m.SetFloat("uKickR", pose.KickR);
            m.SetFloat("uSit", m_sitting ? 1f : 0f);
            m.SetVector("uSmear", pose.Smear);
            m.SetVector("uEcho", pose.Echo);

            // bob is in sprite pixels, negative is up; tilt in degrees, clockwise as seen by you.
            // On the bed she stays seated: no lifting off it, no sinking through it
            var bob = m_sitting ? Mathf.Clamp(pose.Bob, -3f, 2f) : pose.Bob;
            var hover = m_sitting ? 0f : m_hover;
            m_body.localPosition = new Vector3(0f, hover - bob * m_metersPerPixel, 0f);
            m_body.localRotation = Quaternion.Euler(0f, 0f, -pose.Tilt);
            m_body.localScale = new Vector3(pose.Sx, pose.Sy, 1f);
        }

        // Soft dark spot on the floor under her, so she floats above it instead of anywhere in the air
        private void AddBlobShadow()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = new Vector2(x - size / 2 + 0.5f, y - size / 2 + 0.5f).magnitude / (size / 2);
                    var a = Mathf.Clamp01(1f - d);
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)(a * a * 120));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            var shadow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shadow.name = "Blob Shadow";
            Destroy(shadow.GetComponent<Collider>());
            shadow.transform.SetParent(m_root, false);
            shadow.transform.localPosition = new Vector3(0f, 0.005f, 0f);
            shadow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shadow.transform.localScale = new Vector3(0.45f, 0.3f, 1f);
            var renderer = shadow.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = texture };
        }

        private void OnDestroy()
        {
            if (m_root != null)
            {
                Destroy(m_root.gameObject);
            }
            if (m_canvas != null)
            {
                m_canvas.Release();
                Destroy(m_canvas);
            }
        }

        // ---- acting, ported from capri-desktop's acting.js ----

        private struct Pose
        {
            public float Head, Mane, Hair, Tilt, Bob, Sx, Sy, ArmL, ArmR, LongL, LongR, KickL, KickR;
            public Vector2 Smear, Echo;

            public static Pose Neutral => new() { Sx = 1f, Sy = 1f, LongL = 1f, LongR = 1f };
        }

        /// <summary>One drawing of an exposure sheet; fields left null fall back to the stance underneath.</summary>
        private class Key
        {
            public int Hold;
            public float? Head, Mane, Hair, Tilt, Bob, Sx, Sy, Arms, KickL, KickR;
            public Vector2? Smear;
            public int Dir; // set while sampling a directional act: mirrors head, tilt and smear

            public void ApplyTo(ref Pose p)
            {
                var lean = Dir != 0 ? Dir : 1;
                if (Head.HasValue) p.Head = Head.Value * lean;
                if (Mane.HasValue) p.Mane = Mane.Value;
                if (Hair.HasValue) p.Hair = Hair.Value;
                if (Tilt.HasValue) p.Tilt = Tilt.Value * lean;
                if (Bob.HasValue) p.Bob = Bob.Value;
                if (Sx.HasValue) p.Sx = Sx.Value;
                if (Sy.HasValue) p.Sy = Sy.Value;
                if (Arms.HasValue) p.ArmL = p.ArmR = Arms.Value;
                if (KickL.HasValue) p.KickL = KickL.Value;
                if (KickR.HasValue) p.KickR = KickR.Value;
                if (Smear.HasValue) p.Smear = new Vector2(Smear.Value.x * lean, Smear.Value.y);
            }
        }

        private class Act
        {
            public bool Loop;
            public int Priority;
            public Key[] Keys;

            /// <summary>The drawing at this time into the act, or null once a one-shot act is over.</summary>
            public Key Sample(float seconds, int dir)
            {
                var total = 0;
                foreach (var k in Keys)
                {
                    total += k.Hold;
                }
                var f = Mathf.FloorToInt(seconds * SheetFps);
                if (f >= total)
                {
                    if (!Loop)
                    {
                        return null;
                    }
                    f %= total;
                }
                foreach (var k in Keys)
                {
                    if (f < k.Hold)
                    {
                        k.Dir = dir;
                        return k;
                    }
                    f -= k.Hold;
                }
                return null;
            }
        }

        private static class Acts
        {
            // floating: four drawings; the arms lag behind the body (down when she rises, up when she sinks)
            public static readonly Act Stand = new()
            {
                Loop = true,
                Keys = new[]
                {
                    new Key { Hold = 6, Bob = 0, Hair = -2.5f, Arms = 0.04f },
                    new Key { Hold = 4, Bob = -2, Hair = 0, Arms = 0 },
                    new Key { Hold = 6, Bob = -4, Hair = 2.5f, Arms = -0.05f },
                    new Key { Hold = 4, Bob = -2, Hair = 0, Arms = 0 },
                },
            };

            // perched on the edge, hands down on it, legs kicking one after the other
            public static readonly Act Sit = new()
            {
                Loop = true,
                Keys = new[]
                {
                    new Key { Hold = 5, KickL = 0.24f, KickR = -0.03f, Hair = -1.5f, Arms = -0.42f },
                    new Key { Hold = 2, KickL = 0.08f, KickR = 0.08f, Arms = -0.42f },
                    new Key { Hold = 5, KickL = -0.03f, KickR = 0.24f, Hair = 1.5f, Arms = -0.42f },
                    new Key { Hold = 2, KickL = 0.08f, KickR = 0.08f, Arms = -0.42f },
                    new Key { Hold = 5, KickL = 0.24f, KickR = -0.03f, Hair = -1.5f, Arms = -0.42f },
                    new Key { Hold = 2, KickL = 0.08f, KickR = 0.08f, Arms = -0.42f },
                    new Key { Hold = 5, KickL = -0.03f, KickR = 0.24f, Hair = 1.5f, Arms = -0.42f },
                    new Key { Hold = 10, KickL = 0.04f, KickR = 0.04f, Head = 0.08f, Arms = -0.4f }, // a pause, head cocked
                },
            };

            // asleep: arms hanging, head dropped; every so often it falls a bit further and she jolts awake
            public static readonly Act Sleep = new()
            {
                Loop = true,
                Keys = new[]
                {
                    new Key { Hold = 18, Head = 0.1f, Mane = -0.12f, Bob = 2, Arms = -0.3f },
                    new Key { Hold = 1, Head = 0.16f, Mane = -0.13f, Bob = 4, Arms = -0.34f },
                    new Key { Hold = 10, Head = 0.21f, Mane = -0.15f, Bob = 5, Arms = -0.36f },
                    new Key { Hold = 1, Head = 0.06f, Mane = -0.08f, Bob = 0, Smear = new Vector2(0, 8), Arms = -0.18f }, // jolt
                    new Key { Hold = 4, Head = 0.08f, Mane = -0.1f, Bob = 1, Arms = -0.26f },
                },
            };

            // the cocked head: anticipation the other way, a smear, then a long hold
            public static readonly Act Curious = new()
            {
                Priority = 1,
                Keys = new[]
                {
                    new Key { Hold = 1, Head = -0.05f, Mane = 0.04f },
                    new Key { Hold = 1, Head = 0.12f, Mane = 0.15f, Tilt = 1, Smear = new Vector2(-12, 4) },
                    new Key { Hold = 10, Head = 0.19f, Mane = 0.14f, Tilt = 2 },
                    new Key { Hold = 6, Head = 0.16f, Mane = 0.1f, Tilt = 1.6f },
                    new Key { Hold = 1, Head = 0.07f, Tilt = 0.6f },
                },
            };

            // head snapping from one side to the other, held each time
            public static readonly Act Look = new()
            {
                Priority = 1,
                Keys = new[]
                {
                    new Key { Hold = 1, Head = 0.08f, Smear = new Vector2(-8, 0) },
                    new Key { Hold = 8, Head = 0.16f },
                    new Key { Hold = 1, Head = 0 },
                    new Key { Hold = 1, Head = -0.08f, Smear = new Vector2(8, 0) },
                    new Key { Hold = 8, Head = -0.16f },
                    new Key { Hold = 1, Head = -0.05f },
                    new Key { Hold = 3, Head = 0 },
                },
            };

            // startled: thrown back, squashed, then settling
            public static readonly Act Flinch = new()
            {
                Priority = 2,
                Keys = new[]
                {
                    new Key { Hold = 2, Head = 0.22f, Tilt = 4, Sx = 0.92f, Sy = 1.06f, Mane = -0.12f, Bob = 5, Smear = new Vector2(-10, 0), Arms = -0.32f },
                    new Key { Hold = 5, Head = 0.18f, Tilt = 3.4f, Mane = -0.1f, Bob = 4, Arms = -0.22f },
                    new Key { Hold = 1, Head = 0.09f, Tilt = 1.5f, Bob = 2, Arms = -0.08f },
                },
            };
        }
    }
}
