using System.Collections.Generic;

namespace SomeOptimizations
{
    /// <summary>
    /// Per-seed pixel offsets baked from the in-game calibration pass; each one nudges a crop
    /// icon after the global Layout placement so every sprite sits the same way in its cell.
    /// </summary>
    internal static class CropDefaults
    {
        internal static readonly Dictionary<string, UnityEngine.Vector2> Offsets =
            new Dictionary<string, UnityEngine.Vector2>
            {
                { "seed_agave", new UnityEngine.Vector2(0f, 5f) },
                { "seed_alfalfa", new UnityEngine.Vector2(-5f, 1f) },
                { "seed_appletree", new UnityEngine.Vector2(0f, 7f) },
                { "seed_beer_flower", new UnityEngine.Vector2(0f, 1f) },
                { "seed_chinese_cabbage", new UnityEngine.Vector2(0f, 1f) },
                { "seed_coffee", new UnityEngine.Vector2(0f, 4f) },
                { "seed_grape", new UnityEngine.Vector2(1f, 0f) },
                { "seed_paddy", new UnityEngine.Vector2(-1f, -3f) },
                { "seed_pepper", new UnityEngine.Vector2(-3f, -1f) },
                { "seed_pumpkin", new UnityEngine.Vector2(-2f, 0f) },
                { "seed_scallion", new UnityEngine.Vector2(0f, 1f) },
                { "seed_strawberry", new UnityEngine.Vector2(0f, 1f) },
                { "seed_tomato", new UnityEngine.Vector2(0f, -4f) },
                { "seed_watermelon", new UnityEngine.Vector2(0f, -2f) },
                { "seed_wheat", new UnityEngine.Vector2(-4f, 0f) },
            };
    }
}
