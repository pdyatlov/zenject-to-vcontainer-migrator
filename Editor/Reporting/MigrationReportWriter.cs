using System.IO;
using System.Text;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Reporting {
    public sealed class MigrationReportContext {
        public string ProjectPath;
        public string UnityVersion;
        public string ToolVersion;
        public string RunUtc;
        public string BackupTimestamp;
    }

    public static class MigrationReportWriter {
        public static string Render(MigrationPlan plan, MigrationReportContext ctx) {
            var sb = new StringBuilder();
            sb.AppendLine("# Zenject → VContainer Migration Report");
            sb.AppendLine();
            sb.AppendLine($"- **Project:** `{ctx.ProjectPath}`");
            sb.AppendLine($"- **Unity:** {ctx.UnityVersion}");
            sb.AppendLine($"- **Tool:** {ctx.ToolVersion}");
            sb.AppendLine($"- **Run:** {ctx.RunUtc}");
            if (!string.IsNullOrEmpty(ctx.BackupTimestamp)) sb.AppendLine($"- **Backup:** `Temp/Zenject2VContainer/Backup/{ctx.BackupTimestamp}/`");
            sb.AppendLine();

            int csCount = 0, yamlCount = 0, manifestCount = 0;
            foreach (var c in plan.Changes) {
                if (c.Category == FileChangeCategory.CSharp) csCount++;
                else if (c.Category == FileChangeCategory.Yaml) yamlCount++;
                else if (c.Category == FileChangeCategory.Manifest) manifestCount++;
            }
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine($"- C# files changed: {csCount}");
            sb.AppendLine($"- YAML assets changed: {yamlCount}");
            sb.AppendLine($"- Manifest changes: {manifestCount}");
            sb.AppendLine($"- Manual TODOs: {plan.Unsupported.Count}");
            sb.AppendLine();

            sb.AppendLine("## Changes");
            sb.AppendLine();
            sb.AppendLine("| File | Category | Confidence |");
            sb.AppendLine("|------|----------|------------|");
            foreach (var c in plan.Changes) {
                sb.AppendLine($"| `{c.OriginalPath}` | {c.Category} | {c.Confidence} |");
            }
            sb.AppendLine();

            sb.AppendLine("## Manual TODOs");
            sb.AppendLine();
            if (plan.Unsupported.Count == 0) sb.AppendLine("_None._");
            else {
                sb.AppendLine("| Category | File:Line | Reason | Doc |");
                sb.AppendLine("|----------|-----------|--------|-----|");
                foreach (var f in plan.Unsupported) {
                    var doc = string.IsNullOrEmpty(f.DocLink) ? "" : $"[link]({f.DocLink})";
                    sb.AppendLine($"| {f.Category} | `{f.FilePath}:{f.Line}` | {f.Reason} | {doc} |");
                }
            }
            sb.AppendLine();

            sb.AppendLine("## Rollback");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(ctx.BackupTimestamp)) {
                sb.AppendLine($"- File-level rollback available via `BackupManager.Restore(\"{ctx.BackupTimestamp}\")` or the Migration Wizard's Rollback button while this Editor session is alive.");
            }
            sb.AppendLine("- For commits made after migration, use `git revert` instead of tool rollback.");
            return sb.ToString();
        }

        public static string WriteToDisk(string projectRoot, MigrationPlan plan, MigrationReportContext ctx) {
            var dir = Path.Combine(projectRoot, "Assets", "Zenject2VContainer");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "MIGRATION_REPORT.md");
            File.WriteAllText(path, Render(plan, ctx));
            return path;
        }
    }
}
