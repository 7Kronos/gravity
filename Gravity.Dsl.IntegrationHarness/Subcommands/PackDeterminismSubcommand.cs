using System;
using System.IO;
using System.Security.Cryptography;
using Gravity.Dsl.IntegrationHarness.Shared;
using Gravity.Dsl.NupkgNormaliser;

namespace Gravity.Dsl.IntegrationHarness.Subcommands;

/// <summary>
/// AC-9.7-pack: packs <c>Gravity.Dsl.MsBuild.csproj</c> and
/// <c>Gravity.Dsl.Emitter.Sample.Outline.csproj</c> twice each, normalises
/// all four resulting <c>.nupkg</c> files, and asserts pairwise SHA-256
/// equality (FR-3015, LD-25).
/// </summary>
public sealed class PackDeterminismSubcommand : ISubcommand
{
    /// <inheritdoc/>
    public string SubcommandName => "run-ac-9.7-pack";

    /// <inheritdoc/>
    public string AcId => "9.7-pack";

    /// <inheritdoc/>
    public SubcommandResult Run(string scratchDir, string workspaceRoot, HarnessLog log)
    {
        log.WriteToFile("[PackDeterminism] starting; scratchDir=" + scratchDir);

        var msbuildCsproj = Path.Combine(
            workspaceRoot, "Gravity.Dsl.MsBuild", "Gravity.Dsl.MsBuild.csproj");
        var outlineCsproj = Path.Combine(
            workspaceRoot, "samples", "emitters", "outline",
            "Gravity.Dsl.Emitter.Sample.Outline",
            "Gravity.Dsl.Emitter.Sample.Outline.csproj");

        // Pack MsBuild twice.
        var msBuildPack1 = Path.Combine(scratchDir, "msbuild-pack-1");
        var msBuildPack2 = Path.Combine(scratchDir, "msbuild-pack-2");
        Directory.CreateDirectory(msBuildPack1);
        Directory.CreateDirectory(msBuildPack2);

        log.WriteToFile("[PackDeterminism] packing MsBuild (pass 1)");
        var (exit1, stdout1, stderr1) = ProcessRunner.RunDotnetCapture(
            "pack \"" + msbuildCsproj + "\" -c Release -o \"" + msBuildPack1 + "\" --nologo",
            workspaceRoot);
        log.WriteToFile("exit=" + exit1 + "\n" + stdout1 + "\n" + stderr1);
        if (exit1 != 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn009,
                "dotnet pack MsBuild (pass 1) failed with exit " + exit1,
                msBuildPack1, exit1);

        log.WriteToFile("[PackDeterminism] packing MsBuild (pass 2)");
        var (exit2, stdout2, stderr2) = ProcessRunner.RunDotnetCapture(
            "pack \"" + msbuildCsproj + "\" -c Release -o \"" + msBuildPack2 + "\" --nologo",
            workspaceRoot);
        log.WriteToFile("exit=" + exit2 + "\n" + stdout2 + "\n" + stderr2);
        if (exit2 != 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn009,
                "dotnet pack MsBuild (pass 2) failed with exit " + exit2,
                msBuildPack2, exit2);

        // Pack Outline twice.
        var outlinePack1 = Path.Combine(scratchDir, "outline-pack-1");
        var outlinePack2 = Path.Combine(scratchDir, "outline-pack-2");
        Directory.CreateDirectory(outlinePack1);
        Directory.CreateDirectory(outlinePack2);

        log.WriteToFile("[PackDeterminism] packing Outline (pass 1)");
        var (exit3, stdout3, stderr3) = ProcessRunner.RunDotnetCapture(
            "pack \"" + outlineCsproj + "\" -c Release -o \"" + outlinePack1 + "\" --nologo",
            workspaceRoot);
        log.WriteToFile("exit=" + exit3 + "\n" + stdout3 + "\n" + stderr3);
        if (exit3 != 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn009,
                "dotnet pack Outline (pass 1) failed with exit " + exit3,
                outlinePack1, exit3);

        log.WriteToFile("[PackDeterminism] packing Outline (pass 2)");
        var (exit4, stdout4, stderr4) = ProcessRunner.RunDotnetCapture(
            "pack \"" + outlineCsproj + "\" -c Release -o \"" + outlinePack2 + "\" --nologo",
            workspaceRoot);
        log.WriteToFile("exit=" + exit4 + "\n" + stdout4 + "\n" + stderr4);
        if (exit4 != 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn009,
                "dotnet pack Outline (pass 2) failed with exit " + exit4,
                outlinePack2, exit4);

        // Normalise and compare MsBuild packs.
        var msBuildResult = NormaliseAndCompare(
            msBuildPack1, msBuildPack2, "Gravity.Dsl.MsBuild", scratchDir, log);
        if (msBuildResult is not null) return msBuildResult;

        // Normalise and compare Outline packs.
        var outlineResult = NormaliseAndCompare(
            outlinePack1, outlinePack2, "Gravity.Dsl.Emitter.Sample.Outline", scratchDir, log);
        if (outlineResult is not null) return outlineResult;

        log.WriteToFile("[PackDeterminism] all four packs normalised; hashes equal. PASS.");
        return SubcommandResult.Pass();
    }

    private static SubcommandResult? NormaliseAndCompare(
        string packDir1, string packDir2, string packageName,
        string scratchDir, HarnessLog log)
    {
        var nupkg1 = FindNupkg(packDir1);
        var nupkg2 = FindNupkg(packDir2);

        if (nupkg1 is null)
            return SubcommandResult.Fail(HarnessRuleIds.Harn009,
                "No .nupkg found in " + packDir1 + " for " + packageName);
        if (nupkg2 is null)
            return SubcommandResult.Fail(HarnessRuleIds.Harn009,
                "No .nupkg found in " + packDir2 + " for " + packageName);

        var norm1 = Path.Combine(scratchDir, packageName + "-norm-1.nupkg");
        var norm2 = Path.Combine(scratchDir, packageName + "-norm-2.nupkg");

        log.WriteToFile("[PackDeterminism] normalising " + nupkg1 + " -> " + norm1);
        NupkgNormalizer.Normalize(nupkg1, norm1);
        log.WriteToFile("[PackDeterminism] normalising " + nupkg2 + " -> " + norm2);
        NupkgNormalizer.Normalize(nupkg2, norm2);

        var hash1 = Sha256File(norm1);
        var hash2 = Sha256File(norm2);
        log.WriteToFile("[PackDeterminism] " + packageName + " norm1=" + hash1 + " norm2=" + hash2);

        if (!string.Equals(hash1, hash2, StringComparison.Ordinal))
        {
            var preHash1 = Sha256File(nupkg1);
            var preHash2 = Sha256File(nupkg2);
            return SubcommandResult.Fail(
                HarnessRuleIds.Harn009,
                packageName + " normalised .nupkg SHA-256 mismatch: " + hash1 + " != " + hash2
                + "; pre-normalisation: " + preHash1 + " / " + preHash2,
                norm1);
        }

        return null;
    }

    private static string? FindNupkg(string dir)
    {
        var files = Directory.GetFiles(dir, "*.nupkg", SearchOption.TopDirectoryOnly);
        return files.Length > 0 ? files[0] : null;
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        var bytes = SHA256.HashData(stream);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
