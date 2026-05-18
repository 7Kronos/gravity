namespace Gravity.Dsl.Emitter.CSharp;

/// <summary>
/// Resolves the C# namespace for a Gravity declaration: combines an optional
/// emitter-config <c>namespace</c> prefix with the DSL <c>namespace</c> declaration.
/// </summary>
internal static class NamespaceMapper
{
    /// <summary>Fallback namespace when neither a DSL namespace nor a config prefix is supplied.</summary>
    public const string Fallback = "Gravity.Dsl.Generated";

    /// <summary>
    /// Compose the effective C# namespace.
    /// </summary>
    /// <param name="dslNamespace">The DSL file's namespace, e.g. <c>"hr"</c>, or null if the file is unnamespaced.</param>
    /// <param name="configPrefix">The emitter-config <c>namespace</c>, e.g. <c>"AcmeCo.Domain"</c>, or null if unset.</param>
    public static string Compose(string? dslNamespace, string? configPrefix)
    {
        bool hasPrefix = !string.IsNullOrEmpty(configPrefix);
        bool hasDsl = !string.IsNullOrEmpty(dslNamespace);
        if (hasPrefix && hasDsl) return configPrefix + "." + dslNamespace;
        if (hasPrefix) return configPrefix!;
        if (hasDsl) return dslNamespace!;
        return Fallback;
    }

    /// <summary>
    /// Compose a relative file path that mirrors the DSL namespace structure under
    /// the emitter's output root. Files for namespace <c>hr.payroll</c> live under
    /// <c>hr/payroll/</c>; unnamespaced files live at the root.
    /// </summary>
    public static string ComposeDirectory(string? dslNamespace)
    {
        if (string.IsNullOrEmpty(dslNamespace)) return string.Empty;
        return dslNamespace.Replace('.', '/');
    }
}
