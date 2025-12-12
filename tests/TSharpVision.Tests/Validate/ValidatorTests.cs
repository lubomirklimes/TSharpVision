// The streaming round-trip checks require a live registry → NonParallel collection.
using System.IO;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Validate;

[Collection("NonParallel")]
public sealed class ValidatorTests : IDisposable
{
    private readonly StreamableRegistryScope _registry;
    private readonly DriverScope _driver;

    public ValidatorTests()
    {
        _registry = new StreamableRegistryScope();
        _driver   = new DriverScope();
        TValidator.RegisterStreamableTypes();
    }

    public void Dispose()
    {
        _driver.Dispose();
        _registry.Dispose();
    }

    // ── TInputLine without validator ──────────────────────────────────────

    [Fact]
    public void TInputLine_NoValidator_ValidOk()
    {
        var il = new TInputLine(new TRect(0, 0, 12, 1), 16);
        il.Data = "anything";
        Assert.True(il.Valid(Views.cmOK));
    }

    [Fact]
    public void TInputLine_NoValidator_ValidCancel()
    {
        var il = new TInputLine(new TRect(0, 0, 12, 1), 16);
        il.Data = "anything";
        Assert.True(il.Valid(Views.cmCancel));
    }

    // ── TFilterValidator ──────────────────────────────────────────────────

    [Fact]
    public void TFilterValidator_ValidChars_Passes()
    {
        var fv = new TFilterValidator("abc123");
        Assert.True(fv.IsValid("a1b2c3"));
    }

    [Fact]
    public void TFilterValidator_InvalidChar_Fails()
    {
        var fv = new TFilterValidator("abc123");
        Assert.False(fv.IsValid("aXb"));
    }

    [Fact]
    public void TFilterValidator_EmptyString_Passes()
    {
        var fv = new TFilterValidator("abc123");
        Assert.True(fv.IsValid(""));
    }

    [Fact]
    public void TFilterValidator_IsValidInput_MatchesIsValid()
    {
        var fv = new TFilterValidator("abc123");
        Assert.Equal(fv.IsValid("abc"), fv.IsValidInput("abc", false));
    }

    [Fact]
    public void TFilterValidator_IsValid_NullIsTrue()
    {
        var fv = new TFilterValidator("abc");
        Assert.True(fv.IsValid(null));
    }

    // ── TRangeValidator ───────────────────────────────────────────────────

    [Fact]
    public void TRangeValidator_Middle_Passes()
    {
        Assert.True(new TRangeValidator(1, 100).IsValid("50"));
    }

    [Fact]
    public void TRangeValidator_LowerBound_Passes()
    {
        Assert.True(new TRangeValidator(1, 100).IsValid("1"));
    }

    [Fact]
    public void TRangeValidator_UpperBound_Passes()
    {
        Assert.True(new TRangeValidator(1, 100).IsValid("100"));
    }

    [Fact]
    public void TRangeValidator_BelowMin_Fails()
    {
        Assert.False(new TRangeValidator(1, 100).IsValid("0"));
    }

    [Fact]
    public void TRangeValidator_AboveMax_Fails()
    {
        Assert.False(new TRangeValidator(1, 100).IsValid("101"));
    }

    [Fact]
    public void TRangeValidator_Empty_Fails()
    {
        Assert.False(new TRangeValidator(1, 100).IsValid(""));
    }

    [Fact]
    public void TRangeValidator_NonNumeric_Fails()
    {
        Assert.False(new TRangeValidator(1, 100).IsValid("abc"));
    }

    [Fact]
    public void TRangeValidator_Negative_Passes()
    {
        Assert.True(new TRangeValidator(-50, 50).IsValid("-10"));
    }

    [Fact]
    public void TRangeValidator_BelowNegativeMin_Fails()
    {
        Assert.False(new TRangeValidator(-50, 50).IsValid("-51"));
    }

    [Fact]
    public void TRangeValidator_HexInRange_Passes()
    {
        Assert.True(new TRangeValidator(0, 255).IsValid("0xFF"));
    }

    [Fact]
    public void TRangeValidator_HexOutOfRange_Fails()
    {
        Assert.False(new TRangeValidator(0, 255).IsValid("0x100"));
    }

    // ── TPXPictureValidator ───────────────────────────────────────────────

    [Fact]
    public void TPXPicture_HashMask_Accepts()
    {
        Assert.True(new TPXPictureValidator("###").IsValid("123"));
    }

