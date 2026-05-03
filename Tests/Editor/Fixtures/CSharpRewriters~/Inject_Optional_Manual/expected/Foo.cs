using VContainer;

public class Foo
{
    // TODO: MIGRATE-MANUAL [InjectOptional]
    // Reason: VContainer has no direct field/method [InjectOptional]; constructor defaults work.
    // Suggested: see Docs~/manual-todos.md#injectoptional
    // Original code preserved below — review and rewrite.
    [InjectOptional] private IBar _maybeBar;
}
