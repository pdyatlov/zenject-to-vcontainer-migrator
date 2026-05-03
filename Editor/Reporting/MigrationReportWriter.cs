using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Reporting {
    public sealed class MigrationReportContext {
        public string ProjectPath;
        public string UnityVersion;
        public string ToolVersion;
        public string RunUtc;
        public string BackupTimestamp;
        public int RemainingZenjectFiles;     // new — from post-apply verify scan
        public int CompileErrorCount;         // new — from apply phase compile errors
        public string[] SkippedFiles;         // new — project-relative paths scanned but unchanged
    }

    public static class MigrationReportWriter {
        public static string Render(MigrationPlan plan, MigrationReportContext ctx) {
            var sb = new StringBuilder();
            AppendHeader(sb, ctx);
            AppendToc(sb, ctx);
            AppendSummary(sb, plan);
            AppendChanges(sb, plan, ctx);
            AppendManualTodos(sb, plan);
            AppendSkipped(sb, ctx);
            AppendVerification(sb, ctx);
            AppendRollback(sb, ctx);
            return sb.ToString();
        }

        public static string WriteToDisk(string projectRoot, MigrationPlan plan, MigrationReportContext ctx) {
            var dir = Path.Combine(projectRoot, "Assets", "Zenject2VContainer");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "MIGRATION_REPORT.md");
            File.WriteAllText(path, Render(plan, ctx));
            return path;
        }

        // ── helpers ───────────────────────────────────────────────────────────

        /// Make path project-relative when it starts with projectRoot.
        internal static string Rel(string projectRoot, string p) {
            if (string.IsNullOrEmpty(p) || string.IsNullOrEmpty(projectRoot)) return p;
            var root = projectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            var n = p.Replace('\\', '/');
            return n.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? n.Substring(root.Length) : n;
        }

        /// Escape markdown table-breaking characters in a cell value.
        static string Cell(string s) {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("|", @"\|").Replace("`", "'");
        }

        /// Build a Docs~ anchor URL for a category.
        static string CategoryAnchor(string category) =>
            "Docs~/manual-todos.md#" + (category ?? "").ToLowerInvariant();

        /// Parse compact UTC stamp "yyyyMMddTHHmmssZ" and return "yyyy-MM-dd HH:mm UTC".
        /// Returns null if parsing fails.
        static string ParseBackupHuman(string stamp) {
            if (string.IsNullOrEmpty(stamp)) return null;
            if (DateTime.TryParseExact(stamp, "yyyyMMddTHHmmssZ",
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)) {
                return dt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
            }
            return null;
        }

        // ── section renderers ─────────────────────────────────────────────────

        static void AppendHeader(StringBuilder sb, MigrationReportContext ctx) {
            sb.AppendLine("# Zenject → VContainer Migration Report");
            sb.AppendLine();
            sb.AppendLine($"- **Project:** `{ctx.ProjectPath}`");
            sb.AppendLine($"- **Unity:** {ctx.UnityVersion}");
            sb.AppendLine($"- **Tool:** {ctx.ToolVersion}");
            sb.AppendLine($"- **Run:** {ctx.RunUtc}");
            if (!string.IsNullOrEmpty(ctx.BackupTimestamp)) {
                var human = ParseBackupHuman(ctx.BackupTimestamp);
                var humanPart = human != null ? $" ({human})" : "";
                sb.AppendLine($"- **Backup:** `Temp/Zenject2VContainer/Backup/{ctx.BackupTimestamp}/`{humanPart}");
            }
            sb.AppendLine();
        }

        static void AppendToc(StringBuilder sb, MigrationReportContext ctx) {
            sb.AppendLine("## Table of contents");
            sb.AppendLine();
            sb.AppendLine("- [Summary](#summary)");
            sb.AppendLine("- [Changes](#changes)");
            sb.AppendLine("- [Manual TODOs](#manual-todos)");
            if (ctx.SkippedFiles != null && ctx.SkippedFiles.Length > 0)
                sb.AppendLine("- [Skipped / Unchanged](#skipped--unchanged)");
            sb.AppendLine("- [Post-Apply Verification](#post-apply-verification)");
            sb.AppendLine("- [Rollback](#rollback)");
            sb.AppendLine();
        }

        static void AppendSummary(StringBuilder sb, MigrationPlan plan) {
            int csCount = 0, yamlCount = 0, manifestCount = 0;
            int highCount = 0, mediumCount = 0, lowCount = 0;
            foreach (var c in plan.Changes) {
                switch (c.Category) {
                    case FileChangeCategory.CSharp:    csCount++;       break;
                    case FileChangeCategory.Yaml:      yamlCount++;     break;
                    case FileChangeCategory.Manifest:  manifestCount++; break;
                }

                switch (c.Confidence) {
                    case ChangeConfidence.High:        highCount++;   break;
                    case ChangeConfidence.Medium:      mediumCount++; break;
                    case ChangeConfidence.LowFlagged:  lowCount++;    break;
                }
            }

            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine($"- C# files changed: {csCount}");
            sb.AppendLine($"- YAML assets changed: {yamlCount}");
            sb.AppendLine($"- Manifest changes: {manifestCount}");
            sb.AppendLine($"- Manual TODOs: {plan.Unsupported.Count}");
            sb.AppendLine();
            sb.AppendLine("**Confidence breakdown:**");
            sb.AppendLine();
            sb.AppendLine($"- High: {highCount}");
            sb.AppendLine($"- Medium: {mediumCount}");
            sb.AppendLine($"- LowFlagged: {lowCount}");
            sb.AppendLine();
        }

        static void AppendChanges(StringBuilder sb, MigrationPlan plan, MigrationReportContext ctx) {
            sb.AppendLine("## Changes");
            sb.AppendLine();

            var csChanges = new List<FileChange>();
            var yamlChanges = new List<FileChange>();
            var manifestChanges = new List<FileChange>();
            foreach (var c in plan.Changes) {
                if (c.Category == FileChangeCategory.CSharp) csChanges.Add(c);
                else if (c.Category == FileChangeCategory.Yaml) yamlChanges.Add(c);
                else if (c.Category == FileChangeCategory.Manifest) manifestChanges.Add(c);
            }

            void RenderSubsection(string header, List<FileChange> changes) {
                if (changes.Count == 0) return;
                sb.AppendLine($"### {header}");
                sb.AppendLine();
                sb.AppendLine("| File | Confidence |");
                sb.AppendLine("|------|------------|");
                foreach (var c in changes) {
                    var rel = Cell(Rel(ctx.ProjectPath, c.OriginalPath));
                    sb.AppendLine($"| {rel} | {c.Confidence} |");
                }
                sb.AppendLine();
            }

            RenderSubsection("C#", csChanges);
            RenderSubsection("YAML", yamlChanges);
            RenderSubsection("Manifest", manifestChanges);

            if (plan.Changes.Count == 0) {
                sb.AppendLine("_No changes._");
                sb.AppendLine();
            }
        }

        static void AppendManualTodos(StringBuilder sb, MigrationPlan plan) {
            sb.AppendLine("## Manual TODOs");
            sb.AppendLine();
            if (plan.Unsupported.Count == 0) {
                sb.AppendLine("_None._");
                sb.AppendLine();
                return;
            }

            // Determine whether any finding has a DocLink (controls Doc column visibility)
            bool hasDocLink = false;
            foreach (var f in plan.Unsupported) {
                if (!string.IsNullOrEmpty(f.DocLink)) { hasDocLink = true; break; }
            }

            // Group by category
            var groups = new Dictionary<string, List<Finding>>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var f in plan.Unsupported) {
                var cat = f.Category ?? "";
                if (!groups.ContainsKey(cat)) { groups[cat] = new List<Finding>(); order.Add(cat); }
                groups[cat].Add(f);
            }

            foreach (var cat in order.OrderBy(c => c, StringComparer.Ordinal)) {
                var findings = groups[cat];
                var anchor = CategoryAnchor(cat);
                sb.AppendLine($"### {cat} ({findings.Count}) — [docs]({anchor})");
                sb.AppendLine();
                if (hasDocLink) {
                    sb.AppendLine("| File:Line | Reason | Doc |");
                    sb.AppendLine("|-----------|--------|-----|");
                    foreach (var f in findings) {
                        var doc = string.IsNullOrEmpty(f.DocLink) ? "" : $"[link]({f.DocLink})";
                        sb.AppendLine($"| {Cell(f.FilePath)}:{f.Line} | {Cell(f.Reason)} | {doc} |");
                    }
                } else {
                    sb.AppendLine("| File:Line | Reason |");
                    sb.AppendLine("|-----------|--------|");
                    foreach (var f in findings) {
                        sb.AppendLine($"| {Cell(f.FilePath)}:{f.Line} | {Cell(f.Reason)} |");
                    }
                }
                sb.AppendLine();
            }
        }

        static void AppendSkipped(StringBuilder sb, MigrationReportContext ctx) {
            if (ctx.SkippedFiles == null || ctx.SkippedFiles.Length == 0) return;
            sb.AppendLine("## Skipped / Unchanged");
            sb.AppendLine();
            sb.AppendLine("The following files were scanned but had no rewriter changes applied:");
            sb.AppendLine();
            foreach (var f in ctx.SkippedFiles) {
                sb.AppendLine($"- {f}");
            }
            sb.AppendLine();
        }

        static void AppendVerification(StringBuilder sb, MigrationReportContext ctx) {
            sb.AppendLine("## Post-Apply Verification");
            sb.AppendLine();
            sb.AppendLine($"- Remaining Zenject references (files): {ctx.RemainingZenjectFiles}");
            sb.AppendLine($"- Compile errors after apply: {ctx.CompileErrorCount}");
            if (ctx.RemainingZenjectFiles == 0 && ctx.CompileErrorCount == 0) {
                sb.AppendLine();
                sb.AppendLine("_Migration verified clean._");
            } else {
                sb.AppendLine();
                if (ctx.RemainingZenjectFiles > 0)
                    sb.AppendLine($"> **{ctx.RemainingZenjectFiles} file(s) still reference Zenject.** Review the Manual TODOs section and address any remaining items.");
                if (ctx.CompileErrorCount > 0)
                    sb.AppendLine($"> **{ctx.CompileErrorCount} compile error(s) detected.** Check Unity's Console for details.");
            }
            sb.AppendLine();
        }

        static void AppendRollback(StringBuilder sb, MigrationReportContext ctx) {
            sb.AppendLine("## Rollback");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(ctx.BackupTimestamp)) {
                var human = ParseBackupHuman(ctx.BackupTimestamp);
                var humanPart = human != null ? $" ({human})" : "";
                sb.AppendLine($"- File-level rollback available via `BackupManager.Restore(\"{ctx.BackupTimestamp}\")` or the Migration Wizard's Rollback button.");
                sb.AppendLine($"  Backup timestamp: `{ctx.BackupTimestamp}`{humanPart}");
                sb.AppendLine();
                sb.AppendLine("> **Warning:** The backup lives under `Temp/Zenject2VContainer/Backup/` which is **wiped on Editor reimport** (Unity deletes the `Temp/` folder whenever it reimports the project). Restore only works while the same Editor session is alive. If you have restarted Unity or triggered a full reimport since the apply, the backup is gone — use `git revert` instead.");
            } else {
                sb.AppendLine("- No backup was recorded for this run.");
            }
            sb.AppendLine();
            sb.AppendLine("- For commits made after migration, use `git revert` instead of tool rollback.");
        }
    }
}
