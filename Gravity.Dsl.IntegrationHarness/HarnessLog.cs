using System;
using System.IO;
using System.Text;

namespace Gravity.Dsl.IntegrationHarness;

/// <summary>
/// Per-step log writer and stdout-shape emitter for the integration harness
/// (FR-3033, FR-3051). All harness stdout is routed through this class so
/// the shape is governed in one place.
/// </summary>
public sealed class HarnessLog : IDisposable
{
    private readonly StreamWriter _writer;

    /// <summary>Absolute path of the log file this instance writes to.</summary>
    public string LogPath { get; }

    /// <summary>
    /// Opens (or creates) the log file at <paramref name="logPath"/> for append-only
    /// writing using UTF-8 without BOM and LF line endings.
    /// </summary>
    public HarnessLog(string logPath)
    {
        LogPath = logPath;
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        _writer = new StreamWriter(
            new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            leaveOpen: false);
    }

    /// <summary>Appends a line to the log file (LF-terminated, never throws on best-effort).</summary>
    public void WriteToFile(string line)
    {
        _writer.Write(line);
        _writer.Write('\n');
        _writer.Flush();
    }

    /// <summary>
    /// Emits exactly <c>AC-<paramref name="acId"/> PASS\n</c> to stdout (FR-3033).
    /// </summary>
    public static void EmitPassToStdout(string acId)
    {
        Console.Write("AC-" + acId + " PASS\n");
    }

    /// <summary>
    /// Emits the four-line failure block to stdout per FR-3033 / FR-3051:
    /// rule id, AC id, log path each on their own line, plus the dotnet exit
    /// code when available.
    /// </summary>
    public static void EmitFailureToStdout(
        string harnessRuleId,
        string acId,
        string fixturePath,
        int? dotnetExitCode,
        string logPath)
    {
        Console.Write(harnessRuleId + "\n");
        Console.Write("AC-" + acId + " FAIL\n");
        Console.Write("log: " + logPath + "\n");
        if (dotnetExitCode.HasValue)
            Console.Write("dotnet exit: " + dotnetExitCode.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture) + "\n");
        if (!string.IsNullOrEmpty(fixturePath))
            Console.Write("fixture: " + fixturePath + "\n");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _writer.Dispose();
    }
}
