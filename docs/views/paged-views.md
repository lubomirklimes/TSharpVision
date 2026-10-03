# Paged byte and table views

`TSharpVision.HexView` supplies `THexView` for read-only bytes. Attach an `IHexDataSource` with `SetDataSource`; the source provides an optional length and positioned asynchronous reads. The view uses 64-bit offsets and reads bounded pages around the visible rows. For an unknown length, it reads until a page is full or the source returns no more bytes. The caller owns and disposes the source after detaching it or shutting down the view.

`TSharpVision.TableView` supplies `TTableView` for read-only rows and columns. `ITableDataSource` provides column descriptions, an optional row count, and asynchronous row blocks. A block's `ReachedEnd` flag distinguishes the end of an unknown-length table from rows that were temporarily unavailable. Cells are display text with a role; sorting, filtering, typed values, and source lifetime remain with the application.

Both views keep bounded caches, cancel reads made irrelevant by navigation or source replacement, and ignore late results. Drawing uses cached data and does not wait for the source. The source's metadata accessors run on the event-loop thread and should be quick; asynchronous reads may overlap on worker threads, so sources that cannot read concurrently must serialize internally. Read results return to the view through `TView.Post` on the event-loop thread.
