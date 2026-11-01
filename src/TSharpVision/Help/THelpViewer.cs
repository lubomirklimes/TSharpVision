using TSharpVision.Constants;

namespace TSharpVision;

/// <summary>
/// Scrollable topic viewer; Tab cycles links, Enter follows the selected link, Backspace goes back, Alt+I opens the
/// index, Alt+S searches the help text, and Escape closes modal help.
/// </summary>
public class THelpViewer : TScroller
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "THelpViewer";

    private static readonly TPalette _palette =
        new TPalette("\x06\x07\x08", 3);

    /// <summary>Help context identifier reserved for the compiled help index topic.</summary>
    public const int IndexContext = 1;

    /// <summary>Referenced help store used to load topics; the viewer does not close its stream.</summary>
    public THelpFile hFile;
    /// <summary>Currently displayed topic, including a fallback topic when the requested context is unavailable.</summary>
    public THelpTopic topic;
    /// <summary>One-based selected link number; initialized to one even when the topic has no links.</summary>
    public int selected;

    // Back-navigation stack: each entry is the file and context that was shown.
    private int _currentRef;
    private readonly System.Collections.Generic.Stack<(THelpFile File, int Context)> _backStack = new();

    // Set when the viewer shows a help collection: topics may come from any of its files, and files removed from it
    // are no longer read. Without it the viewer searches hFile alone through a private collection.
    private readonly THelpCollection? _collection;
    private THelpCollection? _ownSearchCollection;
    private string _lastQuery = string.Empty;

    /// <summary>Creates a viewer in owner-relative character-cell bounds, references the scrollbars and help store, and loads the initial context.</summary>
    public THelpViewer(TRect bounds, TScrollBar? aHScrollBar,
        TScrollBar? aVScrollBar, THelpFile aHelpFile, ushort context)
        : base(bounds, aHScrollBar, aVScrollBar)
    {
        options |= Views.ofSelectable;
        growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);
        hFile = aHelpFile;
        _currentRef = context;
        topic = aHelpFile.GetTopic(context);
        topic.SetWidth(size.x);
        SetLimit(78, topic.NumLines());
        selected = 1;
    }

    /// <summary>
    /// Creates a viewer over a help collection and loads the context from the first registered file that defines it
    /// (the first file when none does). Links in a topic lead to topics of the same file.
    /// </summary>
    /// <exception cref="ArgumentException">The collection has no registered files.</exception>
    public THelpViewer(TRect bounds, TScrollBar? aHScrollBar,
        TScrollBar? aVScrollBar, THelpCollection help, ushort context)
        : base(bounds, aHScrollBar, aVScrollBar)
    {
        ArgumentNullException.ThrowIfNull(help);
        options |= Views.ofSelectable;
        growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);
        _collection = help;
        hFile = help.FindContext(context)
            ?? (help.Files.Count > 0 ? help.Files[0] : throw new ArgumentException("The help collection has no registered files.", nameof(help)));
        _currentRef = context;
        topic = hFile.GetTopic(context);
        topic.SetWidth(size.x);
        SetLimit(78, topic.NumLines());
        selected = 1;
    }

    /// <inheritdoc />
    public override void ChangeBounds(TRect bounds)
    {
        base.ChangeBounds(bounds);
        topic.SetWidth(size.x);
        SetLimit(limit.x, topic.NumLines());
    }

    /// <inheritdoc />
    public override TPalette GetPalette() => _palette;

    /// <inheritdoc />
    public override void Draw()
    {
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);
        var lineBuf = new char[256];

        ushort normal = GetColor(1);
        ushort keyword = GetColor(2);
        ushort selKeyword = GetColor(3);
        int keyCount = 0;
        TPoint keyPoint = new TPoint(0, 0);
        byte keyLength = 0;
        int keyRef = 0;

        topic.SetWidth(size.x);
        if (topic.GetNumCrossRefs() > 0)
        {
            do
            {
                topic.GetCrossRef(keyCount, ref keyPoint, out keyLength, out keyRef);
                keyCount++;
            } while (keyCount < topic.GetNumCrossRefs() && keyPoint.y <= delta.y);
        }

        for (int i = 1; i <= size.y; i++)
        {
            b.moveChar(0, ' ', normal, size.x);
            topic.GetLine(i + delta.y, lineBuf);
            int len = CharLen(lineBuf);
            if (len > delta.x)
            {
                int copy = System.Math.Min(size.x, len - delta.x);
                var slice = new char[copy];
                for (int k = 0; k < copy; k++)
                {
                    char raw = lineBuf[delta.x + k];
                    slice[k] = raw == '\xFF' || raw == '\u00A0' ? ' ' : raw;
                }
                b.moveBuf(0, slice, normal, copy);
            }

            while (i + delta.y == keyPoint.y)
            {
                int l = keyLength;
                int x = keyPoint.x;
                if (x < delta.x)
                {
                    l -= (delta.x - x);
                    x = delta.x;
                }
                ushort c = (keyCount == selected) ? selKeyword : keyword;
                for (int j = 0; j < l; j++)
                    b.putAttribute(x - delta.x + j, c);
                if (keyCount < topic.GetNumCrossRefs())
                {
                    topic.GetCrossRef(keyCount, ref keyPoint, out keyLength, out keyRef);
                    keyCount++;
                }
                else
                {
                    keyPoint.y = 0;
                }
            }
            WriteLine(0, i - 1, size.x, 1, b);
        }
    }

    /// <summary>Scrolls a zero-based topic reference into view and returns its text position, length, and destination context.</summary>
    public void MakeSelectVisible(int sel, ref TPoint keyPoint,
        out byte keyLength, out int keyRef)
    {
        topic.GetCrossRef(sel, ref keyPoint, out keyLength, out keyRef);
        TPoint d = delta;
        if (keyPoint.x < d.x) d.x = keyPoint.x;
        if (keyPoint.x > d.x + size.x) d.x = keyPoint.x - size.x;
        if (keyPoint.y <= d.y) d.y = keyPoint.y - 1;
        if (keyPoint.y > d.y + size.y) d.y = keyPoint.y - size.y;
        if (d.x != delta.x || d.y != delta.y) ScrollTo(d.x, d.y);
    }

    /// <summary>Loads a context of the current file after saving the current context on the back-navigation stack.</summary>
    public void NavigateTo(int keyRef)
    {
        _backStack.Push((hFile, _currentRef));
        _currentRef = keyRef;
        SwitchToTopic(keyRef);
    }

    /// <summary>
    /// Shows a context of the given help file, saving the current topic on the back-navigation stack. A viewer over a
    /// help collection only opens files still registered in it.
    /// </summary>
    /// <returns>Whether the topic was opened.</returns>
    public bool NavigateTo(THelpFile file, int context)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!CanRead(file)) return false;
        _backStack.Push((hFile, _currentRef));
        hFile = file;
        _currentRef = context;
        SwitchToTopic(context);
        return true;
    }

    /// <summary>
    /// Returns to the most recent saved topic, skipping topics of files removed from the help collection; does nothing
    /// when navigation history is empty.
    /// </summary>
    public void GoBack()
    {
        while (_backStack.Count > 0)
        {
            var (file, prev) = _backStack.Pop();
            if (!CanRead(file)) continue;
            hFile = file;
            _currentRef = prev;
            SwitchToTopic(prev);
            return;
        }
    }

    private bool CanRead(THelpFile file)
        => _collection == null || _collection.Contains(file);

    /// <summary>Navigates to context 1, preserving the current context for back navigation.</summary>
    public void GoToIndex() => NavigateTo(IndexContext);

    /// <summary>Loads and redraws a topic, resets scrolling and link selection, and leaves navigation history unchanged.</summary>
    public void SwitchToTopic(int keyRef)
    {
        // A file removed from the collection has had its stream closed; show the "no help" topic instead.
        topic = _collection == null || _collection.Contains(hFile) ? hFile.GetTopic(keyRef) : hFile.InvalidTopic();
        topic.SetWidth(size.x);
        ScrollTo(0, 0);
        SetLimit(limit.x, topic.NumLines());
        selected = 1;
        DrawView();
    }

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent ev)
    {
        TPoint keyPoint = default;
        byte keyLength;
        int keyRef;
        int keyCount;

        base.HandleEvent(ref ev);
        switch (ev.What)
        {
            case Events.evKeyDown:
                switch (ev.keyDown.keyCode)
                {
                    case Keys.kbTab:
                        selected++;
                        if (selected > topic.GetNumCrossRefs()) selected = 1;
                        if (topic.GetNumCrossRefs() != 0)
                            MakeSelectVisible(selected - 1, ref keyPoint,
                                out keyLength, out keyRef);
                        break;
                    case Keys.kbShiftTab:
                        selected--;
                        if (selected == 0) selected = topic.GetNumCrossRefs();
                        if (topic.GetNumCrossRefs() != 0)
                            MakeSelectVisible(selected - 1, ref keyPoint,
                                out keyLength, out keyRef);
                        break;
                    case Keys.kbEnter:
                        if (selected <= topic.GetNumCrossRefs())
                        {
                            topic.GetCrossRef(selected - 1, ref keyPoint,
                                out keyLength, out keyRef);
                            NavigateTo(keyRef);
                        }
                        break;
                    case Keys.kbBack:
                        GoBack();
                        break;
                    case Keys.kbAltI:
                        GoToIndex();
                        break;
                    case Keys.kbAltS:
                        ClearEvent(ref ev);
                        Search();
                        return;
                    case Keys.kbEsc:
                        ev.What = Events.evCommand;
                        ev.message.command = Views.cmClose;
                        PutEvent(ref ev);
                        break;
                    default:
                        return;
                }
                DrawView();
                ClearEvent(ref ev);
                break;

            case Events.evMouseDown:
                {
                    TPoint mouse = MakeLocal(ev.mouse.where);
                    mouse.x += delta.x;
                    mouse.y += delta.y;
                    keyCount = 0;
                    do
                    {
                        keyCount++;
                        if (keyCount > topic.GetNumCrossRefs()) return;
                        topic.GetCrossRef(keyCount - 1, ref keyPoint,
                            out keyLength, out keyRef);
                    } while (!(keyPoint.y == mouse.y + 1
                            && mouse.x >= keyPoint.x
                            && mouse.x < keyPoint.x + keyLength));
                    selected = keyCount;
                    NavigateTo(keyRef);
                    ClearEvent(ref ev);
                }
                break;

            case Events.evCommand:
                switch (ev.message.command)
                {
                    case Views.cmHelpBack:
                        GoBack();
                        DrawView();
                        ClearEvent(ref ev);
                        break;
                    case Views.cmHelpIndex:
                        GoToIndex();
                        DrawView();
                        ClearEvent(ref ev);
                        break;
                    case Views.cmHelpSearch:
                        ClearEvent(ref ev);
                        Search();
                        break;
                    case Views.cmClose:
                        if (owner != null && (owner.state & Views.sfModal) != 0)
                        {
                            EndModal(Views.cmClose);
                            ClearEvent(ref ev);
                        }
                        break;
                }
                break;
        }
    }

    /// <summary>
    /// Searches the text of the help this viewer shows: every file of its collection, or its own file. Asks for the
    /// words (offering the previous query), lists the matching topics with the file each comes from, and opens the
    /// chosen one; Backspace then returns to the topic shown before.
    /// </summary>
    /// <remarks>Runs modal dialogs in the group that owns the help window. See <see cref="THelpCollection.Search"/>.</remarks>
    public void Search()
    {
        TGroup? host = SearchHost();
        if (host == null) return;
        var (code, value) = MsgBox.InputBox(host,
            TSharpVisionIntl.Get("Help_Search_Title", "Search Help"),
            TSharpVisionIntl.Get("Help_Search_Words", "~W~ords"), _lastQuery, 60);
        if (code == Views.cmCancel || code == 0) return;
        string query = value.Trim();
        if (query.Length == 0) return;
        _lastQuery = query;

        var results = SearchCollection().Search(query);
        if (results.Count == 0)
        {
            MsgBox.MessageBox(host,
                string.Format(TSharpVisionIntl.Get("Help_Search_NoResults", "No help topics contain '{0}'."), query),
                MsgBox.mfInformation | MsgBox.mfOKButton);
            return;
        }

        var dialog = new THelpSearchResultsDialog(host, results);
        try
        {
            if (host.ExecView(dialog) != Views.cmOK || dialog.Selected is not { } chosen) return;
            if (!NavigateTo(chosen.File, chosen.Context))
                MsgBox.MessageBox(host,
                    TSharpVisionIntl.Get("Help_Search_Unavailable", "That help file is no longer available."),
                    MsgBox.mfError | MsgBox.mfOKButton);
            owner?.Select();
            DrawView();
        }
        finally { dialog.ShutDown(); }
    }

    // The group the help window lives in (normally the desktop), so search dialogs open beside it.
    private TGroup? SearchHost()
    {
        TGroup? window = owner;
        return window?.owner ?? window;
    }

    private THelpCollection SearchCollection()
    {
        if (_collection != null) return _collection;
        if (_ownSearchCollection == null || !_ownSearchCollection.Contains(hFile))
        {
            _ownSearchCollection = new THelpCollection();
            _ownSearchCollection.Add(hFile, TSharpVisionIntl.Get("Help_WindowTitle", "Help"));
        }
        return _ownSearchCollection;
    }

    private static int CharLen(char[] b)
    {
        for (int i = 0; i < b.Length; i++)
            if (b[i] == '\0') return i;
        return b.Length;
    }
}
