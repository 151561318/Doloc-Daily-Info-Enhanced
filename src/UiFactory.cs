using UnityEngine;
using UnityEngine.UI;

namespace SomeOptimizations
{
    /// <summary>Small procedural sprites so the mod never depends on assets it does not own.</summary>
    internal static class ProcSprites
    {
        private static Sprite s_star;
        private static Sprite s_glow;
        private static Sprite s_square;
        private static Sprite s_frame;
        private static Sprite s_pip;
        private static Sprite s_plate;
        private static Sprite s_heart;

        /// <summary>Fallback heart for when the game's own heart sprite cannot be resolved.</summary>
        internal static Sprite Heart
        {
            get
            {
                if (s_heart == null)
                {
                    s_heart = Build(24, (x, y) =>
                    {
                        // two lobes plus a tapered bottom
                        float dx = Mathf.Abs(x - 0.5f);
                        float dy = y - 0.5f;
                        float lobe = Mathf.Sqrt((dx - 0.18f) * (dx - 0.18f) + (dy + 0.16f) * (dy + 0.16f));
                        float body = Mathf.Abs(dx) * 1.55f + (0.22f - dy);
                        return lobe < 0.24f || body < 0.34f ? 1f : 0f;
                    });
                }

                return s_heart;
            }
        }

        internal static Sprite Star
        {
            get
            {
                if (s_star == null)
                {
                    s_star = Build(24, (x, y) => InStar(x, y) ? 1f : 0f);
                }

                return s_star;
            }
        }

        internal static Sprite Glow
        {
            get
            {
                if (s_glow == null)
                {
                    s_glow = Build(64, (x, y) =>
                    {
                        float dx = x - 0.5f;
                        float dy = y - 0.5f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                        float a = Mathf.Clamp01(1f - d);
                        return a * a;
                    });
                }

                return s_glow;
            }
        }

        internal static Sprite Square
        {
            get
            {
                if (s_square == null)
                {
                    s_square = Build(8, (x, y) => 1f);
                }

                return s_square;
            }
        }

        /// <summary>Hollow 1px frame, used to visualise the icon reference box in demo mode.</summary>
        internal static Sprite Frame
        {
            get
            {
                if (s_frame == null)
                {
                    s_frame = Build(32, (x, y) =>
                    {
                        bool edge = x < 0.04f || x > 0.96f || y < 0.04f || y > 0.96f;
                        return edge ? 1f : 0f;
                    });
                }

                return s_frame;
            }
        }

        internal static Sprite Pip
        {
            get
            {
                if (s_pip == null)
                {
                    s_pip = Build(16, (x, y) =>
                    {
                        float dx = x - 0.5f;
                        float dy = y - 0.5f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                        return Mathf.Clamp01((1f - d) * 4f);
                    });
                }

                return s_pip;
            }
        }

        /// <summary>Rounded square chip used as the badge backdrop.</summary>
        internal static Sprite Plate
        {
            get
            {
                if (s_plate == null)
                {
                    s_plate = Build(48, (x, y) =>
                    {
                        const float radius = 0.28f;
                        float dx = Mathf.Abs(x - 0.5f) - (0.5f - radius);
                        float dy = Mathf.Abs(y - 0.5f) - (0.5f - radius);
                        float ox = Mathf.Max(dx, 0f);
                        float oy = Mathf.Max(dy, 0f);
                        float d = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
                        return Mathf.Clamp01(0.5f - d * 48f);
                    });
                }

                return s_plate;
            }
        }

        private static Sprite Build(int size, System.Func<float, float, float> alpha)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float ax = (x + 0.5f) / size;
                    float ay = (y + 0.5f) / size;
                    float a = Mathf.Clamp01(alpha(ax, ay));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        /// <summary>Classic five pointed star in unit space (0..1), y up.</summary>
        private static bool InStar(float x, float y)
        {
            float dx = x - 0.5f;
            float dy = y - 0.5f;
            float radius = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
            if (radius > 1f)
            {
                return false;
            }

            float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 90f;
            if (angle < 0f)
            {
                angle += 360f;
            }

            float sector = Mathf.Repeat(angle, 72f) / 72f;
            float edge = Mathf.Lerp(1f, 0.44f, Mathf.Abs(sector * 2f - 1f));
            return radius <= edge;
        }
    }

