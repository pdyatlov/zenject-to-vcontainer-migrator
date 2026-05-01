using VContainer;

public class Foo
{

    // TODO: MIGRATE-MANUAL [InjectOptional]
// Reason: VContainer has no direct field/method [InjectOptional]; constructor defaults work.
// Suggested: see https://github.com/<owner>/<repo>/blob/main/docs/manual-todos/InjectOptional.md
// Original code preserved below — review and rewrite.
[InjectOptional] private IBar _maybeBar;
}
