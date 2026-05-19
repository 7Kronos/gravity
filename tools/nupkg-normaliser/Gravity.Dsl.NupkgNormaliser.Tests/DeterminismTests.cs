using System;
using System.IO;
using System.Security.Cryptography;
using FluentAssertions;
using Gravity.Dsl.NupkgNormaliser;
using Xunit;

namespace Gravity.Dsl.NupkgNormaliser.Tests;

/// <summary>
/// Pins FR-3023: running Normalize twice against the same input produces
/// byte-identical outputs. Exercises both pack-a and pack-b fixtures.
/// </summary>
public sealed class DeterminismTests
{
    private static readonly string FixtureDir = Path.Combine(
        FindRepoRoot(), "tests", "fixtures", "nupkg-normaliser");

    private static string GetTempRoot()
        => Environment.GetEnvironmentVariable("TMPDIR")
           ?? Environment.GetEnvironmentVariable("TEMP")
           ?? "/tmp";

    [Fact]
    public void Normalize_TwiceAgainstSameInput_ByteEqual_PackA()
    {
        var input = Path.Combine(FixtureDir, "pack-a.nupkg");
        var tmp = Path.Combine(GetTempRoot(), "norm-det-a");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        Directory.CreateDirectory(tmp);
        try
        {
            var out1 = Path.Combine(tmp, "out1.nupkg");
            var out2 = Path.Combine(tmp, "out2.nupkg");
            NupkgNormalizer.Normalize(input, out1);
            NupkgNormalizer.Normalize(input, out2);
            Sha256File(out1).Should().Be(Sha256File(out2),
                because: "two Normalize passes on the same input must produce byte-identical output (FR-3023)");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public void Normalize_TwiceAgainstSameInput_ByteEqual_PackB()
    {
        var input = Path.Combine(FixtureDir, "pack-b.nupkg");
        var tmp = Path.Combine(GetTempRoot(), "norm-det-b");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        Directory.CreateDirectory(tmp);
        try
        {
            var out1 = Path.Combine(tmp, "out1.nupkg");
            var out2 = Path.Combine(tmp, "out2.nupkg");
            NupkgNormalizer.Normalize(input, out1);
            NupkgNormalizer.Normalize(input, out2);
            Sha256File(out1).Should().Be(Sha256File(out2),
                because: "two Normalize passes on the same input must produce byte-identical output (FR-3023)");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    private static string Sha256File(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Gravity.Dsl.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Cannot locate repo root (no Gravity.Dsl.sln found)");
    }
}
