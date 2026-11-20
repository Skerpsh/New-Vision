// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Object memory: remembers where each kind of object was last seen (from the confirmed detections, a few places per
    /// class) and leads you back to one chosen on the wrist panel (GENERAL > FIND): HUD brackets on the spot, a chevron at
    /// the edge of the view while it's behind you, and when the detector sees it again: FOUND, the marker turns green and the
    /// stick shows it (no beep). While searching, the marker and the chevron blink faster the closer you get ("hot-cold").
    /// Memory survives restarts: places are stored relative to a Meta spatial anchor saved on the headset, and in
    /// persistentDataPath/object_memory.json. On launch the saved anchors are localized again; the places of a room the
    /// headset can't find right now stay in the file until it can.
    /// </summary>
    public class ObjectMemory : MonoBehaviour
    {
        public class Spot
        {
            public string Name;
            public float LastSeen;
            public float Score;

            /// <summary>The spatial anchor the place is stored relative to (null until one is ready).</summary>
            public OVRSpatialAnchor Anchor;
            public Vector3 Local;

            public Vector3 Position
            {
                get => Anchor != null ? Anchor.transform.TransformPoint(Local) : Local;
                set => Local = Anchor != null ? Anchor.transform.InverseTransformPoint(value) : value;
            }
        }

        [Serializable]
        private class SavedSpot
        {
            public string name;
            public string anchor;
            public float x, y, z;
            public long seen;   // unix seconds
            public float score;
        }

        [Serializable]
        private class SavedMemory
        {
            public int version = 1;
            public string home;   // the anchor new places go to, kept even with no places on it
            public List<SavedSpot> spots = new();
        }

        private const float RecordInterval = 0.3f;
        private const float MergeRadius = 0.6f;
        private const int MaxPerClass = 3;
        private const int MaxSpots = 80;
        private const float FoundViewAngle = 30f;
        private const float ArrowAngle = 22f;
        private const float FoundHoldSeconds = 3f;
        private const float MarkerUnits = 100f;
        private const float MarkerAngleDeg = 4f;

        // Hot-cold: blink rate by distance to the remembered place (log scale between the two)
        private const float BlinkNear = 0.5f;
        private const float BlinkFar = 8f;
        private const float BlinkFastHz = 6f;
        private const float BlinkSlowHz = 0.7f;
        private const float BlinkDimAlpha = 0.12f;

        // Persistence
        private const string FileName = "object_memory.json";
        private const float SaveInterval = 10f;
        private const float LocalizeTimeout = 15f;
        private const long KeepSeconds = 30L * 24 * 3600;

        private readonly List<Spot> m_spots = new();
        private readonly List<Spot> m_sorted = new();
        private SentisInferenceUiManager m_ui;
        private Transform m_head;
        private StickClient m_stick;
        private float m_nextRecord;

        private float m_foundAt = -10f;
        private float m_blinkPhase;

        // Persistence: the anchor new places are stored against, and saved places whose anchor isn't localized
        private OVRSpatialAnchor m_home;
        private readonly List<SavedSpot> m_pending = new();
        private bool m_loaded;
        private bool m_dirty;
        private float m_nextSave;
        private string m_path;

        // HUD
        private RectTransform m_marker;
        private NvBrackets m_markerBrackets;
        private Image m_markerTag;
        private Text m_markerText;
        private RectTransform m_arrow;
        private Image[] m_chevron;
        private Text m_arrowText;
        private Vector2 m_arrowDir = Vector2.right;

        /// <summary>What is being searched for, or null.</summary>
        public Spot Target { get; private set; }

        /// <summary>The searched object was seen again (the panel shows a message).</summary>
        public event Action<Spot> Found;

        public bool TargetFound => Target != null && Time.time - m_foundAt < FoundHoldSeconds;

        public int Count => m_spots.Count;

        /// <summary>Confirmed detections tracked right now (shown or not).</summary>
        public int InView { get; private set; }

        public void Init(SentisInferenceUiManager ui, Transform head, StickClient stick)
        {
            m_ui = ui;
            m_head = head;
            m_stick = stick;
            NvStyle.EnsureTheme();
            BuildHud();
            m_path = Path.Combine(Application.persistentDataPath, FileName);
            Restore();
        }

        /// <summary>Remembered places, the most recently seen first.</summary>
        public IReadOnlyList<Spot> Sorted()
        {
            m_sorted.Clear();
            m_sorted.AddRange(m_spots);
            m_sorted.Sort((a, b) => b.LastSeen.CompareTo(a.LastSeen));
            return m_sorted;
        }

        public void StartFind(Spot spot)
        {
            Target = spot;
            m_foundAt = -10f;
            Debug.Log($"[NewVision] memory: finding {spot.Name} at {spot.Position}");
        }

        public void StopFind() => Target = null;

        public void Clear()
        {
            var waiting = new HashSet<Guid>();
            foreach (var saved in m_pending)
            {
                if (Guid.TryParse(saved.anchor, out var uuid))
                {
                    _ = waiting.Add(uuid);
                }
            }
            m_spots.Clear();
            m_pending.Clear();
            Target = null;
            m_dirty = true;
            Save();
            EraseUnusedAnchors(waiting, true);
        }

        public float DistanceTo(Spot spot) => m_head != null ? Vector3.Distance(m_head.position, spot.Position) : 0f;

        public static string Age(Spot spot)
        {
            var seconds = Time.time - spot.LastSeen;
            return seconds < 10f ? "NOW" : seconds < 60f ? $"{seconds:0}S" : seconds < 3600f ? $"{seconds / 60f:0}M" :
                seconds < 86400f ? $"{seconds / 3600f:0}H" : $"{seconds / 86400f:0}D";
        }

        private void Update()
        {
            if (m_ui == null || m_head == null)
            {
                return;
            }
            if (Time.time >= m_nextRecord)
            {
                m_nextRecord = Time.time + RecordInterval;
                Record();
            }
            Guide();
            if (m_dirty && m_loaded && Time.time >= m_nextSave)
            {
                Save();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                Save();
            }
        }

        private void OnApplicationQuit() => Save();

        // ------------------------------------------------------------------ memory

        private void Record()
        {
            var inView = 0;
            foreach (var box in m_ui.m_boxDrawn)
            {
                if (!box.Confirmed || string.IsNullOrEmpty(box.ClassName))
                {
                    continue;
                }
                inView++;
                var position = box.TargetPosition;
                Spot same = null;
                var count = 0;
                Spot oldest = null;
                foreach (var spot in m_spots)
                {
                    if (spot.Name != box.ClassName)
                    {
                        continue;
                    }
                    count++;
                    if (oldest == null || spot.LastSeen < oldest.LastSeen)
                    {
                        oldest = spot;
                    }
                    if (same == null && Vector3.Distance(spot.Position, position) < MergeRadius)
                    {
                        same = spot;
                    }
                }
                if (same != null)
                {
                    same.Position = Vector3.Lerp(same.Position, position, 0.5f);
                    same.LastSeen = Time.time;
                    same.Score = Mathf.Max(same.Score * 0.9f, box.Score);
                    m_dirty = true;
                    continue;
                }
                if (count >= MaxPerClass && oldest != null)
                {
                    _ = m_spots.Remove(oldest);
                }
                var added = new Spot { Name = box.ClassName, Anchor = m_home, LastSeen = Time.time, Score = box.Score };
                added.Position = position;
                m_spots.Add(added);
                m_dirty = true;
            }
            InView = inView;
            while (m_spots.Count > MaxSpots)
            {
                var oldest = m_spots[0];
                foreach (var spot in m_spots)
                {
                    if (spot.LastSeen < oldest.LastSeen && spot != Target)
                    {
                        oldest = spot;
                    }
                }
                _ = m_spots.Remove(oldest);
            }
            CheckFound();
        }

        /// <summary>Found when the detector sees the searched kind of object in front of you again (it may have been moved).</summary>
        private void CheckFound()
        {
            if (Target == null || TargetFound)
            {
                return;
            }
            foreach (var box in m_ui.m_boxDrawn)
            {
                if (!box.Confirmed || box.ClassName != Target.Name)
                {
                    continue;
                }
                var toBox = box.TargetPosition - m_head.position;
                if (Vector3.Angle(m_head.forward, toBox) > FoundViewAngle)
                {
                    continue;
                }
                Target.Position = box.TargetPosition;
                Target.LastSeen = Time.time;
                m_foundAt = Time.time;
                Debug.Log($"[NewVision] memory: found {Target.Name}");
                m_dirty = true;
                if (m_stick != null && m_stick.Connected)
                {
                    _ = m_stick.Send("LOG FOUND " + Target.Name.ToUpperInvariant());   // no beep: the search is visual only
                }
                Found?.Invoke(Target);
                return;
            }
        }

        // ------------------------------------------------------------------ persistence (spatial anchors)

        /// <summary>Reads the saved places, localizes their anchors and picks (or creates) the anchor for new places.</summary>
        private async void Restore()
        {
            try
            {
                var saved = Load();
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var uuids = new HashSet<Guid>();
                var expired = new HashSet<Guid>();
                if (Guid.TryParse(saved.home, out var home))
                {
                    _ = uuids.Add(home);
                }
                foreach (var spot in saved.spots)
                {
                    if (!Guid.TryParse(spot.anchor, out var uuid))
                    {
                        continue;
                    }
                    if (now - spot.seen < KeepSeconds)
                    {
                        m_pending.Add(spot);
                        _ = uuids.Add(uuid);
                    }
                    else
                    {
                        _ = expired.Add(uuid);
                    }
                }
                expired.ExceptWith(uuids);
                if (expired.Count > 0)
                {
                    EraseUnusedAnchors(expired, false);   // a month unseen: forget the place and its anchor
                }
                Debug.Log($"[NewVision] memory: {m_pending.Count} saved places, {uuids.Count} anchors");

                if (uuids.Count > 0)
                {
                    var unbound = new List<OVRSpatialAnchor.UnboundAnchor>();
                    var load = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(uuids, unbound);
                    if (!load.Success)
                    {
                        Debug.LogWarning($"[NewVision] memory: loading anchors failed {load.Status}");
                    }
                    foreach (var anchor in unbound)
                    {
                        var localized = anchor.Localized || await anchor.LocalizeAsync(LocalizeTimeout);
                        if (!localized || this == null)
                        {
                            Debug.Log($"[NewVision] memory: anchor {anchor.Uuid} not here, its places wait");
                            continue;
                        }
                        var bound = NewAnchorObject("MemoryAnchor").AddComponent<OVRSpatialAnchor>();
                        anchor.BindTo(bound);
                        RestoreFrom(bound);
                    }
                }

                if (this == null)
                {
                    return;
                }
                if (m_home == null)
                {
                    m_home = await CreateHome();
                }
                if (m_home != null)
                {
                    // Places seen before the anchor was ready move onto it
                    foreach (var spot in m_spots)
                    {
                        if (spot.Anchor == null)
                        {
                            var world = spot.Position;
                            spot.Anchor = m_home;
                            spot.Position = world;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NewVision] memory: restore failed: {e.Message}");
            }
            m_loaded = true;
            m_dirty = true;
        }

        /// <summary>Brings back the saved places of one localized anchor.</summary>
        private void RestoreFrom(OVRSpatialAnchor anchor)
        {
            var id = anchor.Uuid.ToString();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var restored = 0;
            for (var i = m_pending.Count - 1; i >= 0; i--)
            {
                var saved = m_pending[i];
                if (saved.anchor != id)
                {
                    continue;
                }
                m_pending.RemoveAt(i);
                m_spots.Add(new Spot
                {
                    Name = saved.name,
                    Anchor = anchor,
                    Local = new Vector3(saved.x, saved.y, saved.z),
                    LastSeen = Time.time - (now - saved.seen),
                    Score = saved.score
                });
                restored++;
            }
            // The closest localized anchor keeps the new places, so one room keeps one anchor
            if (m_home == null || Vector3.Distance(anchor.transform.position, m_head.position) <
                Vector3.Distance(m_home.transform.position, m_head.position))
            {
                m_home = anchor;
            }
            Debug.Log($"[NewVision] memory: anchor {id} localized, {restored} places back");
        }

        private async OVRTask<OVRSpatialAnchor> CreateHome()
        {
            var go = NewAnchorObject("MemoryAnchor");
            var forward = Vector3.ProjectOnPlane(m_head.forward, Vector3.up);
            go.transform.SetPositionAndRotation(m_head.position,
                Quaternion.LookRotation(forward.sqrMagnitude > 1e-4f ? forward : Vector3.forward, Vector3.up));
            var anchor = go.AddComponent<OVRSpatialAnchor>();
            if (!await anchor.WhenLocalizedAsync() || anchor == null)
            {
                Debug.LogWarning("[NewVision] memory: anchor creation failed, places last for this session only");
                return null;
            }
            var save = await anchor.SaveAnchorAsync();
            if (!save.Success)
            {
                Debug.LogWarning($"[NewVision] memory: saving the anchor failed {save.Status} (USE_ANCHOR_API permission?)");
                return null;
            }
            Debug.Log($"[NewVision] memory: new anchor {anchor.Uuid}");
            return anchor;
        }

        private GameObject NewAnchorObject(string name)
        {
            // Not under this object: an anchor must not inherit any parent transform
            var go = new GameObject(name);
            go.transform.SetParent(null, false);
            return go;
        }

        private SavedMemory Load()
        {
            try
            {
                if (File.Exists(m_path))
                {
                    return JsonUtility.FromJson<SavedMemory>(File.ReadAllText(m_path)) ?? new SavedMemory();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NewVision] memory: can't read {FileName}: {e.Message}");
            }
            return new SavedMemory();
        }

        private void Save()
        {
            if (!m_loaded || string.IsNullOrEmpty(m_path))
            {
                return;   // not before the saved places are read back, or they'd be overwritten
            }
            m_dirty = false;
            m_nextSave = Time.time + SaveInterval;
            var data = new SavedMemory { home = m_home != null && m_home.Created ? m_home.Uuid.ToString() : null };
            data.spots.AddRange(m_pending);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var spot in m_spots)
            {
                if (spot.Anchor == null || !spot.Anchor.Created)
                {
                    continue;   // no anchor yet: can't be found again after a restart
                }
                data.spots.Add(new SavedSpot
                {
                    name = spot.Name,
                    anchor = spot.Anchor.Uuid.ToString(),
                    x = spot.Local.x,
                    y = spot.Local.y,
                    z = spot.Local.z,
                    seen = now - (long)Mathf.Max(0f, Time.time - spot.LastSeen),
                    score = spot.Score
                });
            }
            try
            {
                var temp = m_path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(data));
                if (File.Exists(m_path))
                {
                    File.Delete(m_path);
                }
                File.Move(temp, m_path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NewVision] memory: can't write {FileName}: {e.Message}");
            }
        }

        /// <summary>Erases saved anchors no place needs: the loaded ones except the current one (after CLEAR) and the given ids.</summary>
        private async void EraseUnusedAnchors(HashSet<Guid> uuids, bool loadedToo)
        {
            var anchors = new List<OVRSpatialAnchor>();
            foreach (var anchor in loadedToo ? FindObjectsByType<OVRSpatialAnchor>(FindObjectsSortMode.None) : Array.Empty<OVRSpatialAnchor>())
            {
                if (anchor != m_home && anchor.name == "MemoryAnchor")
                {
                    anchors.Add(anchor);
                }
            }
            if (anchors.Count == 0 && uuids.Count == 0)
            {
                return;
            }
            try
            {
                var result = await OVRSpatialAnchor.EraseAnchorsAsync(anchors, uuids);
                Debug.Log($"[NewVision] memory: erased {anchors.Count + uuids.Count} anchors: {result.Status}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NewVision] memory: erasing anchors failed: {e.Message}");
            }
            foreach (var anchor in anchors)
            {
                if (anchor != null)
                {
                    Destroy(anchor.gameObject);
                }
            }
        }

        // ------------------------------------------------------------------ guidance HUD

        private void Guide()
        {
            if (Target != null && TargetFound && Time.time - m_foundAt >= FoundHoldSeconds - 0.05f)
            {
                Target = null;   // shown as found for a moment, then the search ends
            }
            if (Target == null)
            {
                SetActive(m_marker, false);
                SetActive(m_arrow, false);
                return;
            }

            var color = TargetFound ? NvStyle.Green : NvStyle.Accent;
            var toTarget = Target.Position - m_head.position;
            var distance = toTarget.magnitude;
            var label = $"{Target.Name.ToUpperInvariant()} // {(TargetFound ? "FOUND" : distance.ToString("0.0") + "M")}";

            // Hot-cold: blinks faster the closer you are, steady once found
            var heat = Mathf.InverseLerp(Mathf.Log(BlinkFar), Mathf.Log(BlinkNear), Mathf.Log(Mathf.Max(0.05f, distance)));
            m_blinkPhase = (m_blinkPhase + Mathf.Lerp(BlinkSlowHz, BlinkFastHz, heat) * Time.deltaTime) % 1f;
            var blink = TargetFound || m_blinkPhase < 0.5f ? color : NvStyle.WithAlpha(color, BlinkDimAlpha);

            // Brackets on the remembered spot, the same angular size at any distance
            SetActive(m_marker, true);
            var markerSize = 2f * Mathf.Max(0.3f, distance) * Mathf.Tan(MarkerAngleDeg * 0.5f * Mathf.Deg2Rad);
            m_marker.SetPositionAndRotation(Target.Position, Quaternion.LookRotation(toTarget.sqrMagnitude > 1e-4f ? toTarget : m_head.forward, Vector3.up));
            m_marker.localScale = Vector3.one * (markerSize / MarkerUnits);
            m_markerBrackets.color = blink;
            m_markerTag.color = color;
            m_markerText.text = label;
            m_markerTag.rectTransform.sizeDelta = new Vector2(m_markerText.preferredWidth + 24f, 34f);

            // Chevron at the edge of the view when the spot is off to the side or behind
            var outside = Vector3.Angle(m_head.forward, toTarget) > ArrowAngle;
            SetActive(m_arrow, outside);
            if (!outside)
            {
                return;
            }
            var local = Quaternion.Inverse(m_head.rotation) * toTarget;
            var dir = new Vector2(local.x, local.y);
            if (local.z < 0f && dir.sqrMagnitude < 0.01f)
            {
                dir = Vector2.right;   // straight behind: turn either way
            }
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
            m_arrowDir = Vector2.Lerp(m_arrowDir, dir, 1f - Mathf.Exp(-10f * Time.deltaTime)).normalized;
            const float distanceFromEye = 0.9f;
            const float radius = 0.2f;
            var offset = m_head.right * (m_arrowDir.x * radius) + m_head.up * (m_arrowDir.y * radius);
            var rotation = Quaternion.LookRotation(m_head.forward, m_head.up) *
                           Quaternion.Euler(0f, 0f, Mathf.Atan2(m_arrowDir.y, m_arrowDir.x) * Mathf.Rad2Deg);
            m_arrow.SetPositionAndRotation(m_head.position + m_head.forward * distanceFromEye + offset, rotation);
            m_arrow.localScale = Vector3.one * 0.0009f;
            foreach (var bar in m_chevron)
            {
                bar.color = blink;
            }
            m_arrowText.text = $"{Target.Name.ToUpperInvariant()} {distance:0.0}M";
            m_arrowText.color = color;
            // The text stays upright while the chevron turns
            m_arrowText.rectTransform.localRotation = Quaternion.Inverse(Quaternion.Euler(0f, 0f, Mathf.Atan2(m_arrowDir.y, m_arrowDir.x) * Mathf.Rad2Deg));
        }

        private static void SetActive(Component c, bool active)
        {
            if (c.gameObject.activeSelf != active)
            {
                c.gameObject.SetActive(active);
            }
        }

        private void BuildHud()
        {
            m_marker = NewCanvas("MemoryMarker", 91);
            m_marker.sizeDelta = new Vector2(MarkerUnits, MarkerUnits);
            m_markerBrackets = NvStyle.Brackets("Brackets", m_marker, NvStyle.Accent, 0.3f, 4f);
            m_markerBrackets.OutlineAlpha = 0.25f;
            NvStyle.Stretch(m_markerBrackets.rectTransform);
            m_markerTag = NvStyle.ChamferImage("Tag", m_marker, 8f, NvStyle.Accent);
            var tagRect = m_markerTag.rectTransform;
            tagRect.anchorMin = tagRect.anchorMax = new Vector2(0f, 1f);
            tagRect.pivot = Vector2.zero;
            tagRect.anchoredPosition = new Vector2(0f, 6f);
            m_markerText = NvStyle.Label("Text", tagRect, 22, Color.black);
            NvStyle.Stretch(m_markerText.rectTransform);
            m_markerText.rectTransform.offsetMin = new Vector2(12f, 0f);
            m_marker.gameObject.SetActive(false);

            // Chevron ">" from two bars, pointing along the canvas' +x; the label hangs under it
            m_arrow = NewCanvas("MemoryArrow", 93);
            m_arrow.sizeDelta = new Vector2(120f, 120f);
            m_chevron = new Image[2];
            for (var i = 0; i < 2; i++)
            {
                var bar = NvStyle.Solid("Chevron", m_arrow, NvStyle.Accent);
                bar.rectTransform.sizeDelta = new Vector2(46f, 9f);
                bar.rectTransform.anchoredPosition = new Vector2(0f, i == 0 ? 14f : -14f);
                bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? -40f : 40f);
                m_chevron[i] = bar;
            }
            m_arrowText = NvStyle.Label("Text", m_arrow, 22, NvStyle.Accent, TextAnchor.MiddleCenter);
            m_arrowText.rectTransform.sizeDelta = new Vector2(300f, 30f);
            m_arrowText.rectTransform.anchoredPosition = new Vector2(-70f, 0f);
            m_arrow.gameObject.SetActive(false);
        }

        private RectTransform NewCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = order;
            return (RectTransform)go.transform;
        }
    }
}
