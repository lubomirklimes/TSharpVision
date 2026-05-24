using TSharpVision.Constants;

namespace TSharpVision;

public class TGroup : TView
{
    public static readonly string Name = "TGroup";

    public TView current;
    public TView last;

    public ushort endState;

    public ScreenBuffer buffer;
    public byte lockFlag;
    private bool _bufferFreed;

    public TRect clip;
    public phaseType phase;

    public TGroup(TRect bounds) 
        : base(bounds) 
    {
        current = null;
        last = null;
        phase = phaseType.phFocused;
        buffer = null;
        lockFlag = 0;
        endState = 0;

        options |= Views.ofSelectable | Views.ofBuffered;
        clip = GetExtent();
        eventMask = 0xFFFF;
    }

    ~TGroup() { }

    public override void ShutDown()
    {
        // Avoid problems if a hidden or unselectable TView was forced to be
        // selected. Marek Bojarski <bojarski@if.uj.edu.pl>
        ResetCurrent();
        // Autation-safe traversal. The original p.Prev()-based loop
        // assumed each p.ShutDown() removed p from the list (mutating `last`).
        // TWindow.ShutDown() was previously a no-op and did not remove itself,
        // causing an infinite loop whenever the group contained open windows.
        // This pattern — always take `last`, shut it down, repeat — is safe
        // regardless of how many child views remain and does not depend on
        // p.Prev() returning a valid next pointer after list mutation.
        while (last != null)
        {
            TView p = last;
            p.ShutDown(); // expected to call owner.Remove(this) via base.ShutDown()
        }
        FreeBuffer();
        current = null;
        base.ShutDown();
    }

    public virtual ushort ExecView(TView p)
    {
        if (p == null) return Views.cmCancel;

        ushort saveOptions = p.options;
        TGroup saveOwner = p.owner;
        TView saveTopView = TheTopView;
        TView saveCurrent = current;
        TCommandSet saveCommands = new TCommandSet();
        GetCommands(saveCommands);

        TheTopView = p;
        p.options = (ushort)(p.options & ~Views.ofSelectable);
        p.SetState(Views.sfModal, true);
        SetCurrent(p, selectMode.enterSelect);
        if (saveOwner == null) Insert(p);

        int oldLock = lockFlag;
        if (lockFlag != 0) { lockFlag = 1; Unlock(); }

        ushort retval = p.Execute();
        p.SetState(Views.sfActive, false);

        lockFlag = (byte)oldLock;
        if (saveOwner == null) Remove(p);
        SetCurrent(saveCurrent, selectMode.leaveSelect);
        p.SetState(Views.sfModal, false);
        p.options = saveOptions;
        TheTopView = saveTopView;
        SetCommands(saveCommands);
        return retval;
    }
    public override void EndModal(ushort command)
    {
        if ((state & Views.sfModal) != 0)
            endState = command;
        else
            base.EndModal(command);
    }

    public virtual void EventError(TEvent ev)
    {
        owner?.EventError(ev);
    }

    /// <summary>
    /// Modal entry point reused by <see cref="TProgram"/>. Drains events
    /// through <see cref="HandleEvent"/> until <see cref="EndModal"/> sets
    /// <see cref="endState"/> and the command validates.
    /// </summary>
    public override ushort Execute()
    {
        do
        {
            endState = 0;
            do
            {
                TEvent e = default;
                GetEvent(ref e);
                HandleEvent(ref e);
                if (e.What != Events.evNothing)
                    EventError(e);
            } while (endState == 0);
        } while (!Valid(endState));
        return endState;
    }

    public void InsertView(TView p, TView target) 
    {
        p.owner = this;
        if (target != null)
        {
            target = target.Prev();
            p.Next = target.Next;
            target.Next = p;
        }
        else
        {
            if (last == null)
                p.Next = p;
            else
            {
                p.Next = last.Next;
                last.Next = p;
            }
            last = p;
        }
    }
    
