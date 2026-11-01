# TSharpVision documentation

These pages cover framework behavior and application development. Member signatures and parameter contracts belong in the XML API documentation.

- [Getting started](getting-started.md), [drivers](drivers.md), [platform support](platform-support.md) and [troubleshooting](troubleshooting.md).
- [Environment variables](environment-variables.md): driver selection, input trace and SDL diagnostics.
- Architecture: [views, focus and drawing](architecture/overview.md), [input and event routing](architecture/event-model.md), [lifecycle and ownership](architecture/lifecycle-and-ownership.md), [streaming and Awaken](architecture/streaming-and-awaken.md), and [group data transfer](architecture/data-transfer.md).
- [Turbo Vision compatibility](compatibility/turbo-vision-compatibility.md).
- [Dialogs and controls](dialogs-and-controls.md).
- [Editors](editors/editor-architecture.md), [terminal widget and child sessions](terminal/terminal-widget.md), [outline views](views/outline.md), [combo boxes](views/combo-box.md), [progress bars](views/progress-bar.md), [group boxes](views/group-box.md), [paged byte and table views](views/paged-views.md), [palette modes](views/palette-modes.md), [physical palettes and display effects](views/display-presets.md), [the color dialog](views/color-dialog.md), [help collections and search](views/help-search.md), and [TVDemo desktop state](views/tvdemo-desktop-state.md).
- [Showing an image in a window](graphics/image-preview.md): pixel graphics under the SDL drivers, `PixelImage` from decoded RGBA, `GImage` fit, nearest/linear, fallback on text drivers.
- [Command-line tools](tools.md): installing `svrc`, `svhc` and `svres` as .NET tools; compiling and inspecting resources and help.
- [Localization](localization.md): built-in framework translations (cs, de, en, es, fr, it, pl, sk), `[localization] language`, switching at run time with `TSharpVisionIntl.UseBuiltInLanguage`, precedence, overrides and adding a translation.
- [Contributing](contributing.md).
- [Diagnostics tools](diagnostics.md).
