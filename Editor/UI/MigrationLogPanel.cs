using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.UI {
    public static class MigrationLogPanel {
        private static Vector2 _scroll;
        private static bool _expanded = true;

        public static void Draw(MigrationLog log) {
            _expanded = EditorGUILayout.Foldout(_expanded, $"Log ({log.Entries.Count})", true);
            if (!_expanded) return;
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(120), GUILayout.MaxHeight(240));
            foreach (var e in log.Entries) {
                var prev = GUI.color;
                if (e.Level == LogLevel.Error) GUI.color = new Color(1f, 0.5f, 0.5f);
                else if (e.Level == LogLevel.Warn) GUI.color = new Color(1f, 0.85f, 0.4f);
                EditorGUILayout.SelectableLabel(e.ToString(), EditorStyles.label, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                GUI.color = prev;
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