    internal static class UiFactory
    {
        internal static RectTransform Create(Transform parent, string name, Sprite sprite, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.hideFlags = HideFlags.DontSave;
            go.layer = parent != null ? parent.gameObject.layer : 5;

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;

            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            image.preserveAspect = true;
            return rect;
        }

        /// <summary>Creates a small label (used for the "+N" marker) with a dark outline.</summary>
        internal static Text CreateText(Transform parent, string name, Font font, int size, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.hideFlags = HideFlags.DontSave;
            go.layer = parent != null ? parent.gameObject.layer : 5;

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;

            Text text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.2f, -1.2f);

            return text;
        }

        internal static void Size(RectTransform rect, float width, float height)
        {            rect.sizeDelta = new Vector2(width, height);
        }

        internal static void SetSprite(Image image, Sprite sprite)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = sprite;
            image.enabled = sprite != null;
        }
    }

    /// <summary>Sprites shipped inside the dll (the heart the user drew), so no extra file is needed.</summary>
    internal static class ModSprites
    {
        private static Sprite s_heart;
        private static bool s_heartTried;

        internal static Sprite Heart
        {
            get
            {
                if (!s_heartTried)
                {
                    s_heartTried = true;
                    s_heart = Load("SomeOptimizations.heart.png");
                }

                return s_heart;
            }
        }

        private static Sprite Load(string resource)
        {
            try
            {
                System.Reflection.Assembly asm = typeof(ModSprites).Assembly;
                using (System.IO.Stream stream = asm.GetManifestResourceStream(resource))
                {
                    if (stream == null)
                    {
                        Log.Warn("embedded sprite missing: " + resource);
                        return null;
                    }

                    byte[] bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, bytes.Length);

                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    texture.hideFlags = HideFlags.DontSave;
                    if (!texture.LoadImage(bytes))
                    {
                        return null;
                    }

                    texture.filterMode = FilterMode.Point;
                    texture.wrapMode = TextureWrapMode.Clamp;
                    Sprite sprite = Sprite.Create(texture,
                        new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                    sprite.hideFlags = HideFlags.DontSave;
                    return sprite;
                }
            }
            catch (System.Exception ex)
            {
                Log.Warn("embedded sprite load failed: " + ex.Message);
                return null;
            }
        }
    }

    /// <summary>
    /// The game's item / portrait sprites are packed with wide transparent margins, so drawing them in
    /// a fixed box makes each one look a different size. This measures the real ink of a sprite once
    /// and scales/offsets the Image so the visible part fills the requested box.
    /// </summary>
    internal static class IconFit
    {
        private struct Bounds
        {
            internal float Width;
            internal float Height;
            internal float CenterX;
            internal float CenterY;
            internal bool Valid;
        }

        private static readonly System.Collections.Generic.Dictionary<int, Bounds> s_cache =
            new System.Collections.Generic.Dictionary<int, Bounds>();

        private static readonly System.Collections.Generic.Dictionary<int, Texture2D> s_readable =
            new System.Collections.Generic.Dictionary<int, Texture2D>();

        internal enum Align
        {
            Center,
            BottomRight,
            TopRight
        }

        /// <summary>
        /// Fits the sprite's visible pixels into a box of <paramref name="box"/> pixels and aligns
        /// the ink inside that box. Aligning to a corner makes wide sprites share a common edge
        /// instead of drifting away from it because of their transparent margins.
        /// </summary>
        internal static void Fit(Image image, float box, Align align = Align.Center)
        {
            if (image == null || image.sprite == null)
            {
                return;
            }

            Bounds bounds = Measure(image.sprite);
            if (!bounds.Valid)
            {
                image.preserveAspect = false;
                UiFactory.Size(image.rectTransform, box, box);
                image.rectTransform.anchoredPosition = Vector2.zero;
                return;
            }

            float fraction = Mathf.Max(bounds.Width, bounds.Height);
            if (fraction <= 0.01f)
            {
                return;
            }

            float size = box / fraction;
            image.preserveAspect = false;
            UiFactory.Size(image.rectTransform, size, size);

            // Offset from the rect centre to the ink centre; the rect has to move by the negative
            // of it so the ink lands where we want.
            float dx = (0.5f - bounds.CenterX) * size;
            float dy = (0.5f - bounds.CenterY) * size;
            float inkW = bounds.Width * size;
            float inkH = bounds.Height * size;

            Vector2 target;
            switch (align)
            {
                case Align.BottomRight:
                    target = new Vector2(box * 0.5f - inkW * 0.5f, -box * 0.5f + inkH * 0.5f);
                    break;
                case Align.TopRight:
                    target = new Vector2(box * 0.5f - inkW * 0.5f, box * 0.5f - inkH * 0.5f);
                    break;
                default:
                    target = Vector2.zero;
                    break;
            }

            image.rectTransform.anchoredPosition = new Vector2(target.x - dx, target.y - dy);
        }

        private static Bounds Measure(Sprite sprite)
        {
            int id = sprite.GetInstanceID();
            if (s_cache.TryGetValue(id, out Bounds cached))
            {
                return cached;
            }

            Bounds result = default;
            try
            {
                Texture2D texture = Readable(sprite.texture);
                if (texture != null)
                {
                    Rect rect = sprite.textureRect;
                    int x = Mathf.RoundToInt(rect.x);
                    int y = Mathf.RoundToInt(rect.y);
                    int w = Mathf.RoundToInt(rect.width);
                    int h = Mathf.RoundToInt(rect.height);
                    x = Mathf.Clamp(x, 0, Mathf.Max(0, texture.width - 1));
                    y = Mathf.Clamp(y, 0, Mathf.Max(0, texture.height - 1));
                    w = Mathf.Clamp(w, 1, texture.width - x);
                    h = Mathf.Clamp(h, 1, texture.height - y);

                    Color32[] pixels = texture.GetPixels32();
                    int minX = w;
                    int minY = h;
                    int maxX = -1;
                    int maxY = -1;
                    for (int py = 0; py < h; py++)
                    {
                        int row = (y + py) * texture.width;
                        for (int px = 0; px < w; px++)
                        {
                            // Ignore the soft drop shadow many crop sprites carry: counting any
                            // of it made the art sit higher than its box (and looked "floating").
                            if (pixels[row + x + px].a > 150)
                            {
                                if (px < minX) { minX = px; }
                                if (px > maxX) { maxX = px; }
                                if (py < minY) { minY = py; }
                                if (py > maxY) { maxY = py; }
                            }
                        }
                    }

                    if (maxX >= minX && maxY >= minY)
                    {
                        result.Width = (maxX - minX + 1) / (float)w;
                        result.Height = (maxY - minY + 1) / (float)h;
                        result.CenterX = (minX + maxX + 1) * 0.5f / w;
                        result.CenterY = (minY + maxY + 1) * 0.5f / h;
                        result.Valid = true;
                    }

                    if (ModConfig.DemoBadges)
                    {
                        Log.Info("fit " + sprite.name + " tex=" + texture.width + "x" + texture.height +
                                 " rect=" + rect + " ink=" + result.Width.ToString("0.00") + "x" +
                                 result.Height.ToString("0.00") + " c=" + result.CenterX.ToString("0.00") +
                                 "," + result.CenterY.ToString("0.00") + " valid=" + result.Valid);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warn("icon fit failed: " + ex.Message);
            }

            s_cache[id] = result;
            return result;
        }

        private static Texture2D Readable(Texture texture)
        {
            if (texture == null)
            {
                return null;
            }

            int id = texture.GetInstanceID();
            if (s_readable.TryGetValue(id, out Texture2D cached))
            {
                return cached;
            }

            Texture2D copy = null;
            try
            {
                RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                Graphics.Blit(texture, rt);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                copy.Apply();
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
            catch (System.Exception ex)
            {
                Log.Warn("icon texture read failed: " + ex.Message);
                copy = null;
            }

            s_readable[id] = copy;
            return copy;
        }
    }
}
