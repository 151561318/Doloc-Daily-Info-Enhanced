using System;
using UnityEngine;
using UnityEngine.UI;

namespace SomeOptimizations
{
    /// <summary>
    /// A canvas the mod can draw into on its own. In game the HUD canvas is available, but the title
    /// screen has no <c>DolocAPI.uiSystem</c>, so the editor has to borrow (or create) an overlay.
    /// </summary>
    internal static class UiRoot
    {
        private static RectTransform s_root;
        private static Canvas s_created;

        internal static RectTransform Overlay()
        {
            if (s_root != null)
            {
                return s_root;
            }

            try
            {
                if (DolocAPI.uiSystem != null && DolocAPI.uiSystem.rectTransform != null)
                {
                    s_root = DolocAPI.uiSystem.rectTransform;
                    return s_root;
                }
            }
            catch
            {
                // fall through to the title-screen overlay
            }

            try
            {
                Canvas best = null;
                Canvas[] canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
                for (int i = 0; i < canvases.Length; i++)
                {
                    Canvas canvas = canvases[i];
                    if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    {
                        continue;
                    }

                    if (best == null || canvas.sortingOrder > best.sortingOrder)
                    {
                        best = canvas;
                    }
                }

                if (best != null)
                {
                    s_root = (RectTransform)best.transform;
                    return s_root;
                }

                GameObject host = new GameObject("SomeOptCanvas", typeof(RectTransform));
                Canvas created = host.AddComponent<Canvas>();
                created.renderMode = RenderMode.ScreenSpaceOverlay;
                created.sortingOrder = 32000;
                CanvasScaler scaler = host.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
                host.AddComponent<GraphicRaycaster>();
                UnityEngine.Object.DontDestroyOnLoad(host);
                s_created = created;
                s_root = (RectTransform)host.transform;
                Log.Info("ui root: created fallback canvas");
                return s_root;
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "UiRoot.Overlay");
                return null;
            }
        }

        /// <summary>Forgets the cached root (canvas changed between title screen and game).</summary>
        internal static void Invalidate()
        {
            s_root = null;
        }

        internal static void DestroyCreated()
        {
            if (s_created != null)
            {
                UnityEngine.Object.Destroy(s_created.gameObject);
                s_created = null;
                s_root = null;
            }
        }
    }
}
