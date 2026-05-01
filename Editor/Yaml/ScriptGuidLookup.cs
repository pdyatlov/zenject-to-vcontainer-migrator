// Resolves the replacement-side script GUIDs needed by the YAML migrators.
//
// Source-side GUIDs (Zenject types) come from the bundled
// `zenject-script-guids.json` table loaded via `ZenjectScriptGuidTable`.
// Target-side GUIDs (VContainer's LifetimeScope, the migrator's own
// AutoRegisterComponent helper) live inside the host project's installed
// packages, so we resolve them at migration time via AssetDatabase.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Yaml {
    public sealed class ScriptGuidLookup {
        public string LifetimeScopeGuid { get; private set; }
        public string AutoRegisterComponentGuid { get; private set; }
        public string ProjectLifetimeScopeGuid { get; private set; }

        // Returns null entries when the corresponding script could not be
        // located. Callers should treat that as "skip this migrator and emit a
        // manual TODO" rather than swapping to an empty GUID.
        public static ScriptGuidLookup Resolve() {
            var lookup = new ScriptGuidLookup();
            lookup.LifetimeScopeGuid = FindMonoScriptGuid("VContainer.Unity", "LifetimeScope");
            // Generated scripts live in the host's Assets/Zenject2VContainer.Generated/
            // folder once the migrator has emitted them and Unity has compiled.
            lookup.AutoRegisterComponentGuid = FindMonoScriptGuid(
                GeneratedScriptFactory.AutoRegisterComponentNamespace, "AutoRegisterComponent");
            lookup.ProjectLifetimeScopeGuid = FindMonoScriptGuid(
                GeneratedScriptFactory.ProjectLifetimeScopeNamespace, "ProjectLifetimeScope");
            return lookup;
        }

        // Builds the GUID-swap map fed to YamlPatcher.PatchScriptGuids.
        public IReadOnlyDictionary<string, string> BuildGuidMap(ZenjectScriptGuidTable zenjectTable) {
            var map = new Dictionary<string, string>();
            void Add(string scriptName, string targetGuid) {
                if (string.IsNullOrEmpty(targetGuid)) return;
                var src = zenjectTable.GetGuid(scriptName);
                if (string.IsNullOrEmpty(src)) return;
                map[src] = targetGuid;
            }
            Add("SceneContext", LifetimeScopeGuid);
            Add("GameObjectContext", LifetimeScopeGuid);
            Add("ZenjectBinding", AutoRegisterComponentGuid);
            Add("ProjectContext", ProjectLifetimeScopeGuid);
            return map;
        }

        private static string FindMonoScriptGuid(string @namespace, string typeName) {
            var fqName = @namespace + "." + typeName;
            foreach (var guid in AssetDatabase.FindAssets(typeName + " t:MonoScript")) {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null) continue;
                var cls = script.GetClass();
                if (cls == null) continue;
                if (cls.FullName == fqName) return guid;
            }
            return null;
        }
    }
}
