// Orchestrates YAML asset migration: walks every .unity / .prefab / .asset
// under the host's Assets/, swaps Zenject script GUIDs to their VContainer
// equivalents using YamlPatcher, and emits a MigrationPlan-shaped record of
// FileChanges plus manual TODO Findings for cases that need follow-up.
//
// Mapping summary (spec §5.5):
//   SceneContext / GameObjectContext -> LifetimeScope (resolved via AssetDatabase).
//   ProjectContext                   -> generated ProjectLifetimeScope.cs +
//                                       prefab rename. ProjectLifetimeScope.cs is
//                                       emitted as an extra FileChange the first
//                                       time a ProjectContext is detected; once
//                                       Unity compiles it, ScriptGuidLookup
//                                       returns its GUID and a second migration
//                                       pass swaps the prefab.
//   ZenjectBinding                   -> generated AutoRegisterComponent.cs.
//                                       Same two-pass workflow as ProjectContext.
//
// Field-level remapping (e.g. SceneContext._monoInstallers -> LifetimeScope's
// installer composition) is intentionally out of scope for M3 — the GUID swap
// alone leaves serialised fields under the new script with the same names, so
// Unity drops unrecognised fields silently. A per-context Finding tells the
// developer to re-wire the LifetimeScope manually.

using System.Collections.Generic;
using System.IO;
using Zenject2VContainer.Core;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Yaml {
    public sealed class AssetMigrator {
        private readonly ZenjectScriptGuidTable _zenjectTable;
        private readonly ScriptGuidLookup _lookup;
        private readonly IReadOnlyDictionary<string, string> _guidMap;

        public AssetMigrator(ZenjectScriptGuidTable zenjectTable, ScriptGuidLookup lookup) {
            _zenjectTable = zenjectTable;
            _lookup = lookup;
            _guidMap = lookup.BuildGuidMap(zenjectTable);
        }

        public IReadOnlyDictionary<string, string> GuidMap => _guidMap;

        public sealed class AssetMigrationResult {
            public FileChange Change;        // null when no text edits occurred.
            public List<Finding> Findings = new List<Finding>();  // always populated when relevant.
        }

        // Patches one file's text. Returns a result with `Change == null` when
        // no GUID edits applied (still surfaces context-related Findings so the
        // caller can flag the file for manual review without writing it).
        public AssetMigrationResult MigrateAssetText(string filePath, string originalText) {
            var result = new AssetMigrationResult();
            var preFindings = DetectSpecialContexts(filePath, originalText);

            var patch = YamlPatcher.PatchScriptGuids(originalText, _guidMap);
            if (patch.Edits.Count > 0) {
                var change = new FileChange {
                    OriginalPath = filePath,
                    OriginalText = originalText,
                    NewText = patch.Text,
                    Category = FileChangeCategory.Yaml,
                    Confidence = ChangeConfidence.Medium
                };
                foreach (var p in patch.Edits) {
                    change.RelatedFindings.Add(new Finding {
                        Category = "YamlGuidSwap",
                        FilePath = filePath,
                        Line = p.LineNumber,
                        Reason = p.Reason
                    });
                }
                change.RelatedFindings.AddRange(preFindings);
                result.Change = change;
                result.Findings.AddRange(change.RelatedFindings);
            } else {
                // No edits — surface findings only.
                result.Findings.AddRange(preFindings);
            }
            return result;
        }

        // Cross-file detection state — set during a directory walk so the
        // migrator can decide whether to emit ProjectLifetimeScope.cs /
        // AutoRegisterComponent.cs templates after the loop completes.
        public bool SawProjectContext { get; private set; }
        public bool SawZenjectBinding { get; private set; }
        public string ProjectContextPrefabPath { get; private set; }

        private List<Finding> DetectSpecialContexts(string filePath, string text) {
            var findings = new List<Finding>();
            var projectContextGuid = _zenjectTable.GetGuid("ProjectContext");
            var zenjectBindingGuid = _zenjectTable.GetGuid("ZenjectBinding");
            var sceneContextGuid = _zenjectTable.GetGuid("SceneContext");

            if (!string.IsNullOrEmpty(projectContextGuid) && text.Contains(projectContextGuid)) {
                SawProjectContext = true;
                if (filePath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) {
                    ProjectContextPrefabPath = filePath;
                }
                if (string.IsNullOrEmpty(_lookup.ProjectLifetimeScopeGuid)) {
                    findings.Add(new Finding {
                        Category = "ProjectContext",
                        FilePath = filePath,
                        Reason = "ProjectContext detected. ProjectLifetimeScope.cs has been generated; compile the project and re-run YAML migration to swap the prefab GUID and finish the rename."
                    });
                } else {
                    findings.Add(new Finding {
                        Category = "ProjectContext",
                        FilePath = filePath,
                        Reason = "ProjectContext GUID swapped to ProjectLifetimeScope. Rename the prefab to ProjectLifetimeScope.prefab so Resources.Load can find it."
                    });
                }
            }
            if (!string.IsNullOrEmpty(zenjectBindingGuid) && text.Contains(zenjectBindingGuid)) {
                SawZenjectBinding = true;
                if (string.IsNullOrEmpty(_lookup.AutoRegisterComponentGuid)) {
                    findings.Add(new Finding {
                        Category = "ZenjectBinding",
                        FilePath = filePath,
                        Reason = "ZenjectBinding detected. AutoRegisterComponent.cs has been generated; compile the project and re-run YAML migration to swap the GUID."
                    });
                } else {
                    findings.Add(new Finding {
                        Category = "ZenjectBinding",
                        FilePath = filePath,
                        Reason = "ZenjectBinding GUID swapped to AutoRegisterComponent. Verify the BindKind / Target fields land correctly on the migrated component."
                    });
                }
            }
            if (!string.IsNullOrEmpty(sceneContextGuid) && text.Contains(sceneContextGuid)) {
                findings.Add(new Finding {
                    Category = "SceneContextRewire",
                    FilePath = filePath,
                    Reason = "SceneContext GUID swapped to LifetimeScope; re-wire installer references in the new component manually."
                });
            }
            return findings;
        }

        // Convenience helper: walk every YAML asset under `assetsRoot` and
        // accumulate FileChanges + Findings in a MigrationPlan. Files with no
        // GUID edits and no findings are skipped; files with findings but no
        // edits contribute Unsupported entries only (no FileChange written).
        // After the walk, emits ProjectLifetimeScope.cs / AutoRegisterComponent.cs
        // template FileChanges when the corresponding script does not yet exist
        // in the host project. The user compiles those, re-runs migration, and
        // the second pass completes the asset GUID swap.
        public MigrationPlan MigrateAssetsDirectory(string assetsRoot) {
            var plan = new MigrationPlan();
            if (!Directory.Exists(assetsRoot)) return plan;
            foreach (var path in Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories)) {
                var ext = Path.GetExtension(path);
                if (ext != ".unity" && ext != ".prefab" && ext != ".asset") continue;
                var text = File.ReadAllText(path);
                var result = MigrateAssetText(path, text);
                if (result.Change != null) plan.Changes.Add(result.Change);
                plan.Unsupported.AddRange(result.Findings);
            }

            EmitGeneratedScriptIfNeeded(plan, assetsRoot,
                wasDetected: SawProjectContext,
                guidAlreadyResolved: !string.IsNullOrEmpty(_lookup.ProjectLifetimeScopeGuid),
                fileName: GeneratedScriptFactory.ProjectLifetimeScopeFileName,
                source: GeneratedScriptFactory.ProjectLifetimeScopeSource(),
                category: "ProjectContext");

            EmitGeneratedScriptIfNeeded(plan, assetsRoot,
                wasDetected: SawZenjectBinding,
                guidAlreadyResolved: !string.IsNullOrEmpty(_lookup.AutoRegisterComponentGuid),
                fileName: GeneratedScriptFactory.AutoRegisterComponentFileName,
                source: GeneratedScriptFactory.AutoRegisterComponentSource(),
                category: "ZenjectBinding");

            // Surface the prefab-rename TODO as a top-level finding the user can
            // act on after re-running the second pass.
            if (SawProjectContext && !string.IsNullOrEmpty(ProjectContextPrefabPath)) {
                plan.Unsupported.Add(new Finding {
                    Category = "ProjectContextPrefabRename",
                    FilePath = ProjectContextPrefabPath,
                    Reason = "Rename this prefab file to ProjectLifetimeScope.prefab and place it under a Resources/ folder so Resources.Load<ProjectLifetimeScope>(\"ProjectLifetimeScope\") resolves at runtime."
                });
            }
            return plan;
        }

        private static void EmitGeneratedScriptIfNeeded(MigrationPlan plan, string assetsRoot,
                                                        bool wasDetected, bool guidAlreadyResolved,
                                                        string fileName, string source, string category) {
            if (!wasDetected) return;
            // Target path inside the host's Assets/ tree. The relative folder is
            // resolved via assetsRoot so callers operating on a different root
            // (e.g. an integration test fixture) still get correct paths.
            var targetPath = Path.Combine(assetsRoot,
                "Zenject2VContainer.Generated", fileName);
            if (File.Exists(targetPath)) return; // user already has it; nothing to do

            plan.Changes.Add(new FileChange {
                OriginalPath = targetPath,
                OriginalText = string.Empty,
                NewText = source,
                Category = FileChangeCategory.CSharp,
                Confidence = ChangeConfidence.High,
                RelatedFindings = new List<Finding> {
                    new Finding {
                        Category = category,
                        FilePath = targetPath,
                        Reason = guidAlreadyResolved
                            ? "Generated script already present; this file is regenerated on each run for diff comparison."
                            : "Generated " + fileName + ". Compile the project and re-run YAML migration to swap the matching GUIDs across scenes / prefabs."
                    }
                }
            });
        }
    }
}