    public void Remove(TView p)
    {
        ushort saveState = p.state;
        p.Hide();
        RemoveView(p);
        p.owner = null;
        p.Next = null;
        if ((saveState & Views.sfVisible) != 0)
            p.Show();
    }

    public void RemoveView(TView p)
    {
        if (last == null) return;
        TView view = last;
        TView akt = view.Next;
        while (akt != p && akt != last)
        {
            view = akt;
            akt = view.Next;
        }
        if (akt == p)
        {
            akt = p.Next;
            view.Next = akt;
            if (last != p) return;
            if (akt == p) view = null;
            last = view;
        }
    }
    public void ResetCurrent() 
    {
        SetCurrent(FirstMatch(Views.sfVisible, Views.ofSelectable), selectMode.normalSelect);
    }

    private void FocusView(TView p, bool enable)
    {
        if ((state & Views.sfFocused) != 0 && p != null)
            p.SetState(Views.sfFocused, enable);
    }

    public void SetCurrent(TView p, selectMode mode)
    {
        if (current != p)
        {
            Lock();
            FocusView(current, false);
            if (mode != selectMode.enterSelect)
                if (current != null)
                    current.SetState(Views.sfSelected, false);
            if (mode != selectMode.leaveSelect)
                if (p != null)
                    p.SetState(Views.sfSelected, true);
            if ((state & Views.sfFocused) != 0 && p != null)
                p.SetState(Views.sfFocused, true);
            current = p;
            Unlock();
        }
    }

    public void SelectNext(bool forwards)
    {
        if (current == null) return;
        TView p = current;
        do
        {
            p = forwards ? p.Next : p.Prev();
        } while (!(
            (((p.state & (Views.sfVisible | Views.sfDisabled)) == Views.sfVisible)
             && (p.options & Views.ofSelectable) != 0)
            || p == current));
        p.Select();
    }

    public TView FirstThat(Func<TView, object?, bool> func, object? args)
    {
        TView temp = last;
        if (temp == null) return null;
        do
        {
            temp = temp.Next;
            if (func(temp, args)) return temp;
        } while (temp != last);
        return null;
    }

    // Generic overload for strongly-typed args.
    public TView FirstThat<T>(Func<TView, T, bool> func, T args)
    {
        TView temp = last;
        if (temp == null) return null;
        do
        {
            temp = temp.Next;
            if (func(temp, args)) return temp;
        } while (temp != last);
        return null;
    }
    public void ForEach<T>(Action<TView, /*object*/ T> func, /*object */ T args) 
    {
        TView term = last;
        TView temp = last;

        if (temp == null)
            return;

        TView next = temp.Next;
        do
        {
            temp = next;
            next = temp.Next;
            func(temp, args);
        } while (temp != term);
    }

    public void Insert(TView p)
    {
        InsertBefore(p, First());
    }
    public void InsertBefore(TView p, TView target) 
    {
        if (p != null && p.owner == null && (target == null || target.owner == this))
        {
            if ((p.options & Views.ofCenterX) != 0)
                p.origin.x = (size.x - p.size.x) / 2;
            if ((p.options & Views.ofCenterY) != 0)
                p.origin.y = (size.y - p.size.y) / 2;
            ushort saveState = p.state;
            p.Hide();
            InsertView(p, target);
            if ((saveState & Views.sfVisible) != 0)
                p.Show();
        }
    }
    public TView At(short index)
    {
        TView temp = last;
        while (index-- > 0) temp = temp.Next;
        return temp;
    }
    public TView FirstMatch(ushort aState, ushort aOptions) 
    {
        if (last == null)
            return null;

        TView temp = last;
        while(true)
        {
            if ( ((temp.state & aState) == aState)
                && ((temp.options & aOptions) == aOptions))
                return temp;

            temp = temp.Next;
            if (temp == last)
                return null;
        }

        throw new NotImplementedException("TGroup.FirstMatch not implemented."); 
    }

    public short IndexOf(TView p)
    {
        if (last == null) return 0;
        short index = 0;
        TView temp = last;
        do
        {
            index++;
            temp = temp.Next;
        } while (temp != p && temp != last);
        return temp != p ? (short)0 : index;
    }

