using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Dsl.Cli;

/// <summary>
/// <c>gravc</c> CLI entry point. Two commands: <c>check</c> (parse + resolve +
/// validate) and <c>gen</c> (check + emit). Both stream diagnostics in the
/// canonical <c>path:line:col: severity ruleId: message</c> format and exit
/// non-zero on any error diagnostic. CultureInfo is pinned to invariant on the
/// current thread for determinism.
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            return args[0] switch
            {
                "check" => await RunCheck(args).ConfigureAwait(false),
                "gen" => await RunGen(args).ConfigureAwait(false),
                "--help" or "-h" or "help" => UsageExit(0),
                _ => UsageExit(1, "unknown command '" + args[0] + "'"),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("gravc: " + ex.Message);
            return 1;
        }
    }

    private static async Task<int> RunCheck(string[] args)
    {
        var parsed = ParseArgs(args, allowOutput: false);
        if (parsed is null) return 1;
        if (!TryResolveAsOf(parsed.AsOfRaw, out var asOf)) return 1;
        var result = await CompilerPipeline.Check(parsed.Input, asOf, parsed.Emitters).ConfigureAwait(false);
        PrintDiagnostics(result.Diagnostics);
        return result.Success ? 0 : 1;
    }

    private static async Task<int> RunGen(string[] args)
    {
        var parsed = ParseArgs(args, allowOutput: true);
        if (parsed is null) return 1;
        if (string.IsNullOrEmpty(parsed.Output))
        {
            Console.Error.WriteLine("gravc: gen requires --output <dir>");
            return 1;
        }
        if (!TryResolveAsOf(parsed.AsOfRaw, out var asOf)) return 1;
        var result = await CompilerPipeline.Gen(parsed.Input, parsed.Output!, asOf, parsed.Emitters).ConfigureAwait(false);
        PrintDiagnostics(result.Diagnostics);
        return result.Success ? 0 : 1;
    }

    /// <summary>
    /// FR-141 / FR-142: resolve the <c>--as-of</c> value to a <see cref="DateOnly"/>.
    /// When absent, the CLI reads <see cref="DateTime.UtcNow"/> — this is the ONLY
    /// clock read in the entire repository; the compiler library (and every emitter)
    /// stays clock-free per LD-7. The <c>BannedSymbolsFile</c> analyzer is attached
    /// to every project except <c>Gravity.Dsl.Cli</c> (see <c>Directory.Build.props</c>),
    /// so a stray <see cref="DateTime.UtcNow"/> in the compiler still fails the build.
    /// </summary>
    private static bool TryResolveAsOf(string? raw, out DateOnly asOf)
    {
        if (raw is { Length: > 0 })
        {
            if (!DateOnly.TryParseExact(
                    raw, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out asOf))
            {
                Console.Error.WriteLine(
                    "gravc " + CliRuleIds.Cli002 + ": --as-of value '" + raw + "' must be YYYY-MM-DD");
                return false;
            }
            return true;
        }
        asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        return true;
    }

    private static void PrintDiagnostics(System.Collections.Immutable.ImmutableArray<Gravity.Dsl.Ast.Diagnostic> diags)
    {
        foreach (var d in diags)
        {
            var line = DiagnosticFormatter.Format(d);
            if (d.Severity == Gravity.Dsl.Ast.DiagnosticSeverity.Error)
            {
                Console.Error.WriteLine(line);
            }
            else
            {
                Console.Out.WriteLine(line);
            }
        }
    }

    private static ParsedArgs? ParseArgs(string[] args, bool allowOutput)
    {
        string? input = null;
        string? output = null;
        string? asOfRaw = null;
        var emitters = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input":
                    if (++i >= args.Length) { Console.Error.WriteLine("gravc: --input requires a value"); return null; }
                    input = args[i];
                    break;
                case "--output":
                    if (!allowOutput) { Console.Error.WriteLine("gravc: --output not valid for this command"); return null; }
                    if (++i >= args.Length) { Console.Error.WriteLine("gravc: --output requires a value"); return null; }
                    output = args[i];
                    break;
                case "--emitter":
                    if (++i >= args.Length) { Console.Error.WriteLine("gravc: --emitter requires a value"); return null; }
                    emitters.Add(args[i]);
                    break;
                case "--as-of":
                    if (++i >= args.Length) { Console.Error.WriteLine("gravc: --as-of requires a value"); return null; }
                    asOfRaw = args[i];
                    break;
                default:
                    Console.Error.WriteLine("gravc: unknown argument '" + args[i] + "'");
                    return null;
            }
        }
        if (input is null)
        {
            Console.Error.WriteLine("gravc: --input <dir> is required");
            return null;
        }
        return new ParsedArgs(input, output, emitters, asOfRaw);
    }

    private static int UsageExit(int code, string? message = null)
    {
        if (message is not null) Console.Error.WriteLine("gravc: " + message);
        PrintUsage();
        return code;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("usage: gravc <command> [options]");
        Console.Error.WriteLine("  gravc check --input <dir> [--as-of YYYY-MM-DD]");
        Console.Error.WriteLine("  gravc gen --input <dir> --output <dir> [--emitter <name>]* [--as-of YYYY-MM-DD]");
    }

    private sealed record ParsedArgs(string Input, string? Output, List<string> Emitters, string? AsOfRaw);
}
