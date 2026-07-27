using TSharpVision.Constants;
namespace TSharpVision.Samples.TVDemo;

// Only plain demonstration windows have a defined persistence contract.
// Games, accessories and file viewers remain open when a snapshot is restored.
public static class DesktopState
{
    public static bool Supports(TWindow window)
    {
        bool supported = window.GetType() == typeof(TWindow);
        window.ForEachView(view => supported &= view.GetType() == typeof(TFrame)
            || view.GetType() == typeof(TStaticText));
        return supported;
    }

    public static void Save(string path, IReadOnlyList<TWindow> windows, byte[] palette)
    {
        if (windows.Any(w => !Supports(w)))
            throw new ArgumentException("Only plain demonstration windows can be saved.");
        StreamableRegistration.RegisterAll();
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.ReadWrite))
            {
                var stream = new Fpstream(file);
                var resources = new TResourceFile(stream);
                var metadata = new TStringCollection();
                metadata.Insert("TVDemo desktop 1");
                metadata.Insert(Convert.ToHexString(palette));
                metadata.Insert(windows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                resources.Put(metadata, "metadata");
                for (int i = 0; i < windows.Count; i++)
                    resources.Put(windows[i], $"window.{i:D5}");
                resources.Flush();
                if (stream.Out.Fail() != 0) throw new IOException("Desktop serialization failed.");
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static (List<TWindow> Windows, byte[] Palette) Load(string path, int paletteLength)
    {
        StreamableRegistration.RegisterAll();
        var windows = new List<TWindow>();
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
            // Fpstream requires a writable stream for its output half.
            using var copy = new FileStream(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tvr"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                4096, FileOptions.DeleteOnClose);
            file.CopyTo(copy);
            copy.Position = 0;
            var stream = new Fpstream(copy);
            var resources = new TResourceFile(stream);
            if (resources.Get("metadata") is not TStringCollection meta || meta.Count != 3
                || meta[0] != "TVDemo desktop 1") throw new InvalidDataException("Not a TVDemo desktop snapshot.");
            byte[] palette = Convert.FromHexString(meta[1]);
            if (palette.Length != paletteLength) throw new InvalidDataException("Palette size differs from this application.");
            if (!int.TryParse(meta[2], out int count) || count < 0 || count > 1000)
                throw new InvalidDataException("Invalid window count.");
            for (int i = 0; i < count; i++)
            {
                if (resources.Get($"window.{i:D5}") is not TWindow window)
                    throw new InvalidDataException("Missing saved window.");
                windows.Add(window);
                if (!Supports(window)) throw new InvalidDataException("Unsupported saved window type.");
            }
            if (stream.In.Fail() != 0) throw new InvalidDataException("Incomplete desktop snapshot.");
            return (windows, palette);
        }
        catch
        {
            foreach (var window in windows) window.ShutDown();
            throw;
        }
    }
}