    [Fact]
    public void TPXPicture_HashMask_RejectsLetters()
    {
        Assert.False(new TPXPictureValidator("###").IsValid("abc"));
    }

    [Fact]
    public void TPXPicture_HashMask_RejectsTooShort()
    {
        Assert.False(new TPXPictureValidator("###").IsValid("12"));
    }

    [Fact]
    public void TPXPicture_HashMask_RejectsTooLong()
    {
        Assert.False(new TPXPictureValidator("###").IsValid("1234"));
    }

    [Fact]
    public void TPXPicture_AtMask_AcceptsAlpha()
    {
        Assert.True(new TPXPictureValidator("@@@").IsValid("abc"));
    }

    [Fact]
    public void TPXPicture_AtMask_AcceptsDigits()
    {
        Assert.True(new TPXPictureValidator("@@@").IsValid("123"));
    }

    [Fact]
    public void TPXPicture_AtMask_RejectsEmpty()
    {
        Assert.False(new TPXPictureValidator("@@@").IsValid(""));
    }

    [Fact]
    public void TPXPicture_LiteralMask_Valid()
    {
        Assert.True(new TPXPictureValidator("??-##-####").IsValid("AB-12-3456"));
    }

    [Fact]
    public void TPXPicture_LiteralMask_Invalid()
    {
        Assert.False(new TPXPictureValidator("??-##-####").IsValid("A1-12-3456"));
    }

    [Fact]
    public void TPXPicture_BadSyntax_StatusVsSyntax()
    {
        var pvBad = new TPXPictureValidator("{bad");
        Assert.Equal(TValidator.VsSyntax, pvBad.Status);
        Assert.False(pvBad.IsValid("anything"));
    }

    // ── TStringLookupValidator ────────────────────────────────────────────

    [Fact]
    public void TStringLookup_Hit()
    {
        var col = new TStringCollection();
        col.Insert("alpha"); col.Insert("bravo"); col.Insert("charlie");
        var sv = new TStringLookupValidator(col);
        Assert.True(sv.IsValid("alpha"));
    }

    [Fact]
    public void TStringLookup_Miss()
    {
        var col = new TStringCollection();
        col.Insert("alpha"); col.Insert("bravo"); col.Insert("charlie");
        var sv = new TStringLookupValidator(col);
        Assert.False(sv.IsValid("delta"));
    }

    [Fact]
    public void TStringLookup_Empty_Fails()
    {
        var col = new TStringCollection();
        col.Insert("alpha");
        Assert.False(new TStringLookupValidator(col).IsValid(""));
    }

    [Fact]
    public void TStringLookup_Null_Fails()
    {
        var col = new TStringCollection();
        col.Insert("alpha");
        Assert.False(new TStringLookupValidator(col).IsValid(null));
    }

    // ── TInputLine with validator ─────────────────────────────────────────

    [Fact]
    public void TInputLine_Validator_ValidOk()
    {
        var col = new TStringCollection();
        col.Insert("yes");
        var il = new TInputLine(new TRect(0, 0, 12, 1), 16);
        il.Validator = new TStringLookupValidator(col);
        il.Data = "yes";
        Assert.True(il.Valid(Views.cmOK));
    }

    [Fact]
    public void TInputLine_Validator_ValidCancel_AlwaysTrue()
    {
        var col = new TStringCollection();
        col.Insert("yes");
        var il = new TInputLine(new TRect(0, 0, 12, 1), 16);
        il.Validator = new TStringLookupValidator(col);
        il.Data = "maybe";
        Assert.True(il.Valid(Views.cmCancel));
    }

    // ── Edge-case base TValidator ─────────────────────────────────────────

    [Fact]
    public void TValidator_BaseValidate_EmptyString()
    {
        Assert.True(new TValidator().Validate(""));
    }

    // ── Streaming round-trip ──────────────────────────────────────────────

    [Fact]
    public void TFilterValidator_StreamRoundTrip()
    {
        using var ms = new MemoryStream();
        var fv = new TFilterValidator("XYZ");
        fv.Options = TValidator.VoFill;

        var os = new Opstream(ms);
        os.WritePointer(fv);

        ms.Position = 0;
        var isStream = new Ipstream(ms);
        var fv2 = isStream.ReadPointer() as TFilterValidator;

        Assert.NotNull(fv2);
        Assert.True(fv2.IsValid("XY"));
        Assert.False(fv2.IsValid("A"));
        Assert.Equal(TValidator.VoFill, fv2.Options);
    }
}
