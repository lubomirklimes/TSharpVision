# Group data transfer

A group exchanges control values as a `TDataRecord`. Its `Size` equals the group's total `DataSize()`, and `Segments` contains flattened leaf values with logical offsets and sizes. These units describe control-record geometry, not memory addresses, serialized bytes or C++ structure layout. Each value retains the managed type expected by its control.

Data traversal starts at `last` and follows `Prev()` back around the child circle. Nested groups repeat that order using the same cursor, so their leaves flatten into the parent record. Zero-size leaves consume no span.

`SetValue(index, value)` replaces a value without changing geometry. Passing the record to the group's `SetData` validates total size, segment offsets and sizes, and complete consumption. Checked arithmetic detects overflow. These checks do not identify controls or every hierarchy change: reordering equal-sized compatible controls can leave identical geometry and send values to different controls. Keep the target hierarchy compatible with the record's traversal order.

Direct leaf `GetData`/`SetData` calls still exchange scalar values. Group aggregation is separate from object streaming.
