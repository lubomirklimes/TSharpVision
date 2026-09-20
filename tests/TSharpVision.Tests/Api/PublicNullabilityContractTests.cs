// Phase F2.5a.2: pins the corrected public/protected nullable metadata so the
// contract cannot silently regress during the resumed F2.5a implementation cleanup.
using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace TSharpVision.Tests.Api;

public sealed class PublicNullabilityContractTests
{
    private static readonly NullabilityInfoContext Context = new();

    private static NullabilityInfo ParameterInfoFor(MethodBase method, string parameterName)
    {
        var parameter = Array.Find(method.GetParameters(), p => p.Name == parameterName);
        return Context.Create(Assert.IsAssignableFrom<ParameterInfo>(parameter));
    }

    private static MethodInfo MethodOf(Type type, string name)
    {
        var method = type.GetMethod(
            name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        return Assert.IsAssignableFrom<MethodInfo>(method);
    }

    [Fact]
    public void WritePointer_AcceptsNullStreamable()
    {
        var info = ParameterInfoFor(MethodOf(typeof(Opstream), nameof(Opstream.WritePointer)), "t");

        Assert.Equal(NullabilityState.Nullable, info.WriteState);
    }

    [Fact]
    public void StreamableTypesLookup_ReturnsNullableClass()
    {
        var method = MethodOf(typeof(TStreamableTypes), nameof(TStreamableTypes.Lookup));

        Assert.Equal(NullabilityState.Nullable, Context.Create(method.ReturnParameter).ReadState);
        Assert.Equal(
            NullabilityState.NotNull,
            ParameterInfoFor(method, "name").WriteState);
    }

    [Theory]
    [InlineData(typeof(TStatusItem), nameof(TStatusItem.Next))]
    [InlineData(typeof(TMenuItem), nameof(TMenuItem.Next))]
    public void ChainLinkProperties_AreNullable(Type declaringType, string propertyName)
    {
        var property = declaringType.GetProperty(propertyName);
        Assert.Equal(
            NullabilityState.Nullable,
            Context.Create(Assert.IsAssignableFrom<PropertyInfo>(property)).ReadState);
    }

    [Theory]
    [InlineData(nameof(TView.DrawUnderRect))]
    [InlineData(nameof(TView.DrawUnderView))]
    [InlineData(nameof(TView.DrawHide))]
    [InlineData(nameof(TView.DrawShow))]
    public void DrawBoundaryParameters_AcceptNullLastView(string methodName)
    {
        var info = ParameterInfoFor(MethodOf(typeof(TView), methodName), "lastView");

        Assert.Equal(NullabilityState.Nullable, info.WriteState);
    }

    [Fact]
    public void DrawSubViews_AcceptsNullTraversalBoundaries()
    {
        var method = MethodOf(typeof(TGroup), nameof(TGroup.DrawSubViews));

        Assert.Equal(NullabilityState.Nullable, ParameterInfoFor(method, "p").WriteState);
        Assert.Equal(NullabilityState.Nullable, ParameterInfoFor(method, "bottom").WriteState);
    }

    [Fact]
    public void SetCurrent_AcceptsNoCurrentView()
    {
        var info = ParameterInfoFor(MethodOf(typeof(TGroup), nameof(TGroup.SetCurrent)), "p");

        Assert.Equal(NullabilityState.Nullable, info.WriteState);
    }

    // Guards the deliberate F2.5a.1 decision: the IndexOf warning belongs to its
    // caller, not to this signature. IndexOf must not be widened.
    [Fact]
    public void IndexOf_RemainsNonNull()
    {
        var info = ParameterInfoFor(MethodOf(typeof(TGroup), nameof(TGroup.IndexOf)), "p");

        Assert.Equal(NullabilityState.NotNull, info.WriteState);
    }

    [Fact]
    public void MissingKeyEvent_AllowsNoSubscribers()
    {
        var field = typeof(TSharpVisionIntl).GetField(
            "MissingKey",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.Equal(
            NullabilityState.Nullable,
            Context.Create(Assert.IsAssignableFrom<FieldInfo>(field)).ReadState);
    }

    // The Try-pattern is expressed with [MaybeNullWhen(false)] rather than `out string?`,
    // so the success path stays non-null while the failure path is truthfully nullable.
    [Fact]
    public void TryGetValue_UsesMaybeNullWhenFalseFailurePath()
    {
        var method = MethodOf(typeof(TStringResource), nameof(TStringResource.TryGetValue));
        var value = Array.Find(method.GetParameters(), p => p.Name == "value");
        value = Assert.IsAssignableFrom<ParameterInfo>(value);
        Assert.True(value.IsOut);
        Assert.Equal(NullabilityState.Nullable, Context.Create(value).ReadState);
        Assert.Contains(
            value.GetCustomAttributes(inherit: false),
            a => a.GetType().Name == "MaybeNullWhenAttribute");
    }

    // Confirms the runtime behaviour behind the [MaybeNullWhen(false)] choice:
    // a successful lookup really does yield a non-null value.
    [Fact]
    public void TryGetValue_SuccessPathYieldsNonNullValue()
    {
        var resource = new TStringResource(new Dictionary<string, string> { ["k"] = "v" });

        Assert.True(resource.TryGetValue("k", out var found));
        Assert.NotNull(found);
        Assert.False(resource.TryGetValue("missing", out var absent));
        Assert.Null(absent);
    }

    // NewSubView was decided parameter-by-parameter: a top-level menu has no parent,
    // but no caller supports a null menu, so aMenu must stay non-null.
    [Fact]
    public void NewSubView_ParentIsNullable_ButMenuRemainsNonNull()
    {
        var method = MethodOf(typeof(TMenuView), nameof(TMenuView.NewSubView));

        Assert.Equal(NullabilityState.Nullable, ParameterInfoFor(method, "aParentMenu").WriteState);
        Assert.Equal(NullabilityState.NotNull, ParameterInfoFor(method, "aMenu").WriteState);
    }

    [Fact]
    public void WriteString_AcceptsTheSerializedNullMarker()
    {
        var info = ParameterInfoFor(MethodOf(typeof(Opstream), nameof(Opstream.WriteString)), "str");

        Assert.Equal(NullabilityState.Nullable, info.WriteState);
    }

    [Fact]
    public void MenuSeparatorName_IsNullable()
    {
        var property = Assert.IsAssignableFrom<PropertyInfo>(typeof(TMenuItem).GetProperty(nameof(TMenuItem.Name)));

        Assert.Equal(NullabilityState.Nullable, Context.Create(property).ReadState);
    }

    [Theory]
    [InlineData(typeof(TChDirDialog), nameof(TChDirDialog.dirInput))]
    [InlineData(typeof(TFileDialog), nameof(TFileDialog.fileList))]
    [InlineData(typeof(THistory), nameof(THistory.Link))]
    public void LifecycleFields_AreNullable(Type declaringType, string fieldName)
    {
        var field = Assert.IsAssignableFrom<FieldInfo>(declaringType.GetField(fieldName));

        Assert.Equal(NullabilityState.Nullable, Context.Create(field).ReadState);
    }

    [Fact]
    public void EditorEncoding_AutomaticPolicyIsNullable()
    {
        var property = Assert.IsAssignableFrom<PropertyInfo>(
            typeof(EditorTextEncoding).GetProperty(nameof(EditorTextEncoding.LegacyEncoding)));

        Assert.Equal(NullabilityState.Nullable, Context.Create(property).ReadState);
    }

    [Fact]
    public void EditorDialogPayload_IsNullable()
    {
        var info = ParameterInfoFor(MethodOf(typeof(TEditor.TEditorDialog), nameof(TEditor.TEditorDialog.Invoke)), "info");

        Assert.Equal(NullabilityState.Nullable, info.WriteState);
    }
}