    public bool Matches(TView p) => p.owner == this;
    public TView First()
    {
        if (last == null)
            return null;
        else
            return last.Next;
    }

    private void DoExpose(TView p, bool o)
    {
        if ((p.state & Views.sfVisible) != 0)
            p.SetState(Views.sfExposed, o);
    }

    private void DoSetState(TView p, SetBlock b)
    {
        p.SetState(b.st, b.en);
    }

    public override void SetState(ushort aState, bool enable) 
    {
        SetBlock setBlock = new SetBlock(aState, enable);

        base.SetState(aState, enable);

        if ((aState & (Views.sfActive | Views.sfDragging)) != 0)
        {
            Lock();
            ForEach(DoSetState, setBlock);
            Unlock();
        }

        if ((aState & Views.sfFocused) != 0)
        {
            if (current != null)
                current.SetState(Views.sfFocused, enable);
        }

        if ((aState & Views.sfExposed) != 0)
        {
            ForEach(DoExpose, enable);
            if (enable == false)
                FreeBuffer();
        }
    }

    public override void HandleEvent(ref TEvent @event)
    {
        // for focused events; positional events fan out via firstThat-with-mouse;
        // everything else is broadcast to all subviews.
        base.HandleEvent(ref @event);
        if (@event.What == Events.evNothing) return;

        // Local event copy so subviews can clear it.
        TEvent localEv = @event;

        void Dispatch(TView p)
        {
            if (p == null) return;
            if ((p.state & Views.sfDisabled) != 0
                && (localEv.What & (Views.positionalEvents | Views.focusedEvents)) != 0)
                return;
            switch (phase)
            {
                case phaseType.phPreProcess:
                    if ((p.options & Views.ofPreProcess) == 0) return;
                    break;
                case phaseType.phPostProcess:
                    if ((p.options & Views.ofPostProcess) == 0) return;
                    break;
            }
            if ((localEv.What & p.eventMask) != 0)
                p.HandleEvent(ref localEv);
        }

        if ((@event.What & Views.focusedEvents) != 0)
        {
            phase = phaseType.phPreProcess;
            ForEachView(v => Dispatch(v));
            phase = phaseType.phFocused;
            Dispatch(current);
            phase = phaseType.phPostProcess;
            ForEachView(v => Dispatch(v));
        }
        else
        {
            phase = phaseType.phFocused;
            if ((@event.What & Views.positionalEvents) != 0)
            {
                TEvent positional = @event;
                Dispatch(FirstThat<TEvent>((v, pos) => v.ContainsMouse(pos), positional));
            }
            else
            {
                ForEachView(v => Dispatch(v));
            }
        }

        @event = localEv;
    }

    /// <summary>
    /// Iterates every subview in upstream traversal order (last.Next -> last).
    /// Mirrors <c>TGroup::forEach</c>; lambdas capture per-call state safely.
    /// </summary>
    public void ForEachView(Action<TView> action)
    {
        TView term = last;
        TView temp = last;
        if (temp == null) return;
        TView next = temp.Next;
        do
        {
            temp = next;
            next = temp.Next;
            action(temp);
        } while (temp != term);
    }
    
    public void DrawSubViews(TView p, TView bottom) 
    { 
        while (p != bottom)
        {
            p.DrawView();
            p = p.NextView();
        }
    }

    public override void ChangeBounds(TRect bounds)
    {
        TPoint d = new TPoint(
            (bounds.b.x - bounds.a.x) - size.x,
            (bounds.b.y - bounds.a.y) - size.y);
        if (d.x == 0 && d.y == 0)
        {
            SetBounds(bounds);
            DrawView();
        }
        else
        {
            FreeBuffer();
            SetBounds(bounds);
            clip = GetExtent();
            GetBuffer();
            Lock();
            ForEachView(p =>
            {
                TRect r = default;
                p.CalcBounds(ref r, d);
                p.ChangeBounds(r);
            });
            Unlock();
        }
    }

