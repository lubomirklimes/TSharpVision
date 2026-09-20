using TSharpVision.Tests.Infrastructure;
using Xunit;
namespace TSharpVision.Tests.Streams;

[Collection("NonParallel")]
public sealed class ProvenanceResourcePackTests
{
    sealed class Payload : TStreamable
    {
        public string Text = "";
        public override string streamableName => "Phase25cPayload";
        public override void Write(Opstream stream) => stream.WriteString(Text);
        public override object Read(Ipstream stream)
        {
            Text = stream.ReadString()
                ?? throw new InvalidDataException("Payload text is missing from the stream.");
            return this;
        }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Pack_PreservesLiveBytesOrdersOffsetsTruncatesAndRepeats(bool empty)
    {
        using var registry = new StreamableRegistryScope(); using var temp = new TempDirectory();
        new TStreamableClass("Phase25cPayload", () => new Payload(), 0);
        Pstream.RegisterType(TResourceCollection.StreamableClass);
        string path = Path.Combine(temp.Path, "pack.tvr");
        var stream = new Fpstream(path);
        try
        {
            var resources = new TResourceFile(stream);
            resources.Put(new Payload { Text = new string('d', 1000) }, "deleted");
            resources.Put(new Payload { Text = "z-last-key-first-in-file" }, "z");
            resources.Put(new Payload { Text = "a-first-key-last-in-file" }, "a");
            resources.Flush(); long before = stream.Filelength();
            byte[] a = Assert.IsType<byte[]>(resources.GetRawBytes("a"));
            byte[] z = Assert.IsType<byte[]>(resources.GetRawBytes("z"));
            resources.Remove("deleted"); if (empty) { resources.Remove("a"); resources.Remove("z"); }
            resources.Pack(); long packed = stream.Filelength(); Assert.True(packed < before);
            if (!empty)
            {
                Assert.Equal("a", resources.KeyAt(0)); Assert.Equal("z", resources.KeyAt(1));
                Assert.Equal(12L, resources.ItemAt(1).pos);
                Assert.Equal(12L + resources.ItemAt(1).size, resources.ItemAt(0).pos);
                Assert.Equal(a, resources.GetRawBytes("a")); Assert.Equal(z, resources.GetRawBytes("z"));
            }
            else Assert.Equal(0, resources.Count());
            resources.Pack(); Assert.Equal(packed, stream.Filelength());
        }
        finally { stream.Close(); }
        byte[] bytes = File.ReadAllBytes(path);
        var reopened = new Fpstream(path);
        try
        {
            var resources = new TResourceFile(reopened); Assert.Equal(empty ? 0 : 2, resources.Count());
            Assert.Null(resources.Get("deleted"));
            if (!empty) Assert.Equal("a-first-key-last-in-file", Assert.IsType<Payload>(resources.Get("a")).Text);
            Assert.Equal(bytes.Length - 8, BitConverter.ToInt32(bytes, 4));
            long endOfPayloads = empty ? 12 : resources.ItemAt(0).pos + resources.ItemAt(0).size;
            Assert.Equal(endOfPayloads, BitConverter.ToInt32(bytes, 8));
        }
        finally { reopened.Close(); }
    }
}
