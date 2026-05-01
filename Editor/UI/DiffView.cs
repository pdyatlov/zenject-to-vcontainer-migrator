using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.UI {
    public static class DiffView {
        public static string[] BuildUnifiedDiff(string original, string updated) {
            var a = (original ?? "").Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            var b = (updated ?? "").Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            if (a.Length == 1 && a[0] == "") a = System.Array.Empty<string>();
            if (b.Length == 1 && b[0] == "") b = System.Array.Empty<string>();

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

        // GUI rendering: scroll view + per-line colour. State held by caller.
        public static void Draw(ref Vector2 scroll, FileChange change) {
            EditorGUILayout.LabelField(change.OriginalPath, EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(200));
            var lines = BuildUnifiedDiff(change.OriginalText, change.NewText);
            var monospace = new GUIStyle(EditorStyles.label) { font = EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font ?? EditorStyles.label.font };
            var addColor = new Color(0.3f, 0.7f, 0.3f);
            var delColor = new Color(0.8f, 0.3f, 0.3f);
            foreach (var line in lines) {
                var prev = GUI.color;
                GUI.color = line.Length > 0 && line[0] == '+' ? addColor : line.Length > 0 && line[0] == '-' ? delColor : prev;
                EditorGUILayout.SelectableLabel(line, monospace, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                GUI.color = prev;
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
