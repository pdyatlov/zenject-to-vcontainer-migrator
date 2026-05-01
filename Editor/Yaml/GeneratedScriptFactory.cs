// Produces the C# scaffolding the migrator emits when it detects Zenject
// scenes/prefabs that need a generated counterpart on the VContainer side.
//
// Two scripts are produced:
//   - ProjectLifetimeScope.cs  — replaces ProjectContext (spec §5.5).
//   - AutoRegisterComponent.cs — replaces ZenjectBinding (spec §5.6).
//
// Both files target the host project under Assets/Zenject2VContainer.Generated/
// so they sit alongside other user code, get picked up by the default
// Assembly-CSharp asmdef, and acquire stable script GUIDs once Unity compiles
// them. The migration is two-pass: first run emits these files (and Findings
// noting the GUID swap is pending); the user opens Unity to compile; the
// second run sees the new script GUIDs via ScriptGuidLookup and completes the
// asset patches.

namespace Zenject2VContainer.Yaml {
    public static class GeneratedScriptFactory {
        public const string GeneratedFolderRel = "Assets/Zenject2VContainer.Generated";
        public const string ProjectLifetimeScopeFileName = "ProjectLifetimeScope.cs";
        public const string AutoRegisterComponentFileName = "AutoRegisterComponent.cs";

        public const string ProjectLifetimeScopeNamespace = "Zenject2VContainer.Generated";
        public const string AutoRegisterComponentNamespace = "Zenject2VContainer.Generated";

        public const string ProjectLifetimeScopeFqn =
            ProjectLifetimeScopeNamespace + ".ProjectLifetimeScope";
        public const string AutoRegisterComponentFqn =
            AutoRegisterComponentNamespace + ".AutoRegisterComponent";

        public static string ProjectLifetimeScopeSource() {
            return @"using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Zenject2VContainer.Generated
{
    // Replacement for Zenject's ProjectContext. The original prefab in
    // Resources/ is renamed to ProjectLifetimeScope.prefab and its script GUID
    // swapped to point at this class. The bootstrap method instantiates the
    // prefab before any scene loads, mirroring ProjectContext's lifecycle.
    public class ProjectLifetimeScope : LifetimeScope
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var prefab = Resources.Load<ProjectLifetimeScope>(""ProjectLifetimeScope"");
            if (prefab != null) Instantiate(prefab);
        }

        protected override void Configure(IContainerBuilder builder)
        {
            // TODO: move bindings from the original ProjectContext installers here.
        }
    }
}
";
        }

        public static string AutoRegisterComponentSource() {
            return @"using UnityEngine;
using VContainer;

namespace Zenject2VContainer.Generated
{
    // Replacement for Zenject's ZenjectBinding component. Attach to a
    // GameObject in the scene; during LifetimeScope.Configure the parent
    // scope walks its hierarchy and calls Register on each AutoRegisterComponent
    // it finds, preserving the user's bind-type choices.
    public sealed class AutoRegisterComponent : MonoBehaviour
    {
        public enum BindKind { Self, AllInterfaces, AllInterfacesAndSelf }

        [Tooltip(""How the attached component should be registered."")]
        public BindKind Kind = BindKind.Self;

        [Tooltip(""Component to register. Defaults to the first sibling component if left empty."")]
        public Component Target;

        public void Register(IContainerBuilder builder)
        {
            var component = Target != null ? Target : GetTargetComponent();
            if (component == null) return;

            switch (Kind)
            {
                case BindKind.Self:
                    builder.RegisterInstance(component).AsSelf();
                    break;
                case BindKind.AllInterfaces:
                    builder.RegisterInstance(component).AsImplementedInterfaces();
                    break;
                case BindKind.AllInterfacesAndSelf:
                    builder.RegisterInstance(component).AsImplementedInterfaces().AsSelf();
                    break;
            }
        }

        private Component GetTargetComponent()
        {
            foreach (var c in GetComponents<Component>())
            {
                if (c == this) continue;
                if (c == null) continue;
                return c;
            }
            return null;
        }
    }
}
";
        }
    }
}
