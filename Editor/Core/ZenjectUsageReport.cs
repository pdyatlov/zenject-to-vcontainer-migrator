using System;
using System.Collections.Generic;

namespace Zenject2VContainer.Core {
    public enum CSharpFindingKind {
        UsingDirective,
        InjectAttribute,
        BindCall,
        InstallerSubclass,
        LifecycleInterface,
        FactoryReference,
        SubContainerCall,
        DiContainerUsage,
        UnsupportedFeature
    }

    [Serializable]
    public sealed class CSharpFinding {
        public string FilePath;
        public int Line;
        public int Column;
        public CSharpFindingKind Kind;
        public string SymbolName;
        public string Snippet;
        public string Confidence;          // "High" | "Medium" | "LowFlagged"
        public string Notes;
    }

    [Serializable]
    public sealed class AssetFinding {
        public string FilePath;            // .unity / .prefab / .asset
        public int Line;                   // line of m_Script ref
        public string ZenjectScriptName;   // e.g. "SceneContext"
        public string ZenjectGuid;
    }

    [Serializable]
    public sealed class InstallationInfo {
        public bool ZenjectViaUpm;
        public string UpmPackageId;        // e.g. "com.svermeulen.extenject"
        public string UpmVersionOrUrl;
        public bool ZenjectViaAssets;
        public string AssetsFolderPath;    // e.g. "Assets/Plugins/Zenject"
        public bool VContainerInstalled;
        public string VContainerVersion;
    }

    [Serializable]
    public sealed class UnsupportedFeature {
        public string Category;            // matches manual TODO category, e.g. "SignalBus"
        public string FilePath;
        public int Line;
        public string Reason;
    }

    [Serializable]
    public sealed class ZenjectUsageReport {
        public string ToolVersion;
        public string UnityVersion;
        public string ScannedAtUtc;        // ISO 8601
        public List<CSharpFinding> CSharpFindings = new();
        public List<AssetFinding> AssetFindings = new();
        public InstallationInfo Install = new();
        public List<UnsupportedFeature> Unsupported = new();

        public static string ToJson(ZenjectUsageReport report) =>
            UnityEngine.JsonUtility.ToJson(report, prettyPrint: true);

        public static ZenjectUsageReport FromJson(string json) =>
            UnityEngine.JsonUtility.FromJson<ZenjectUsageReport>(json);
    }
}