    public override ushort DataSize()
    {
        ushort total = 0;
        ForEachView(p => total += p.DataSize());
        return total;
    }

    // (setData walks last->prev chain calling each subview's setData with successive offsets).
    // For now the managed port keeps the reference-typed `object` payload as the upstream `void*` analogue;
    // callers stash a wrapper themselves.
    public override void GetData(ref object rec) { /* no-op default */ }
    public override void SetData(object rec) { /* no-op default */ }
    public override void Draw() 
    { 
        if (buffer == null)
        {
            GetBuffer();
            if (buffer != null)
            {
                lockFlag++;
                Redraw();
                lockFlag--;
            }
        }

        if (buffer != null)
        {
            WriteBuf(0, 0, size.x, size.y, buffer);
        }
        else
        {
            clip = GetClipRect();
            Redraw();
            clip = GetExtent();
        }
    }

    public void Redraw() 
    {
        DrawSubViews(First(), null);
    }
    public void Lock() 
    {
        if (buffer != null || lockFlag != 0)
            lockFlag++;
    }
    public void Unlock() 
    {
        if (lockFlag != 0 && --lockFlag == 0)
            DrawView();
    }
    public override void ResetCursor() 
    {
        if (current != null)
            current.ResetCursor();
    }

    public override ushort GetHelpCtx()
    {
        ushort h = Views.hcNoContext;
        if (current != null)
            h = current.GetHelpCtx();
        if (h == Views.hcNoContext)
            h = base.GetHelpCtx();
        return h;
    }

    public override bool Valid(ushort command)
    {
        return FirstThat((v, c) => !v.Valid((ushort)c!), command) == null;
    }

    public void FreeBuffer()
    {
        if ((options & Views.ofBuffered) != 0 && buffer != null)
        {
            _bufferFreed = true;
        }
    }
    public void GetBuffer()
    {
        if ((state & Views.sfExposed) != 0)
            if ((options & Views.ofBuffered) != 0)
            {
                int sz = Math.Max(size.x * size.y * ScreenBuffer.GetSize(), 0);
                if (_bufferFreed && buffer != null && buffer.Size == sz)
                {
                    buffer.Clear();
                    _bufferFreed = false;
                }
                else
                {
                    buffer = new ScreenBuffer(sz);
                    _bufferFreed = false;
                }
            }
    }

    protected TGroup(object streamableInit) : base(StreamableInit.streamableInit) { }
    public static readonly TStreamableClass StreamableClassTGroup =
        new TStreamableClass("TGroup", () => new TGroup(StreamableInit.streamableInit), 0);

    public override void Write(Opstream os)
    {
        // Write base TView fields first.
        base.Write(os);

        // Count children in the circular linked list.
        int count = 0;
        if (last != null)
        {
            TView p = last;
            do { count++; p = p.Next; } while (p != last);
        }

        // Save current index before writing (IndexOf returns 0 if current==null).
        short currentIndex = (last != null) ? IndexOf(current) : (short)0;

        os.WriteInt((uint)count);
        // Write each child pointer in circular-list order (first→last).
        if (last != null)
        {
            TView p = last;
            do
            {
                p = p.Next;
                os.WritePointer(p);
            } while (p != last);
        }
        os.WriteShort((ushort)currentIndex);
    }

    public override object Read(Ipstream isStream)
    {
        // Restore base TView fields.
        base.Read(isStream);
        clip  = GetExtent();
        phase = phaseType.phFocused;
        current = null;
        buffer  = null;
        lockFlag = 0;
        endState = 0;

        int count = (int)isStream.ReadInt();
        for (int i = 0; i < count; i++)
        {
            var tv = isStream.ReadPointer() as TView;
            if (tv != null) InsertView(tv, null);
        }

        short currentIndex = (short)isStream.ReadShort();
        if (last != null)
            SetCurrent(At(currentIndex), selectMode.normalSelect);
        return this;
    }

    public new static TStreamable Build() { return new TGroup(StreamableInit.streamableInit); }
    public override string StreamableName() { return Name; }
}
