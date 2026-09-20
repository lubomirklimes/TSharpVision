using System.IO;
using TSharpVision;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Streams;

[Collection("NonParallel")]
public sealed class AwakenCompatibilityTests : IDisposable
{
    private const string ViewTypeName = "PhaseDAwakenView";
    private const string GroupTypeName = "PhaseDAwakenGroup";
    private readonly StreamableRegistryScope _registry = new();

    public void Dispose() => _registry.Dispose();

    [Fact]
    public void NormalConstructionDoesNotAwaken()
    {
        AwakenLog.Entries.Clear();

        _ = new AwakenView("ordinary");
        _ = new AwakenGroup("group");

        Assert.Empty(AwakenLog.Entries);
    }

    [Fact]
    public void StreamedNestedGraphAwakensOnceInBorlandChildOrderAfterLinkage()
    {
        RegisterTypes();
        var root = new AwakenGroup("root");
        var first = new AwakenView("A");
        var nested = new AwakenGroup("nested");
        var second = new AwakenView("B");
        var third = new AwakenView("C");
        nested.Insert(second);
        nested.Insert(third);
        root.Insert(first);
        root.Insert(nested);
        using var stream = new MemoryStream();
        new Opstream(stream).WritePointer(root).Flush();
        AwakenLog.Entries.Clear();
        stream.Position = 0;

        var restored = Assert.IsType<AwakenGroup>(new Ipstream(stream).ReadPointer());

        Assert.Equal(
            ["root:root:detached", "group:nested:owned", "view:C:owned", "view:B:owned", "view:A:owned"],
            AwakenLog.Entries);
        Assert.Equal(5, AwakenLog.Entries.Count);
        Assert.Null(restored.owner);
    }

    [Fact]
    public void ReadObjectIntoExistingGroupInvokesDerivedAwakenOnce()
    {
        RegisterTypes();
        var source = new AwakenGroup("source");
        source.Insert(new AwakenView("child"));
        using var stream = new MemoryStream();
        new Opstream(stream).WriteObject(source).Flush();
        stream.Position = 0;
        var target = new AwakenGroup("target");
        AwakenLog.Entries.Clear();

        new Ipstream(stream).ReadObject(target);

        Assert.Equal(["root:source:detached", "view:child:owned"], AwakenLog.Entries);
    }

    private static void RegisterTypes()
    {
        Pstream.DeInitTypes();
        _ = new TStreamableClass(ViewTypeName, () => new AwakenView(), 0);
        _ = new TStreamableClass(GroupTypeName, () => new AwakenGroup(), 0);
    }

    private static class AwakenLog
    {
        internal static List<string> Entries { get; } = [];
    }

    private sealed class AwakenView : TView
    {
        private string _label = string.Empty;

        internal AwakenView(string label) : base(new TRect(0, 0, 1, 1)) => _label = label;
        internal AwakenView() : base(StreamableInit.streamableInit) { }

        public override void Awaken()
            => AwakenLog.Entries.Add($"view:{_label}:{(owner == null ? "detached" : "owned")}");

        public override void Write(Opstream stream)
        {
            base.Write(stream);
            stream.WriteString(_label);
        }

        public override object Read(Ipstream stream)
        {
            base.Read(stream);
            _label = stream.ReadString()
                ?? throw new InvalidDataException("AwakenView label is missing from the stream.");
            return this;
        }

        public override string streamableName => ViewTypeName;
    }

    private sealed class AwakenGroup : TGroup
    {
        private string _label = string.Empty;

        internal AwakenGroup(string label) : base(new TRect(0, 0, 10, 5)) => _label = label;
        internal AwakenGroup() : base(StreamableInit.streamableInit) { }

        public override void Awaken()
        {
            AwakenLog.Entries.Add($"{(owner == null ? "root" : "group")}:{_label}:{(owner == null ? "detached" : "owned")}");
            base.Awaken();
        }

        public override void Write(Opstream stream)
        {
            base.Write(stream);
            stream.WriteString(_label);
        }

        public override object Read(Ipstream stream)
        {
            base.Read(stream);
            _label = stream.ReadString()
                ?? throw new InvalidDataException("AwakenGroup label is missing from the stream.");
            return this;
        }

        public override string streamableName => GroupTypeName;
    }
}
