using TSharpVision.Constants;

namespace TSharpVision.Samples.TVTerm;

/// <summary>
/// Options → Terminal Colors…
///
/// Shows all 16 ANSI terminal colors with their current VGA nibble mapping
/// and lets the user change each one. Values are stored in
/// <see cref="TVTermConfig.ColorMap"/> (16 bytes: index 0–7 = standard colors,
/// 8–15 = bright colors). Changes take effect for newly created terminal
/// windows once AnsiTerminalParser supports custom tables (core extension).
/// </summary>
public static class ColorMappingDialog
{
    // ANSI color labels + default VGA nibble (foreground) values.
    // Order matches AnsiTerminalParser: colors 30–37 then 90–97.
    private static readonly (string Name, string AnsiCode, byte DefaultVga)[] Entries =
    {
        ("Black",         "30 / 40",  Colors.fgBlack),
        ("Red",           "31 / 41",  Colors.fgRed),
        ("Green",         "32 / 42",  Colors.fgGreen),
        ("Brown/Yellow",  "33 / 43",  Colors.fgBrown),
        ("Blue",          "34 / 44",  Colors.fgBlue),
        ("Magenta",       "35 / 45",  Colors.fgMagenta),
        ("Cyan",          "36 / 46",  Colors.fgCyan),
        ("Light Gray",    "37 / 47",  Colors.fgLightGray),
        ("Dark Gray",     "90 / 100", Colors.fgDarkGray),
        ("Light Red",     "91 / 101", Colors.fgLightRed),
        ("Light Green",   "92 / 102", Colors.fgLightGreen),
        ("Yellow",        "93 / 103", Colors.fgYellow),
        ("Light Blue",    "94 / 104", Colors.fgLightBlue),
        ("Light Magenta", "95 / 105", Colors.fgLightMagenta),
        ("Light Cyan",    "96 / 106", Colors.fgLightCyan),
        ("White",         "97 / 107", Colors.fgWhite),
    };

    public static void ShowDialog(TGroup parent, TVTermConfig cfg)
    {
        // Ensure ColorMap has 16 entries; fill defaults if absent/short.
        byte[] map = LoadOrDefault(cfg);

        // ── Dialog layout: 72×22 ─────────────────────────────────────────────
        var dlg = new TDialog(new TRect(0, 0, 72, 22), "Terminal Colors");
        dlg.options |= Views.ofCentered;

        dlg.Insert(new TStaticText(new TRect(2, 1, 70, 2),
            "VGA nibble for each ANSI color (hex 00–0F). '?' = unknown/custom."));

        // Column headers
        dlg.Insert(new TStaticText(new TRect(2,  2, 22, 3), "Color name"));
        dlg.Insert(new TStaticText(new TRect(22, 2, 34, 3), "ANSI code"));
        dlg.Insert(new TStaticText(new TRect(34, 2, 40, 3), "VGA"));
        dlg.Insert(new TStaticText(new TRect(40, 2, 70, 3), "Description"));

        // One TInputLine per color (VGA nibble as 2-char hex).
        var inputs = new TInputLine[16];
        for (int i = 0; i < 16; i++)
        {
            int row = 3 + i;
            var (name, ansi, _) = Entries[i];
            dlg.Insert(new TStaticText(new TRect(2,  row, 22, row + 1), name));
            dlg.Insert(new TStaticText(new TRect(22, row, 34, row + 1), ansi));

            var inp = new TInputLine(new TRect(34, row, 38, row + 1), 3);
            inp.SetData($"{map[i]:X2}");
            dlg.Insert(inp);
            inputs[i] = inp;

            // Brief description of what the VGA nibble means.
            dlg.Insert(new TStaticText(new TRect(40, row, 70, row + 1),
                VgaDescription(map[i])));
        }

        dlg.Insert(new TButton(new TRect(10, 19, 22, 21), "O~K~",
            Views.cmOK, ButtonConstants.bfDefault));
        dlg.Insert(new TButton(new TRect(24, 19, 36, 21), "~D~efault",
            Views.cmYes, ButtonConstants.bfNormal));
        dlg.Insert(new TButton(new TRect(38, 19, 50, 21), "Cancel",
            Views.cmCancel, ButtonConstants.bfNormal));

        dlg.SelectNext(false);

        bool done = false;
        while (!done)
        {
            ushort res = parent.ExecView(dlg);
            switch (res)
            {
                case Views.cmOK:
                {
                    byte[] newMap = new byte[16];
                    bool valid = true;
                    for (int i = 0; i < 16; i++)
                    {
                        string txt = (inputs[i].Data ?? string.Empty).Trim();
                        if (byte.TryParse(txt, System.Globalization.NumberStyles.HexNumber,
                                          null, out byte v) && v <= 0x0F)
                        {
                            newMap[i] = v;
                        }
                        else
                        {
                            MsgBox.MessageBox(parent,
                                $"Invalid VGA nibble '{txt}' for {Entries[i].Name}.\n" +
                                "Enter a hex value 00–0F.",
                                MsgBox.mfError | MsgBox.mfOKButton);
                            valid = false;
                            break;
                        }
                    }
                    if (valid)
                    {
                        cfg.ColorMap = newMap;
                        cfg.Save();
                        MsgBox.MessageBox(parent,
                            "Terminal color mapping saved.\nChanges apply immediately to all open terminals.",
                            MsgBox.mfInformation | MsgBox.mfOKButton);
                        done = true;
                    }
                    break;
                }
                case Views.cmYes:
                {
                    // Reset all inputs to defaults.
                    for (int i = 0; i < 16; i++)
                        inputs[i].SetData($"{Entries[i].DefaultVga:X2}");
                    dlg.DrawView();
                    break;
                }
                default:
                    done = true;
                    break;
            }
        }
    }

    private static byte[] LoadOrDefault(TVTermConfig cfg)
    {
        byte[] map = cfg.ColorMap;
        if (map == null || map.Length < 16)
        {
            map = new byte[16];
            for (int i = 0; i < 16; i++)
                map[i] = Entries[i].DefaultVga;
        }
        return map;
    }

    private static string VgaDescription(byte nibble) => nibble switch
    {
        0x00 => "Black",
        0x01 => "Blue",
        0x02 => "Green",
        0x03 => "Cyan",
        0x04 => "Red",
        0x05 => "Magenta",
        0x06 => "Brown",
        0x07 => "Light Gray",
        0x08 => "Dark Gray",
        0x09 => "Light Blue",
        0x0A => "Light Green",
        0x0B => "Light Cyan",
        0x0C => "Light Red",
        0x0D => "Light Magenta",
        0x0E => "Yellow",
        0x0F => "White",
        _    => "?",
    };
}
