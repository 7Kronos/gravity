using System;
using System.IO;
using System.Security.Cryptography;
using FluentAssertions;
using Gravity.Dsl.IntegrationHarness.Shared;
using Xunit;

namespace Gravity.Dsl.IntegrationHarness.Tests;

/// <summary>
/// FR-3003 / AC-9c.3: the shared helper library is the single source of truth
/// for the consumer csproj template and the MinimalEmployeeGravity fixture.
/// Both the harness and the fast lane must call the same shared method.
/// </summary>
public sealed class HelperParityTests
{
    [Fact]
    public void WriteConsumerCsproj_CalledTwiceWithSameArgs_ByteEqual()
    {
        var tmpBase = GetTempDir("parity-test");
        try
        {
            var dir1 = Path.Combine(tmpBase, "consumer-a");
            var dir2 = Path.Combine(tmpBase, "consumer-b");
            Directory.CreateDirectory(dir1);
            Directory.CreateDirectory(dir2);

            const string version = "0.1.0-test";
            const string localFeed = "/tmp/gravity-test-feed";
            var nugetCache1 = Path.Combine(tmpBase, "cache-a");
            var nugetCache2 = Path.Combine(tmpBase, "cache-b");

            var csproj1 = ConsumerCsproj.Write(dir1, string.Empty, nugetCache1, version, localFeed);
            var csproj2 = ConsumerCsproj.Write(dir2, string.Empty, nugetCache2, version, localFeed);

            // The csproj bodies differ in RestorePackagesPath (cache-a vs cache-b),
            // so we check the template shape is consistent by comparing the text
            // minus the cache path. The core assertion is that the same helper produces
            // the same template structure.
            var text1 = File.ReadAllText(csproj1);
            var text2 = File.ReadAllText(csproj2);

            // Both should have the same structure; replace the differing cache path.
            var normalised1 = text1.Replace(nugetCache1, "<CACHE>", StringComparison.Ordinal);
            var normalised2 = text2.Replace(nugetCache2, "<CACHE>", StringComparison.Ordinal);
            normalised1.Should().Be(normalised2,
                because: "FR-3003: ConsumerCsproj.Write must produce identical template structure for identical logical inputs");

            // Hash the normalised content to pin byte stability.
            var hash1 = Sha256String(normalised1);
            var hash2 = Sha256String(normalised2);
            hash1.Should().Be(hash2,
                because: "FR-3003: normalised csproj content must be byte-identical");
        }
        finally
        {
            if (Directory.Exists(tmpBase))
                try { Directory.Delete(tmpBase, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void MinimalEmployeeGravity_MatchesOriginalInlineConst()
    {
        // This is the regression guard for the move from MsBuildIntegrationTests.cs:84-109.
        // The verbatim string here is the expected value; any drift in Fixtures.cs fails this test.
        const string expected =
            "namespace hr;\n"
            + "\n"
            + "entity Employee version 1 {\n"
            + "  identity id: UUID;\n"
            + "  properties {\n"
            + "    name: String;\n"
            + "  }\n"
            + "  lifecycle {\n"
            + "    states { Active, Terminated; }\n"
            + "    transitions { Active -> Terminated on Terminated; }\n"
            + "  }\n"
            + "  events {\n"
            + "    Terminated { terminated_at: DateTime; };\n"
            + "  }\n"
            + "  commands {\n"
            + "    Terminate(reason: String)\n"
            + "      returns TerminationResult\n"
            + "      with side_effect Terminated;\n"
            + "  }\n"
            + "}\n"
            + "\n"
            + "type TerminationResult {\n"
            + "  success: Boolean;\n"
            + "  message: String?;\n"
            + "}\n";

        Fixtures.MinimalEmployeeGravity.Should().Be(expected,
            because: "FR-3003 / FR-3032: Fixtures.MinimalEmployeeGravity must match the verbatim const from MsBuildIntegrationTests.cs:84-109");
    }

    private static string GetTempDir(string label)
    {
        var tmp = Environment.GetEnvironmentVariable("TMPDIR")
                  ?? Environment.GetEnvironmentVariable("TEMP")
                  ?? "/tmp";
        var dir = Path.Combine(tmp, "gravity-harness-tests-" + label);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Sha256String(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
