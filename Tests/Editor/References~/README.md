# Reference stub assemblies

These DLLs are minimal compile-time surface stubs of Zenject and VContainer
used as Roslyn `MetadataReference` inputs in the test suite. They contain
no real behaviour and must not be loaded at runtime.

## Prerequisites

The build script uses `dotnet-script`. Install it once globally:

    dotnet tool install -g dotnet-script

To regenerate, run from this directory:

    dotnet script build-stubs.csx

Sources are inline in the script. When the spec gains coverage of new
Zenject members, append signatures here, rebuild, and stage the new DLLs.
