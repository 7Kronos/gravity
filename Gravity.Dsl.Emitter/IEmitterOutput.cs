namespace Gravity.Dsl.Emitter;

/// <summary>
/// Sink an emitter writes its files into. The host owns the concrete implementation
/// (see <see cref="BufferedEmitterOutput"/>): writes are buffered in memory, sorted by
/// relative path under <see cref="System.StringComparer.Ordinal"/>, then committed to
/// disk after the emitter returns. This is what guarantees on-disk write order is
/// independent of emitter authoring style or thread scheduling (plan.md §4).
/// </summary>
public interface IEmitterOutput
{
    /// <summary>
    /// Buffer a file write. <paramref name="relativePath"/> is resolved against the
    /// emitter's configured <c>output</c> root.
    /// </summary>
    void WriteFile(string relativePath, string contents);
}
