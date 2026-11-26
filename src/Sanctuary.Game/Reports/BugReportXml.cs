using System;
using System.IO;
using System.Xml;

using Sanctuary.Game.ChatCommands;

namespace Sanctuary.Game.Reports;

/// <summary>What the bug report window's XML says, cleaned and capped. Any field can be empty.</summary>
public sealed record BugReportFields(string Category, string SubCategory, string Severity, string Description);

/// <summary>
/// Reads the client's bug report XML (root <c>BugSubmission</c> with the attributes Category, SubCategory, Severity and
/// BugDescription). The XML is from a client that may not be the real one, so it is read with an <see cref="XmlReader"/>
/// that refuses DTDs and external entities and stops at a size cap: no regular expression, nothing is resolved or fetched.
/// BubbaJoeX's version (982a800) matched attributes with a regex whose <c>\b</c> sat in a non-verbatim string, so it never
/// matched.
/// </summary>
public static class BugReportXml
{
    public static bool TryParse(string? xml, out BugReportFields fields)
    {
        fields = new BugReportFields(string.Empty, string.Empty, string.Empty, string.Empty);

        // A document over the cap isn't read at all; the packet is no longer than 64 KiB, so this is a cheap, early no.
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > ReportLimits.MaxBugXmlLength)
            return false;

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = ReportLimits.MaxBugXmlLength,
            MaxCharactersFromEntities = 0,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            CloseInput = true
        };

        try
        {
            using var stringReader = new StringReader(xml);
            using var reader = XmlReader.Create(stringReader, settings);

            // The root is the first element; a document that starts with anything else is not the client's.
            if (!reader.ReadToFollowing("BugSubmission") || reader.Depth != 0)
                return false;

            fields = new BugReportFields(
                Field(reader, "Category", ReportLimits.MaxBugCategoryLength),
                Field(reader, "SubCategory", ReportLimits.MaxBugCategoryLength),
                Field(reader, "Severity", ReportLimits.MaxBugSeverityLength),
                Field(reader, "BugDescription", ReportLimits.MaxBugDescriptionLength));

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>A cleaned, cut copy of text that could not be read as XML.</summary>
    public static string RawCopy(string? xml) => ModArguments.CleanText(xml, ReportLimits.MaxRawBugLength) ?? string.Empty;

    private static string Field(XmlReader reader, string name, int maxLength) =>
        ModArguments.CleanText(reader.GetAttribute(name), maxLength) ?? string.Empty;
}
