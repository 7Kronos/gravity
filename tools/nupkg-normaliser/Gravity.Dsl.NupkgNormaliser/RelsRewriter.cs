using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Gravity.Dsl.NupkgNormaliser;

/// <summary>
/// Rewrites the <c>_rels/.rels</c> XML document inside a <c>.nupkg</c> so that
/// the single <c>&lt;Relationship&gt;</c> entry whose <c>Type</c> attribute
/// identifies core-properties (i.e. the one pointing at the <c>.psmdcp</c> file)
/// has its <c>Target</c> attribute updated to the normalised path.
///
/// The manifest-pointer <c>&lt;Relationship&gt;</c> (whose <c>Type</c> attribute
/// identifies the NuGet package manifest) is <b>left untouched</b> — the spike
/// (<c>/tmp/phase9c-spike/</c>) confirmed that its <c>Id</c> and <c>Target</c>
/// are byte-stable across packs, so rewriting them would introduce divergence
/// that the SDK does not produce (LD-22 / FR-3020 step e / spec §6 risk register).
///
/// XML parsing uses <see cref="XmlReaderSettings"/> with
/// <see cref="DtdProcessing.Prohibit"/> and a null <see cref="XmlResolver"/>
/// as defence in depth — .NET 6+ already prohibits DTDs implicitly, but the
/// explicit settings document the intent and survive future framework changes.
/// </summary>
internal static class RelsRewriter
{
    // Type URI that identifies the core-properties (.psmdcp) relationship.
    private const string CorePropertiesType =
        "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";

    private static readonly XmlReaderSettings SecureReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    /// <summary>
    /// Parses <paramref name="xml"/> as the <c>_rels/.rels</c> document, updates
    /// the <c>Target</c> attribute of the single core-properties
    /// <c>&lt;Relationship&gt;</c> to <paramref name="newTarget"/>, and serialises
    /// back. Whitespace preservation and duplicate-namespace suppression match the
    /// spec pseudocode (plan.md §3.2).
    /// </summary>
    public static string RewritePsmdcpTarget(string xml, string newTarget)
    {
        XDocument doc;
        using (var sr = new StringReader(xml))
        using (var reader = XmlReader.Create(sr, SecureReaderSettings))
        {
            doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }

        var rel = doc.Descendants()
            .Where(e => e.Name.LocalName == "Relationship")
            .Single(e => string.Equals(
                (string?)e.Attribute("Type"),
                CorePropertiesType,
                StringComparison.Ordinal));

        rel.SetAttributeValue("Target", newTarget);
        // Normalize the Id attribute to a fixed constant — the SDK emits a random GUID-derived
        // value here on every pack. NuGet consumers do not depend on the Id value of the
        // core-properties relationship; normalising it to "R1" makes the entry byte-stable.
        rel.SetAttributeValue("Id", "R1");

        using var sw = new StringWriter();
        doc.Save(sw, SaveOptions.DisableFormatting | SaveOptions.OmitDuplicateNamespaces);
        return sw.ToString();
    }
}
