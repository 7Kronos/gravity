using Xunit;

// Disable xunit's default test-class parallelism for this assembly. The Phase 9
// slow tests spawn `dotnet pack` and `dotnet build` child processes via the SDK
// build-server; running two of those simultaneously contends for locked ref/
// assemblies (Gravity.Dsl.Ast.dll etc.) and produces sporadic IOException
// failures. Serial execution removes the race without losing test coverage.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
