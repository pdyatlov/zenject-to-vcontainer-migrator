using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Manifest {
    public sealed class ZenjectRemover : IZenjectRemover {
        public RemovalPlan Plan(InstallationInfo install, string projectRoot) {
            var plan = new RemovalPlan();
            if (install != null) {
                if (install.ZenjectViaUpm) {
                    plan.UpmInstall = true;
                    plan.UpmPackageId = install.UpmPackageId;
                }
                if (install.ZenjectViaAssets && !string.IsNullOrEmpty(install.AssetsFolderPath)) {
                    plan.FolderInstallPath = install.AssetsFolderPath;
                }
            }

            var manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
            if (File.Exists(manifestPath)) {
                var preview = ManifestEditor.StripZenjectScopedRegistries(File.ReadAllText(manifestPath));
                plan.ScopedRegistryNamesToDrop.AddRange(preview.RemovedRegistryNames);
            }

            plan.IsNoop = !plan.UpmInstall
                && string.IsNullOrEmpty(plan.FolderInstallPath)
                && plan.ScopedRegistryNamesToDrop.Count == 0;
            return plan;
        }

        public RemovalResult Apply(RemovalPlan plan, string projectRoot) {
            var result = new RemovalResult { Success = true };
            if (plan.IsNoop) {
                result.Message = "Nothing to remove.";
                return result;
            }

            // 1. UPM uninstall — block until Client.Remove finishes (or 60s timeout).
            if (plan.UpmInstall && !string.IsNullOrEmpty(plan.UpmPackageId)) {
                var req = Client.Remove(plan.UpmPackageId);
                var deadline = System.DateTime.UtcNow.AddSeconds(60);
                while (!req.IsCompleted && System.DateTime.UtcNow < deadline) {
                    System.Threading.Thread.Sleep(100);
                }
                if (req.Status == StatusCode.Success) {
                    result.ActionsTaken.Add("UPM removed " + plan.UpmPackageId);
                } else if (req.Status == StatusCode.Failure) {
                    result.Success = false;
                    result.Message = "UPM removal failed: " + (req.Error?.message ?? "unknown");
                    return result;
                } else {
                    result.Success = false;
                    result.Message = "UPM removal timed out after 60s.";
                    return result;
                }
            }

            // 2. Folder removal — AssetDatabase keeps the .meta in sync.
            if (!string.IsNullOrEmpty(plan.FolderInstallPath)) {
                if (AssetDatabase.DeleteAsset(plan.FolderInstallPath)) {
                    result.ActionsTaken.Add("Deleted folder " + plan.FolderInstallPath);
                } else {
                    result.Success = false;
                    result.Message = "AssetDatabase.DeleteAsset failed for " + plan.FolderInstallPath;
                    return result;
                }
            }

            // 3. Manifest scoped registries.
            if (plan.ScopedRegistryNamesToDrop.Count > 0) {
                var manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
                if (File.Exists(manifestPath)) {
                    var edited = ManifestEditor.StripZenjectScopedRegistries(File.ReadAllText(manifestPath));
                    if (edited.Modified) {
                        File.WriteAllText(manifestPath, edited.NewText);
                        foreach (var name in edited.RemovedRegistryNames) {
                            result.ActionsTaken.Add("Dropped scoped registry " + name);
                        }
                    }
                }
            }

            AssetDatabase.Refresh();
            result.Message = "Zenject removal complete.";
            return result;
        }
    }
}
