# Zenject → VContainer Migrator — Implementation Roadmap

**Date:** 2026-05-01
**Spec:** [`../specs/2026-05-01-zenject-to-vcontainer-migrator-design.md`](../specs/2026-05-01-zenject-to-vcontainer-migrator-design.md)

The work is split into six milestones. Each milestone produces working,
testable software that builds on the previous. Milestones are executed in
order; do not skip ahead.

## Project-wide commit policy

The repository owner manages all branches and commits. **Plans must not
include `git commit`, `git push`, `git branch`, `git merge`, or worktree
operations.** Tasks end with a "Stage changes for user review" step
instead, leaving `git status` showing the staged changes for the owner to
inspect and land manually.

This rule overrides the default plan format from the writing-plans skill.

---

## Milestone overview

| ID  | Title                                  | Plan file                                                        | Output                                                                 |
|-----|----------------------------------------|------------------------------------------------------------------|------------------------------------------------------------------------|
| M1  | Foundation + Scanner                   | `2026-05-01-z2vc-m1-foundation-and-scanner.md`                   | UPM package skeleton, asmdefs, vendored Roslyn, snapshot test infra, `ProjectScanner` covering C#, YAML assets, manifest. `MigrationPipeline.RunScanHeadless()` produces `ZenjectUsageReport` JSON. |
| M2  | C# Migration Engine                    | `2026-05-01-z2vc-m2-csharp-rewriters.md`                          | All C# rewriters from spec §5.1–5.4, 5.7–5.9. `MigrationPipeline.RunCSharpHeadless()` rewrites in-memory C# to expected output. ~30 snapshot fixtures green. |
| M3  | YAML Asset Migration                   | `2026-05-01-z2vc-m3-yaml-migration.md`                            | `ScriptGuidMapper`, `SceneContextMigrator`, `ProjectContextMigrator`, `ZenjectBindingMigrator`, `YamlPatcher`. `MigrationPipeline.RunFullHeadless()` covers C# + assets. |
| M4  | Editor Wizard UI                       | `2026-05-01-z2vc-m4-editor-wizard.md`                             | `MigrationWizardWindow` (IMGUI), all wizard steps (Scan, Preview, Apply, Verify), diff viewer, backup/rollback UI, structured log panel. |
| M5  | Zenject Removal + Manifest Management  | `2026-05-01-z2vc-m5-zenject-removal.md`                           | `ZenjectInstallDetector`, `ZenjectRemover` (UPM Client API + AssetDatabase deletion). Final wizard step. VContainer presence + scoped registry cleanup. |
| M6  | Integration Tests + README + Polish    | `2026-05-01-z2vc-m6-integration-and-docs.md`                      | Three Unity fixture projects in `Tests/Integration/`, batch-mode runner, README rewrite per spec §15, `MIGRATION_REPORT.md` formatting polish, doc links for every manual TODO category. |

## Dependencies

```
M1 ─► M2 ─► M3 ─► M4 ─┬─► M6
                      │
                M5 ───┘
```

M5 can be built in parallel with M4 once M3 lands, but the wizard's final
step (in M4) calls into M5's removal API, so practically M5 should land
before M4's removal-step task.

## Definition of done per milestone

Each milestone closes when:

1. All tasks in its plan are complete.
2. Snapshot tests added in that milestone are green.
3. The headless entry point added in that milestone runs successfully on
   at least one local fixture.
4. The repo owner has reviewed and landed the staged changes.
