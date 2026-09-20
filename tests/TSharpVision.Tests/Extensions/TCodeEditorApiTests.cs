using System.Reflection;
using TSharpVision.CodeEditor;
using TSharpVision.Tests.Infrastructure;
using Xunit;
using CodeEditorControl = TSharpVision.CodeEditor.TCodeEditor;

namespace TSharpVision.Tests.Extensions;

public sealed class TCodeEditorApiTests
{
    [Fact]
    public void DrawUsesProtectedVirtualDrawCodeLinesHook()
    {
        using var driver = new DriverScope();
        var editor = new DrawProbe();

        editor.Draw();

        Assert.True(editor.Called);
        MethodInfo method = typeof(CodeEditorControl).GetMethod(
            "DrawCodeLines", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(method.IsFamily);
        Assert.True(method.IsVirtual);
        Assert.False(method.IsFinal);
    }

    [Fact]
    public void ExplicitNullOpenOptionsUsesDefaults()
    {
        using var driver = new DriverScope();
        var editor = new CodeEditorControl(
            new TRect(0, 0, 20, 5), null, null, null, null, null);

        Assert.True(editor.isValid);
    }

    private sealed class DrawProbe : CodeEditorControl
    {
        public bool Called { get; private set; }

        public DrawProbe()
            : base(new TRect(0, 0, 20, 5), null, null, null, string.Empty)
        {
        }

        protected override void DrawCodeLines(int y, int count, uint linePtr)
        {
            Called = true;
            Assert.Equal(0, y);
            Assert.Equal(size.y, count);
        }
    }
}
