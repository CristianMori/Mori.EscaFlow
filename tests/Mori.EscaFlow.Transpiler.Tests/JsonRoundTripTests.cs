using System.Text.Json;
using Xunit;

namespace Mori.EscaFlow.Transpiler.Tests;

/// <summary>Verifies the JSON contract: every node kind serializes and reads back without loss, both the indented and compact forms are stable, and the document-level format/version and strictness checks behave.</summary>
public class JsonRoundTripTests
{
    /// <summary>Serializing the full sample document, reading it back, and serializing again yields byte-identical JSON, in both indented and compact form; this is the strongest guarantee the format offers: no node kind silently drops or reorders data.</summary>
    [Fact]
    public void EveryNodeKindRoundTripsByteIdentically()
    {
        var doc = SampleDocument.Build();
        var json = IrJson.Serialize(doc);
        Assert.Contains("\"format\": \"plc-ir\"", json);
        Assert.Contains("\"irVersion\": \"2.0\"", json);
        Assert.Contains("\"op\": \"ref\"", json);
        Assert.Contains("\"kind\": \"fbCall\"", json);
        Assert.Contains("\"kind\": \"contact\"", json);

        var back = IrJson.Deserialize(json);
        var again = IrJson.Serialize(back);
        Assert.Equal(json, again);

        // Compact form also round-trips.
        var compact = IrJson.Serialize(doc, indented: false);
        Assert.Equal(compact, IrJson.Serialize(IrJson.Deserialize(compact), indented: false));

        Assert.Equal(doc.Types.Count, back.Types.Count);
        Assert.Equal(doc.Pous.Count, back.Pous.Count);
        Assert.Equal(doc.AllVariables().Count(), back.AllVariables().Count());
        var p = back.FindPou("P")!;
        Assert.Equal(doc.Pous[0].Body!.Statements.Count, p.Body!.Statements.Count);
        Assert.IsType<IrLdDiagram>(p.FindAction("Main")!.Body.Diagram);
        Assert.IsType<IrNetDiagram>(p.FindAction("Net")!.Body.Diagram);
        Assert.IsType<IrSfcDiagram>(p.FindAction("Seq")!.Body.Diagram);
    }

    /// <summary>A vendor slot entry reads back as a raw JsonElement (the core model never depends on a vendor's C# type) and converts to a typed view on demand, with the conversion cached so repeated access does not re-deserialize.</summary>
    [Fact]
    public void VendorExtensionComesBackAsJsonElementAndConvertsToTypedView()
    {
        var doc = SampleDocument.Build();
        var back = IrJson.Deserialize(IrJson.Serialize(doc));
        var name = back.FindVariable(SampleDocument.Name)!;
        Assert.IsType<JsonElement>(name.Vendor!["logix"]);
        var ext = name.GetVendor<LogixTestExt>("logix")!;
        Assert.Equal("Name", ext.Path);
        Assert.Equal("ASCII", ext.Radix);
        Assert.IsType<LogixTestExt>(name.Vendor["logix"]); // cached typed view
        Assert.True(back.HasVendor("logix"));
        Assert.Null(name.GetVendor<LogixTestExt>("s7"));
    }

    /// <summary>A document whose <c>format</c> is not "plc-ir" or whose <c>irVersion</c> major is not 2 fails to deserialize; a compatible minor version (2.7) is accepted so the format can grow without breaking readers.</summary>
    [Fact]
    public void FormatAndVersionAreChecked()
    {
        var ex = Assert.Throws<InvalidDataException>(() => IrJson.Deserialize("{\"format\":\"plc-ir\",\"irVersion\":\"9.0\"}"));
        Assert.Contains("9.0", ex.Message);
        Assert.Throws<InvalidDataException>(() => IrJson.Deserialize("{\"format\":\"other\",\"irVersion\":\"2.0\"}"));
        var ok = IrJson.Deserialize("{\"format\":\"plc-ir\",\"irVersion\":\"2.7\"}");
        Assert.Equal("2.7", ok.IrVersion);
    }

    /// <summary>Strict mode (used by the validator's ValidateJson) rejects an unknown JSON member as a parse error; lenient mode (the default, for tolerant tooling) ignores it.</summary>
    [Fact]
    public void StrictReadRejectsUnknownMembers()
    {
        Assert.Throws<JsonException>(() => IrJson.Deserialize("{\"format\":\"plc-ir\",\"irVersion\":\"2.0\",\"bogus\":1}", strict: true));
        var lenient = IrJson.Deserialize("{\"format\":\"plc-ir\",\"irVersion\":\"2.0\",\"bogus\":1}");
        Assert.Equal("plc-ir", lenient.Format);
    }

    /// <summary>A named type prints as a bare string; arrays, strings and pointers print as the object form with their own fields; both forms read back as the same <see cref="IrTypeRef"/> the string shorthand was designed to make writing a named reference painless without losing the richer forms.</summary>
    [Fact]
    public void TypeRefConverterHandlesStringAndObjectForms()
    {
        Assert.Equal("\"DINT\"", IrJson.Serialize(IrTypeRef.Named("DINT"), indented: false));
        var array = IrTypeRef.Array(IrTypeRef.Named("REAL"), new IrDim(0, 9), new IrDim(1, 2));
        var json = IrJson.Serialize(array, indented: false);
        Assert.Equal("{\"kind\":\"array\",\"dims\":[{\"lower\":0,\"upper\":9},{\"lower\":1,\"upper\":2}],\"element\":\"REAL\"}", json);
        var back = IrJson.DeserializeNode<IrTypeRef>(json)!;
        Assert.True(back.IsArray);
        Assert.Equal(2, back.Dims!.Count);
        Assert.Equal(10, back.Dims[0].Count);
        Assert.Equal("REAL", back.LeafName);
        Assert.Equal(array, back);

        var str = IrJson.DeserializeNode<IrTypeRef>("{\"kind\":\"string\",\"length\":20,\"wide\":true}")!;
        Assert.Equal(20, str.Length);
        Assert.True(str.Wide);
        Assert.Equal("WSTRING(20)", str.ToString());

        var named = IrJson.DeserializeNode<IrTypeRef>("\"Motor\"")!;
        Assert.True(named.Is("motor"));

        var ptr = IrJson.DeserializeNode<IrTypeRef>("{\"kind\":\"pointer\",\"to\":\"Motor\"}")!;
        Assert.Equal("Motor", ptr.LeafName);
        Assert.Equal("POINTER TO Motor", ptr.ToString());
        Assert.Equal("{\"kind\":\"any\"}", IrJson.Serialize(IrTypeRef.Any, indented: false));
    }

    /// <summary>The document's by-name/by-id lookups (types, variables including a POU-local one, POUs, I/O channels) find their targets case-insensitively and return null rather than throwing for a missing name.</summary>
    [Fact]
    public void DocumentLookupsWork()
    {
        var doc = SampleDocument.Build();
        Assert.Equal("Motor", doc.FindType("motor")!.Name);
        Assert.Equal("M1", doc.FindVariable(SampleDocument.M1)!.Name);
        Assert.Equal("Force", doc.FindVariable("pou/Blink/Reset/Force")!.Name);
        Assert.Equal("functionBlock", doc.FindPou("blink")!.Kind);
        Assert.Equal("input", doc.FindChannel("io/DI/I")!.Direction);
        Assert.Null(doc.FindVariable("nope"));
    }
}
