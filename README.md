[![CI](https://github.com/marcin-krystianc/NuOptimizer/actions/workflows/ci.yml/badge.svg?branch=master&event=push)](https://github.com/marcin-krystianc/NuOptimizer/actions/workflows/ci.yml?query=branch%3Amaster+event%3Apush)
[![](https://img.shields.io/nuget/vpre/NuOptimizer)](https://www.nuget.org/packages/NuOptimizer/absoluteLatest)

NuOptimizer is a dotnet tool that helps to work around the exponential complexity of the NuGet restore algorithm (https://github.com/NuGet/Home/issues/10030).

# How it works
NuOptimizer analyses the transitive dependency graph of each project in the codebase and injects extra
`<ProjectReference>` and `<PackageReference>` items so the dependency graph becomes flat.
If the dependency graph changes, the NuOptimizer tool needs to be run again to generate these extra items.

Note, that NuOptimizer can flatten dependency graphs safely only when the CPVM (Central Package Version Management) is enabled.

# How to use it
`dotnet tool install --global nuoptimizer`

`nuoptimizer --root-path=<path>`
