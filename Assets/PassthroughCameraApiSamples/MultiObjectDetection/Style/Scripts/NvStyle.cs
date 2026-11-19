// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only
// Look adapted from Vartovyi (github.com/Skerpsh/vartovyi, MIT): one accent colour, black glass with scanlines,
// panels with two cut corners, HUD brackets, Tektur font (SIL OFL, Resources/NewVision/Tektur-OFL.txt).

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// The "cyberdeck" look shared by everything in the headset and by the stick: colours from one accent (themes),
    /// cut-corner panels, brackets, scanlines, the Tektur font. Theme and glitch level are saved and sent to the stick.
    /// </summary>
    public static class NvStyle
    {
        public struct Theme
        {
            public string Name;
            public Color32 Accent;
        }

        public static readonly Theme[] Themes =
        {
            new() { Name = "ORANGE", Accent = new Color32(255, 156, 0, 255) },
            new() { Name = "CYAN", Accent = new Color32(0, 229, 255, 255) },
            new() { Name = "MAGENTA", Accent = new Color32(255, 40, 190, 255) },
            new() { Name = "VIOLET", Accent = new Color32(165, 95, 255, 255) },
            new() { Name = "BLUE", Accent = new Color32(45, 130, 255, 255) },
            new() { Name = "ICE", Accent = new Color32(205, 225, 255, 255) },
        };

        public static readonly string[] FxNames = { "OFF", "LOW", "HIGH" };

        // Danger and state colours stay apart from the chrome, so no theme is red, yellow or green
        public static readonly Color Background = new Color32(5, 5, 7, 255);
        public static readonly Color Green = new Color32(57, 255, 20, 255);
        public static readonly Color Cyan = new Color32(0, 229, 255, 255);
        public static readonly Color Magenta = new Color32(255, 0, 200, 255);
        public static readonly Color Yellow = new Color32(255, 228, 0, 255);
        public static readonly Color Red = new Color32(255, 32, 56, 255);

        public static Color Accent { get; private set; }
        public static Color Dim { get; private set; }
        public static Color Panel { get; private set; }
        public static Color Muted { get; private set; }
        public static Color Text { get; private set; }
        public static Color Scan { get; private set; }

        /// <summary>Raised after the theme changed; built UI re-applies its colours.</summary>
        public static event Action ThemeChanged;

        private const string ThemeKey = "NewVision.Theme";
        private const string FxKey = "NewVision.Fx";
        private static int s_theme = -1;
        private static Font s_font;
        private static readonly Dictionary<string, Sprite> s_sprites = new();

        public static int ThemeIndex
        {
            get
            {
                EnsureTheme();
                return s_theme;
            }
            set
            {
                s_theme = ((value % Themes.Length) + Themes.Length) % Themes.Length;
                PlayerPrefs.SetInt(ThemeKey, s_theme);
                PlayerPrefs.Save();
                ApplyTheme();
                ThemeChanged?.Invoke();
            }
        }

        /// <summary>Glitches on motion: 0 off, 1 low (only hard knocks), 2 high (any brisk motion).</summary>
        public static int FxLevel
        {
            get => PlayerPrefs.GetInt(FxKey, 2);
            set
            {
                PlayerPrefs.SetInt(FxKey, Mathf.Clamp(value, 0, 2));
                PlayerPrefs.Save();
            }
        }

        public static Font Font
        {
            get
            {
                if (s_font == null)
                {
                    s_font = Resources.Load<Font>("NewVision/Tektur");
                    if (s_font == null)
                    {
                        Debug.LogWarning("[NewVision] Tektur font missing, using the built-in one");
                        s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    }
                }
                return s_font;
            }
        }

        public static void EnsureTheme()
        {
            if (s_theme < 0)
            {
                s_theme = Mathf.Clamp(PlayerPrefs.GetInt(ThemeKey, 0), 0, Themes.Length - 1);
                ApplyTheme();
            }
        }

        // Everything but the accent is the accent mixed with black, grey or white, so any theme stays one family
        private static void ApplyTheme()
        {
            var a = (Color)Themes[s_theme].Accent;
            Accent = a;
            Dim = Mix(a, Color.black, 0.59f);
            Panel = Mix(a, Color.black, 0.88f);
            Muted = Mix(a, new Color(120f / 255f, 120f / 255f, 120f / 255f), 0.6f);
            Text = Mix(a, Color.white, 0.85f);
            Scan = Mix(a, Color.black, 0.93f);
        }

        private static Color Mix(Color a, Color with, float amount) => Color.Lerp(a, with, amount);

        public static Color WithAlpha(Color c, float alpha) => new(c.r, c.g, c.b, alpha);

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // ------------------------------------------------------------------ sprites

        /// <summary>
        /// A panel with the top-left and bottom-right corners cut off, white, 9-sliced. Filled, or an outline
        /// of the given line width. The cut and line are in sprite pixels; scale them with Image.pixelsPerUnitMultiplier.
        /// </summary>
        public static Sprite Chamfer(int cut, bool filled, int line = 2)
        {
            var key = $"chamfer{cut}_{filled}_{line}";
            if (s_sprites.TryGetValue(key, out var cached))
            {
                return cached;
            }
            var size = cut * 2 + 4;
            var texture = NewTexture(size, size);
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // Pixel centre; y up. Inside = in the square minus the two corner triangles
                    var px = x + 0.5f;
                    var py = y + 0.5f;
                    var inside = Coverage(px, py, size, cut);
                    var alpha = inside;
                    if (!filled)
                    {
                        alpha = Mathf.Min(inside, 1f - Coverage(px, py, size, cut, line));
                    }
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var border = cut + 1;
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            s_sprites[key] = sprite;
            return sprite;
        }

        // Anti-aliased coverage of the cut-corner shape, shrunk by 'inset' pixels on every side
        private static float Coverage(float px, float py, int size, int cut, float inset = 0f)
        {
            var min = inset;
            var max = size - inset;
            var edge = Mathf.Min(Mathf.Min(px - min, max - px), Mathf.Min(py - min, max - py));
            var c = cut - inset * 0.414f;
            // top-left corner: x + (size - y) >= cut ; bottom-right: (size - x) + y >= cut
            var topLeft = ((px - min) + (max - py) - c) * 0.7071f;
            var bottomRight = ((max - px) + (py - min) - c) * 0.7071f;
            return Mathf.Clamp01(Mathf.Min(edge, Mathf.Min(topLeft, bottomRight)) + 0.5f);
        }

        /// <summary>Plain white sprite (for Filled images: bars and meters).</summary>
        public static Sprite White
        {
            get
            {
                if (!s_sprites.TryGetValue("white", out var sprite))
                {
                    var texture = NewTexture(4, 4);
                    var pixels = new Color32[16];
                    for (var i = 0; i < pixels.Length; i++)
                    {
                        pixels[i] = new Color32(255, 255, 255, 255);
                    }
                    texture.SetPixels32(pixels);
                    texture.Apply(false, true);
                    sprite = Sprite.Create(texture, new UnityEngine.Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
                    s_sprites["white"] = sprite;
                }
                return sprite;
            }
        }

        /// <summary>One dark line every 'period' pixels; tile it with a RawImage uvRect.</summary>
        public static Texture2D Scanlines(int period = 4)
        {
            var key = $"scan{period}";
            if (s_sprites.TryGetValue(key, out var cached))
            {
                return cached.texture;
            }
            var texture = NewTexture(1, period);
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[period];
            for (var i = 0; i < period; i++)
            {
                pixels[i] = new Color32(255, 255, 255, (byte)(i == 0 ? 255 : 0));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            s_sprites[key] = Sprite.Create(texture, new Rect(0, 0, 1, period), Vector2.zero);
            return texture;
        }

        private static Texture2D NewTexture(int w, int h) => new(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        // ------------------------------------------------------------------ builders

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Position from the parent's top-left corner, in canvas units.</summary>
        public static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        public static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        public static Text Label(string name, Transform parent, int size, Color color, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>A cut-corner panel. 'cut' is the corner size in canvas units.</summary>
        public static Image ChamferImage(string name, Transform parent, float cut, Color color, bool filled = true, float line = 2f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            const int spriteCut = 16;
            var spriteLine = filled ? 2 : Mathf.Max(1, Mathf.RoundToInt(line * spriteCut / Mathf.Max(1f, cut)));
            image.sprite = Chamfer(spriteCut, filled, spriteLine);
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = spriteCut / Mathf.Max(1f, cut);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Image Solid(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Black glass with scanlines filling the parent.</summary>
        public static RawImage ScanlineOverlay(Transform parent, float linesEvery, float height)
        {
            var go = new GameObject("Scanlines", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var raw = go.GetComponent<RawImage>();
            raw.texture = Scanlines();
            raw.color = Scan;
            raw.uvRect = new UnityEngine.Rect(0f, 0f, 1f, height / linesEvery);
            raw.raycastTarget = false;
            Stretch(raw.rectTransform);
            return raw;
        }

        public static NvBrackets Brackets(string name, Transform parent, Color color, float length, float thickness)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(NvBrackets));
            go.transform.SetParent(parent, false);
            var brackets = go.GetComponent<NvBrackets>();
            brackets.color = color;
            brackets.Length = length;
            brackets.Thickness = thickness;
            brackets.raycastTarget = false;
            return brackets;
        }

        /// <summary>Letters not yet decoded show as noise, resolving left to right (Vartovyi's menu effect).</summary>
        public static string Decode(string text, float progress)
        {
            const string noise = "#$%&*+<>/|=01ABCDEFXZ";
            if (progress >= 1f || string.IsNullOrEmpty(text))
            {
                return text;
            }
            var shown = (int)(text.Length * Mathf.Clamp01(progress));
            var chars = text.ToCharArray();
            for (var i = shown; i < chars.Length; i++)
            {
                if (chars[i] != ' ')
                {
                    chars[i] = noise[UnityEngine.Random.Range(0, noise.Length)];
                }
            }
            return new string(chars);
        }
    }
}
