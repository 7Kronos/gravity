using FluentAssertions;
using Gravity.Dsl.Ast;
using Xunit;

namespace Gravity.Dsl.Tests;

/// <summary>
/// Phase 8 / T111 / AC-8.14. Pins the literal AST version value so accidental
/// bumps surface as test failures.
/// </summary>
/// <remarks>
/// AC-8.14 has two halves: (1) the literal-value assertion (covered here); and
/// (2) a regression that loads a vendored 1.0.0 <c>Gravity.Dsl.Ast.dll</c> into
/// an isolated <c>AssemblyLoadContext</c> and asserts a stub emitter compiled
/// against 1.0.0 still loads against the live 1.1.0 host. The vendored-1.0.0
/// half requires a published 1.0.0 nupkg which does not exist yet (Phase 3 did
/// not publish to NuGet) — see the TODO below.
/// </remarks>
public sealed class AstStabilityTests
{
    [Fact]
    public void AstVersion_IsLockedAt_1_1_0()
    {
        AstVersion.Value.Should().Be("1.1.0");
    }

    // TODO: requires vendored 1.0.0 Gravity.Dsl.Ast.dll once Phase 10 OSS launch
    // publishes one. The second half of AC-8.14 — loading a 1.0.0-compiled emitter
    // assembly into an isolated AssemblyLoadContext and asserting it still binds
    // against 1.1.0 — cannot be implemented until that artifact exists.
}
