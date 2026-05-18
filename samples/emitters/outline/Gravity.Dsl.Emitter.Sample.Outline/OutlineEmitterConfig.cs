namespace Gravity.Dsl.Emitter.Sample.Outline;

/// <summary>
/// Strongly typed view of the <see cref="OutlineEmitter"/> configuration block.
/// Mirrors the <c>CSharpEmitterConfig</c> pattern in the reference emitter so a
/// copy-paste author can extend the schema without re-deriving the idiom.
/// </summary>
/// <remarks>
/// The emitter publishes its <see cref="EmitterConfigSchema"/> via
/// <see cref="OutlineEmitter.ConfigurationSchema"/>; the host's
/// <c>ConfigLoader</c> validates user input against that schema before
/// <see cref="OutlineEmitter.Emit"/> ever runs. This class is a typed lens over
/// the resulting <see cref="EmitterConfig.Values"/> dictionary.
/// </remarks>
public sealed class OutlineEmitterConfig
{
    /// <summary>The relative output directory under the host's <c>outputRoot</c>.</summary>
    public string Output { get; }

    private OutlineEmitterConfig(string output)
    {
        Output = output;
    }

    /// <summary>Project a validated <see cref="EmitterConfig"/> into the typed view.</summary>
    public static OutlineEmitterConfig From(EmitterConfig config)
    {
        if (config is null) throw new System.ArgumentNullException(nameof(config));
        // ConfigLoader has already validated the schema (required + string-typed);
        // GetString throws on misuse, which is intentional — the host should not
        // call Emit with a malformed config.
        return new OutlineEmitterConfig(config.GetString(OutlineEmitter.ConfigKeyOutput));
    }
}
