using System;
using UnityEngine;

namespace SomeOptimizations
{
    /// <summary>
    /// The portrait used for the player's own birthday badge.
    ///
    /// The vanilla UI points "player portrait" at <c>icon_character_player</c>, but that texture is
    /// a wide 75x58 framed illustration: dropped into a 66 px badge its border and background
    /// dominate and the character turns to mush. The idle frame of the player's own rig is the same
    /// art the character is drawn with in the world, ships with clean transparent margins and reads
    /// well at badge size, so it is used first and the old texture stays as a fallback.
    /// </summary>
    internal static class PlayerPortrait
    {
        /// <summary>Player rig frame (<c>anim_player_idle_0</c>), picked by the user from the dump.</summary>
        internal const string DefaultSprite = "anim_player_idle_0";

        private const string FallbackSprite = "icon_character_player";

        private static Sprite s_sprite;
        private static string s_resolved;
        private static bool s_probed;

        /// <summary>Name that actually resolved; useful in the editor and in the log.</summary>
        internal static string SpriteName
        {
            get { return string.IsNullOrEmpty(s_resolved) ? DefaultSprite : s_resolved; }
        }

        internal static Sprite Icon()
        {
            if (s_probed)
            {
                return s_sprite;
            }

            s_probed = true;
            s_sprite = TryLoad(DefaultSprite, out s_resolved);
            if (s_sprite == null)
            {
                s_sprite = TryLoad(FallbackSprite, out s_resolved);
            }

            // The rig frame is 24x30: drawn at badge size it is magnified ~2.6x, and the atlas is
            // imported with bilinear filtering, which turns a pixel-art character into mush. Cut the
            // frame out into its own point-filtered texture so the pixels stay square.
            if (s_sprite != null && s_resolved == DefaultSprite)
            {
                s_sprite = Crisp(s_sprite);
            }

            if (s_sprite == null)
            {
                Log.Warn("player portrait: neither " + DefaultSprite + " nor " + FallbackSprite + " loaded");
            }
            else
            {
                Log.Info("player portrait = " + s_resolved + " (" +
                         s_sprite.rect.width.ToString("0") + "x" + s_sprite.rect.height.ToString("0") + ")");
            }

            return s_sprite;
        }

        private static Sprite TryLoad(string name, out string resolved)
        {
            resolved = null;
            try
            {
                Sprite sprite = DolocAPI.LoadSprite(name);
                if (sprite != null)
                {
                    resolved = name;
                    return sprite;
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "player portrait " + name);
            }

            return null;
        }

        /// <summary>
        /// Extracts the sprite's pixels from the shared atlas into a standalone <see cref="Texture2D"/>
        /// with point filtering. The atlas itself must not be touched (every other icon uses it), and
        /// bilinear magnification of a 24x30 frame is what makes pixel art look smeared.
        /// </summary>
        private static Sprite Crisp(Sprite source)
        {
            try
            {
                Texture2D atlas = source.texture;
                if (atlas == null || source.packed)
                {
                    // A packed sprite may be stored rotated inside the atlas, and textureRect then
                    // describes the un-rotated footprint: hand-cutting it would land on the wrong
                    // pixels. The rig frames are plain spritesheet sub-rects, so bailing out here
                    // costs nothing and keeps the fallback correct for any other sprite.
                    return source;
                }

                Rect rect = source.textureRect;
                int x = Mathf.Clamp(Mathf.RoundToInt(rect.x), 0, Mathf.Max(0, atlas.width - 1));
                int y = Mathf.Clamp(Mathf.RoundToInt(rect.y), 0, Mathf.Max(0, atlas.height - 1));
                int w = Mathf.Clamp(Mathf.RoundToInt(rect.width), 1, atlas.width - x);
                int h = Mathf.Clamp(Mathf.RoundToInt(rect.height), 1, atlas.height - y);

                RenderTexture rt = RenderTexture.GetTemporary(atlas.width, atlas.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                Graphics.Blit(atlas, rt);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;

                Texture2D full = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, false);
                full.ReadPixels(new Rect(0f, 0f, atlas.width, atlas.height), 0, 0);
                full.Apply();

                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);

                Color[] pixels = full.GetPixels(x, y, w, h);
                UnityEngine.Object.Destroy(full);

                // Sanity check: if the rect landed on empty space the character would silently
                // vanish, so fall back to the atlas sprite rather than shipping an invisible badge.
                int opaque = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (pixels[i].a > 0.05f)
                    {
                        opaque++;
                    }
                }

                if (opaque < pixels.Length / 50)
                {
                    Log.Warn("player portrait cut out is empty (" + opaque + "/" + pixels.Length + "), using atlas sprite");
                    return source;
                }

                Texture2D crop = new Texture2D(w, h, TextureFormat.RGBA32, false);
                crop.filterMode = FilterMode.Point;
                crop.wrapMode = TextureWrapMode.Clamp;
                crop.SetPixels(pixels);
                crop.Apply();

                Sprite sprite = Sprite.Create(crop, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect);
                sprite.name = source.name + "_crisp";
                Log.Info("player portrait cut out " + w + "x" + h + " point filtered, ink=" +
                         (opaque * 100 / Mathf.Max(1, pixels.Length)) + "%");
                return sprite;
            }
            catch (Exception ex)
            {
                // Falling back to the atlas sprite only costs sharpness, so it is never fatal.
                Log.Warn("player portrait crisp failed: " + ex.Message);
                return source;
            }
        }
    }
}
