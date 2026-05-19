using System;
using System.IO;
using System.Security.Cryptography;
using FluentAssertions;
using Gravity.Dsl.NupkgNormaliser;
using Xunit;

namespace Gravity.Dsl.NupkgNormaliser.Tests;

/// <summary>
/// Pins AC-9c.11 / FR-3023: Normalize is its own fixed point.
/// Running three passes in succession produces byte-identical results for
/// passes 2 and 3 (idempotence). Pins that no transformation introduces
/// non-determinism on a second application.
/// </summary>
public sealed class IdempotenceTests
{
    private static readonly string FixtureDir = Path.Combine(
        FindRepoRoot(), "tests", "fixtures", "nupkg-normaliser");

    private static string GetTempRoot()
        => Environment.GetEnvironmentVariable("TMPDIR")
           ?? Environment.GetEnvironmentVariable("TEMP")
           ?? "/tmp";

    [Fact]
    public void Normalize_Thrice_Runs2And3ByteEqual()
    {
        var input = Path.Combine(FixtureDir, "pack-a.nupkg");
        var tmp = Path.Combine(GetTempRoot(), "norm-idem-a");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        Directory.CreateDirectory(tmp);
        try
        {
            var pass1 = Path.Combine(tmp, "pass1.nupkg");
            var pass2 = Path.Combine(tmp, "pass2.nupkg");
            var pass3 = Path.Combine(tmp, "pass3.nupkg");

            NupkgNormalizer.Normalize(input, pass1);
            NupkgNormalizer.Normalize(pass1, pass2);
            NupkgNormalizer.Normalize(pass2, pass3);

            Sha256File(pass2).Should().Be(Sha256File(pass3),
                because: "passes 2 and 3 must be byte-identical: Normalize is idempotent on its own output (AC-9c.11 / FR-3023)");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public void Normalize_Thrice_PackB_Runs2And3ByteEqual()
    {
        var input = Path.Combine(FixtureDir, "pack-b.nupkg");
        var tmp = Path.Combine(GetTempRoot(), "norm-idem-b");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        Directory.CreateDirectory(tmp);
        try
        {
            var pass1 = Path.Combine(tmp, "pass1.nupkg");
            var pass2 = Path.Combine(tmp, "pass2.nupkg");
            var pass3 = Path.Combine(tmp, "pass3.nupkg");

            NupkgNormalizer.Normalize(input, pass1);
            NupkgNormalizer.Normalize(pass1, pass2);
            NupkgNormalizer.Normalize(pass2, pass3);

            Sha256File(pass2).Should().Be(Sha256File(pass3),
                because: "passes 2 and 3 must be byte-identical: Normalize is idempotent on its own output (AC-9c.11 / FR-3023)");
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
