# Outline views

`TOutline` displays a linked `TNode` hierarchy in visible preorder. Each node links to its first child through `childList` and to its next sibling through `next`; `expanded` controls whether children contribute visible rows. A null root is an empty outline. The root's own `next` link is retained and streamed but is not treated as a second root, matching the Borland traversal model.

Create child sibling chains first, attach them to a root, and pass the root to `TOutline`. Call `Update()` after changing links, text, or expansion outside the view's built-in input handling so its width, row count, focus clamp, and scrollbar ranges are recalculated.

`TOutlineViewer` is the historical extension point for alternative tree storage. Derived viewers supply root, child, text, and expansion operations while inheriting traversal, graph construction, focus, input, scrolling, palette, and drawing behavior. `FirstThat` stops at the first true predicate; `ForEach` visits every currently visible node.

Arrow, Home/End and page keys move through visible rows; the historical WordStar movement mappings are also supported. `+` and `-` expand or collapse, and `*` expands descendants. Enter/Ctrl+Enter activates the focused node. Clicking a row focuses it, clicking its graph prefix toggles expansion, and double-clicking calls the virtual `Selected` hook. The base hook is empty: `cmOutlineItemSelected` is available to application overrides, not an automatic broadcast.

Drawing uses the historical four-entry palette (normal, focus, selection, collapsed text). CP437 branch characters are represented by their Unicode box-drawing equivalents and rendered through the ordinary character-cell drawing pipeline.

Only `TOutline` is registered as a concrete streamable type. Its node graph is stored recursively in child-then-sibling order. `TNode` is not independently streamable, and the outline family has no `DataSize`, `GetData`, or `SetData` role.
