//
// Group A (22e-1..22e-7): verify TEditorApp ctor initialises core components
//   and installs editorDialog.  Each test wraps its own DriverScope.
//
// Group B (22e-8..22e-11): verify that Install(null) returns graceful
//   cmCancel / no-throw for all dialog IDs it guards.

using System;
using System.Collections.Generic;
using SharpVision;
using SharpVision.Constants;
using SharpVision.Tests.Infrastructure;
using Xunit;

namespace SharpVision.Tests.Editors;

[Collection("NonParallel")]
public sealed class EditorAppDialogHelperTests : IDisposable
{
    private readonly DriverScope _ds;
    public EditorAppDialogHelperTests() => _ds = new DriverScope();
    public void Dispose()
    {
        // Force any pending TApplication/TScreen finalizers to run while the
        // NullDriver is still installed.  Without this, GC can run them after
        // _ds.Dispose() nulls TDisplay.driver, causing a NullReferenceException
        // inside TScreen.Suspend().
        GC.Collect();
        GC.WaitForPendingFinalizers();
        _ds.Dispose();
    }

    // ── menu / status line walkers (private helpers) ──────────────────────────

    private static void CollectMenuCommands(TMenu menu, List<ushort> commands)
    {
        if (menu == null) return;
        var item = menu.Items;
        while (item != null)
        {
            if (item.Command != 0)
                commands.Add(item.Command);
            if (item.SubMenu != null)
                CollectMenuCommands(item.SubMenu, commands);
            item = item.Next;
        }
    }

    private static List<ushort> CollectStatusCommands(TStatusLine sl)
    {
        var cmds = new List<ushort>();
        var def = sl?.Defs;
        while (def != null)
        {
            var item = def.Items;
            while (item != null)
            {
                if (item.Command != 0)
                    cmds.Add(item.Command);
                item = item.Next;
            }
            def = def.Next;
        }
        return cmds;
    }

    // ── Group A — TEditorApp construction ─────────────────────────────────────

    [Fact]
    public void TEditorApp_Ctor_NoException()
    {
        TEditorApp app = null;
        var ex = Record.Exception(() => app = new TEditorApp());
        Assert.Null(ex);
        app?.ShutDown();
    }

    [Fact]
    public void TEditorApp_MenuBar_NonNull()
    {
        var app = new TEditorApp();
        try { Assert.NotNull(app.MenuBar); }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void TEditorApp_StatusLine_NonNull()
    {
        var app = new TEditorApp();
        try { Assert.NotNull(app.StatusLine); }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void TEditorApp_DeskTop_NonNull()
    {
        var app = new TEditorApp();
        try { Assert.NotNull(app.DeskTop); }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void TEditorApp_EditorDialogInstalled()
    {
        var app = new TEditorApp();
        try { Assert.NotNull(TEditor.editorDialog); }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void TEditorApp_Menu_ContainsRequiredCommands()
    {
        var app = new TEditorApp();
        try
        {
            var commands = new List<ushort>();
            CollectMenuCommands(app.MenuBar?.Menu, commands);
            Assert.Contains(Views.cmNew,  commands);
            Assert.Contains(Views.cmOpen, commands);
            Assert.Contains(Views.cmSave, commands);
            Assert.Contains(Views.cmFind, commands);
            Assert.Contains(Views.cmTile, commands);
            Assert.Contains(Views.cmQuit, commands);
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void TEditorApp_StatusLine_ContainsRequiredCommands()
    {
        var app = new TEditorApp();
        try
        {
            var commands = CollectStatusCommands(app.StatusLine);
            Assert.Contains(Views.cmSave, commands);
            Assert.Contains(Views.cmOpen, commands);
            Assert.Contains(Views.cmQuit, commands);
            Assert.Contains(Views.cmNext, commands);
        }
        finally { app.ShutDown(); }
    }

    // ── Group B — TEditorDialogHelper.Install(null) guard tests ──────────────

    /// <summary>Save and restore TEditor.editorDialog around a test body.</summary>
    private static void WithSavedDialog(Action body)
    {
        var saved = TEditor.editorDialog;
        try { body(); }
        finally { TEditor.editorDialog = saved; }
    }

    [Fact]
    public void Install_Null_EdFind_ReturnsCmCancel()
    {
        WithSavedDialog(() =>
        {
            TEditorDialogHelper.Install(null);
            var rec = new TFindDialogRec(new byte[80], 0);
            ushort result = TEditor.editorDialog(Views.edFind, rec);
            Assert.Equal(Views.cmCancel, result);
        });
    }

    [Fact]
    public void Install_Null_EdSaveUntitled_ReturnsCmCancel()
    {
        WithSavedDialog(() =>
        {
            TEditorDialogHelper.Install(null);
            ushort result = TEditor.editorDialog(Views.edSaveUntitled, null);
            Assert.Equal(Views.cmCancel, result);
        });
    }

    [Fact]
    public void Install_Null_EdSearchFailed_NoThrow()
    {
        WithSavedDialog(() =>
        {
            TEditorDialogHelper.Install(null);
            var ex = Record.Exception(() =>
                TEditor.editorDialog(Views.edSearchFailed, null));
            Assert.Null(ex);
        });
    }

    [Fact]
    public void Install_Null_EdReplace_ReturnsCmCancel()
    {
        WithSavedDialog(() =>
        {
            TEditorDialogHelper.Install(null);
            var rec = new TReplaceDialogRec(new byte[80], new byte[80], 0);
            ushort result = TEditor.editorDialog(Views.edReplace, rec);
            Assert.Equal(Views.cmCancel, result);
        });
    }
}
