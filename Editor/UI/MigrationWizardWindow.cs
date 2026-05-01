using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core;
using Zenject2VContainer.Manifest;
using Zenject2VContainer.UI.Steps;

namespace Zenject2VContainer.UI {
    public sealed class MigrationWizardWindow : EditorWindow {
        private MigrationContext _ctx;
        private WizardState _current = WizardState.Scan;
        private Dictionary<WizardState, IWizardStep> _steps;

        [MenuItem("Window/Zenject2VContainer/Migration Wizard…")]
        public static void Open() {
            var w = GetWindow<MigrationWizardWindow>("Z2VC Migration");
            w.minSize = new Vector2(720, 500);
            w.Show();
        }

        private void OnEnable() {
            _ctx = new MigrationContext();
            _steps = new Dictionary<WizardState, IWizardStep> {
                { WizardState.Scan,    new ScanStep() },
                { WizardState.Preview, new PreviewStep() },
                { WizardState.Apply,   new ApplyStep() },
                { WizardState.Verify,  new VerifyStep() },
                { WizardState.Remove,  new RemoveZenjectStep(new StubZenjectRemover()) }
            };
        }

        private void OnGUI() {
            DrawHeader();
            EditorGUILayout.Space();

            if (_current == WizardState.Done) {
                EditorGUILayout.HelpBox("Migration complete.", MessageType.Info);
            } else if (_steps.TryGetValue(_current, out var step)) {
                EditorGUILayout.LabelField(step.Title, EditorStyles.largeLabel);
                step.OnGUI(_ctx);
                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope()) {
                    GUI.enabled = CanGoBack();
                    if (GUILayout.Button("Back")) GoBack();
                    GUI.enabled = step.CanAdvance(_ctx);
                    if (GUILayout.Button("Next")) GoNext();
                    GUI.enabled = true;
                }
            }

            EditorGUILayout.Space();
            MigrationLogPanel.Draw(_ctx.Log);
        }

        private void DrawHeader() {
            EditorGUILayout.LabelField("Zenject → VContainer migration", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Step {(int)_current + 1} / {System.Enum.GetValues(typeof(WizardState)).Length - 1}: {_current}");
            EditorGUI.ProgressBar(GUILayoutUtility.GetRect(0, 8), Progress(), "");
        }

        private float Progress() => System.Math.Min(1f, (int)_current / (float)((int)WizardState.Done));

        private bool CanGoBack() => _current > WizardState.Scan && _current != WizardState.Done;
        private void GoBack() => _current = (WizardState)((int)_current - 1);
        private void GoNext() {
            var values = (WizardState[])System.Enum.GetValues(typeof(WizardState));
            int idx = System.Array.IndexOf(values, _current);
            if (idx + 1 < values.Length) _current = values[idx + 1];
        }
    }
}
