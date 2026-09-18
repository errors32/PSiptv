using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace PSiptv.Core;

public static class XmlTvParser
{
    public static IReadOnlyList<TvProgramme> Parse(string xml)
    {
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 40_000_000
        });
        var result = new List<TvProgramme>();
        reader.MoveToContent();
        if (reader.LocalName != "tv") throw new InvalidOperationException("A fonte não contém um guia XMLTV válido.");
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "programme")
            {
                var item = (XElement)XNode.ReadFrom(reader);
                if (TryDate((string?)item.Attribute("start"), out var start) &&
                    TryDate((string?)item.Attribute("stop"), out var end) && end > start)
                    result.Add(new((string?)item.Attribute("channel") ?? "", (string?)item.Element("title") ?? "Sem título",
                        (string?)item.Element("desc") ?? "", start, end));
            }
            else reader.Read();
        }
        return result;
    }

    public static bool TryDate(string? value, out DateTimeOffset date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var zone = parts.Length > 1 ? parts[1] : "+0000";
        if (zone.Length == 5) zone = zone.Insert(3, ":");
        return DateTimeOffset.TryParseExact($"{parts[0]} {zone}", "yyyyMMddHHmmss zzz",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
