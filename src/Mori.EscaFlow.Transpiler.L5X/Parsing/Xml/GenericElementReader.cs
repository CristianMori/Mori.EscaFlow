using System.Xml;
using System.Xml.Linq;
using Mori.EscaFlow.Transpiler.L5X.Model;

namespace Mori.EscaFlow.Transpiler.L5X.Parsing.Xml;

/// <summary>Copies an XML subtree into <see cref="GenericElement"/> objects.</summary>
public static class GenericElementReader
{
    /// <summary>Reads the current element (and its subtree) and moves the reader past its end tag.</summary>
    public static GenericElement Read(XmlReader reader, bool skipCustomProperties = true, Action? onSkippedCustomProperties = null)
    {
        if (reader.NodeType != XmlNodeType.Element) throw new InvalidOperationException("Reader must be on an element");
        var element = new GenericElement
        {
            Name = reader.LocalName,
            SourceLine = reader.LineNumber(),
        };
        if (reader.HasAttributes)
        {
            reader.MoveToFirstAttribute();
            do
            {
                if (reader.Prefix != "xmlns" && reader.Name != "xmlns") element.Attributes[reader.Name] = reader.Value;
            } while (reader.MoveToNextAttribute());
            reader.MoveToElement();
        }
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return element;
        }
        var depth = reader.Depth;
        reader.Read();
        System.Text.StringBuilder? text = null;
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                reader.Read();
                break;
            }
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    if (skipCustomProperties && reader.LocalName == "CustomProperties")
                    {
                        onSkippedCustomProperties?.Invoke();
                        reader.Skip();
                    }
                    else
                    {
                        element.Children.Add(Read(reader, skipCustomProperties, onSkippedCustomProperties));
                    }
                    continue;
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                    (text ??= new System.Text.StringBuilder()).Append(reader.Value);
                    break;
            }
            reader.Read();
        }
        if (text is not null)
        {
            var t = CdataText.Normalize(text.ToString());
            if (t.Length > 0 || element.Children.Count == 0) element.Text = t;
        }
        return element;
    }

    public static GenericElement Read(XElement element, bool skipCustomProperties = true)
    {
        var g = new GenericElement
        {
            Name = element.Name.LocalName,
            SourceLine = element.Line(),
        };
        foreach (var a in element.Attributes())
        {
            if (a.IsNamespaceDeclaration) continue;
            g.Attributes[a.Name.LocalName] = a.Value;
        }
        var hasChildren = false;
        foreach (var child in element.Elements())
        {
            if (skipCustomProperties && child.Name.LocalName == "CustomProperties") continue;
            g.Children.Add(Read(child, skipCustomProperties));
            hasChildren = true;
        }
        if (!hasChildren)
        {
            var t = CdataText.Normalize(element.Value);
            g.Text = t;
        }
        else
        {
            var direct = string.Concat(element.Nodes().OfType<XText>().Select(n => n.Value));
            var t = CdataText.Normalize(direct);
            if (t.Trim().Length > 0) g.Text = t;
        }
        return g;
    }
}
