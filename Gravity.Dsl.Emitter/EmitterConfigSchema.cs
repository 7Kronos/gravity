using System.Collections.Immutable;

namespace Gravity.Dsl.Emitter;

/// <summary>
/// Scalar kinds the Gravity emitter config loader knows how to validate.
/// Mirrors the YAML scalar shapes the loader maps onto.
/// </summary>
public enum ConfigValueKind
{
    /// <summary>String value.</summary>
    String,

    /// <summary>Signed integer value (64-bit).</summary>
    Int,

    /// <summary>Boolean value.</summary>
    Bool
}

/// <summary>
/// One key in an emitter's published configuration schema.
/// </summary>
/// <param name="Name">Configuration key.</param>
/// <param name="Kind">Expected scalar kind.</param>
/// <param name="Required">When <c>true</c>, the loader emits <c>CFG003</c> if the key is absent.</param>
/// <param name="Default">Default value applied when the key is absent and not required; may be <c>null</c>.</param>
public sealed record ConfigKey(
    string Name,
    ConfigValueKind Kind,
    bool Required,
    object? Default);

/// <summary>
/// Declarative schema an emitter publishes so the Gravity config loader can
/// validate the user's configuration block before invocation. Keys are stored in
/// an <see cref="ImmutableArray{T}"/> to preserve declaration order (which has no
/// semantic meaning to the loader but keeps documentation and error iteration stable).
/// </summary>
public sealed record EmitterConfigSchema(ImmutableArray<ConfigKey> Keys)
{
    /// <summary>An empty schema — accepts only the implicit <c>enabled</c> flag handled by the host.</summary>
    public static EmitterConfigSchema Empty { get; } = new(ImmutableArray<ConfigKey>.Empty);
}
