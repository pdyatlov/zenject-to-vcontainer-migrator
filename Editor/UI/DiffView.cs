using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.UI {
    public static class DiffView {
        public const int DefaultContextLines = 3;
        public const int MaxRenderedLines = 2000;
        public const long LcsCellBudget = 5_000_000L;

        public static string[] BuildUnifiedDiff(string original, string updated) {
            var a = (original ?? "").Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            var b = (updated ?? "").Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            if (a.Length == 1 && a[0] == "") a = System.Array.Empty<string>();
            if (b.Length == 1 && b[0] == "") b = System.Array.Empty<string>();

            // Guard against LCS table blow-up on huge YAML files.
            long cells = (long)(a.Length + 1) * (b.Length + 1);
            if (cells > LcsCellBudget) {
                return new[] { $"(diff suppressed — {a.Length} vs {b.Length} lines exceeds LCS budget)" };
            }

            var lcs = new int[a.Length + 1, b.Length + 1];
            for (int i = a.Length - 1; i >= 0; i--)
                for (int j = b.Length - 1; j >= 0; j--)
                    lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : System.Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

            var lines = new List<string>();
            int x = 0, y = 0;
            while (x < a.Length && y < b.Length) {
                if (a[x] == b[y]) { lines.Add(" " + a[x]); x++; y++; }
                else if (lcs[x + 1, y] >= lcs[x, y + 1]) { lines.Add("-" + a[x]); x++; }
                else { lines.Add("+" + b[y]); y++; }
            }
            while (x < a.Length) { lines.Add("-" + a[x]); x++; }
            while (y < b.Length) { lines.Add("+" + b[y]); y++; }
            return lines.ToArray();
        }

        // Reduces a full unified diff to changed lines plus `context` unchanged lines on each
        // side, separated by an ellipsis where unchanged runs are skipped. Avoids rendering
        // tens of thousands of unchanged YAML lines that hang IMGUI.
        public static string[] FilterToHunks(string[] diffLines, int context = DefaultContextLines) {
            if (diffLines == null || diffLines.Length == 0) return System.Array.Empty<string>();
            var include = new bool[diffLines.Length];
            for (int i = 0; i < diffLines.Length; i++) {
                var line = diffLines[i];
                if (line.Length == 0) continue;
                if (line[0] == '+' || line[0] == '-') {
                    int lo = System.Math.Max(0, i - context);
                    int hi = System.Math.Min(diffLines.Length - 1, i + context);
                    for (int j = lo; j <= hi; j++) include[j] = true;
                }
            }
            var result = new List<string>();
            bool prevIncluded = true;
            for (int i = 0; i < diffLines.Length; i++) {
                if (include[i]) {
                    if (!prevIncluded) result.Add("…");
                    result.Add(diffLines[i]);
                    prevIncluded = true;
                } else {
                    prevIncluded = false;
                }
            }
            return result.ToArray();
        }

        // GUI rendering: scroll view + per-line colour. State held by caller.
        public static void Draw(ref Vector2 scroll, FileChange change) {
            EditorGUILayout.LabelField(change.OriginalPath, EditorStyles.boldLabel);
            var full = BuildUnifiedDiff(change.OriginalText, change.NewText);
            var hunks = FilterToHunks(full);
            EditorGUILayout.LabelField($"{hunks.Length} hunk lines / {full.Length} total");
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(200));
            var monospace = new GUIStyle(EditorStyles.label) { font = EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font ?? EditorStyles.label.font };
            var addColor = new Color(0.3f, 0.7f, 0.3f);
            var delColor = new Color(0.8f, 0.3f, 0.3f);
            int rendered = 0;
            foreach (var line in hunks) {
                if (rendered++ >= MaxRenderedLines) {
                    EditorGUILayout.LabelField($"… {hunks.Length - MaxRenderedLines} more lines suppressed");
                    break;
                }
                var prev = GUI.color;
                GUI.color = line.Length > 0 && line[0] == '+' ? addColor : line.Length > 0 && line[0] == '-' ? delColor : prev;
                EditorGUILayout.SelectableLabel(line, monospace, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                GUI.color = prev;
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
