using System;
using System.Collections.Generic;
using System.Text;
using DolocTown.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SomeOptimizations
{
    /// <summary>
    /// Temporary field diagnostics. Kept behind <see cref="Enabled"/> so a release build only pays
    /// for one branch; every line it writes is prefixed with "diag" for easy grepping.
    /// </summary>
    internal static class Diagnostics
    {
        internal static bool Enabled = false;

        private static string s_signature = string.Empty;

        internal static void Report(List<Badge> badges, TimeTip tip, RectTransform row)
        {
            if (!Enabled)
            {
                return;
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < badges.Count; i++)
            {
                sb.Append(badges[i].Kind).Append(':').Append(badges[i].Key).Append(' ');
            }

            string signature = sb.ToString();
            if (signature == s_signature)
            {
                return;
            }

            s_signature = signature;

            Log.Info("diag badges(" + badges.Count + "): " + signature);

            try
            {
                if (tip != null && tip.rectTransform != null)
                {
                    RectTransform r = tip.rectTransform;
                    Vector3 centre = r.TransformPoint(r.rect.center);
                    Vector3 left = r.TransformPoint(new Vector3(r.rect.xMin, r.rect.center.y, 0f));
                    Vector3 right = r.TransformPoint(new Vector3(r.rect.xMax, r.rect.center.y, 0f));
                    Log.Info(string.Format(
                        "diag tip name={0} parent={1} rect={2} centre=({3:F1},{4:F1}) left={5:F1} right={6:F1} row={7}",
                        r.name, r.parent != null ? r.parent.name : "-",
                        r.rect, centre.x, centre.y, left.x, right.x,
                        row != null ? row.position.ToString() : "null"));

                    Transform parent = r.parent;
                    if (parent != null)
                    {
                        for (int i = 0; i < parent.childCount; i++)
                        {
                            RectTransform child = parent.GetChild(i) as RectTransform;
                            if (child == null)
                            {
                                continue;
                            }

                            Vector3 l = child.TransformPoint(new Vector3(child.rect.xMin, child.rect.center.y, 0f));
                            Vector3 rr = child.TransformPoint(new Vector3(child.rect.xMax, child.rect.center.y, 0f));
                            Log.Info(string.Format("diag sibling {0} left={1:F1} right={2:F1} y={3:F1}",
                                child.name, l.x, rr.x, child.position.y));
                        }
                    }

                    RectTransform[] kids = r.GetComponentsInChildren<RectTransform>(true);
                    for (int i = 0; i < kids.Length && i < 8; i++)
                    {
                        RectTransform child = kids[i];
                        Vector3 l = child.TransformPoint(new Vector3(child.rect.xMin, child.rect.center.y, 0f));
                        Vector3 rr = child.TransformPoint(new Vector3(child.rect.xMax, child.rect.center.y, 0f));
                        Log.Info(string.Format("diag tipchild {0} rect={1} left={2:F1} right={3:F1} y={4:F1} active={5}",
                            child.name, child.rect, l.x, rr.x, child.position.y, child.gameObject.activeSelf));

                        Text label = child.GetComponent<Text>();
                        if (label != null)
                        {
                            Log.Info(string.Format("diag label '{0}' align={1} preferred={2:F1} textWidth={3:F1}",
                                label.text, label.alignment, label.preferredWidth, label.rectTransform.rect.width));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "Diagnostics tip");
            }

            Log.Info("diag " + CropScan.DebugSummary());
            Log.Info("diag " + Reminders.DebugMerchants());
        }
    }
}


