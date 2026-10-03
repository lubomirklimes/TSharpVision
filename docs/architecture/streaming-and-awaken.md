# Streaming and Awaken

Turbo Vision streaming reconstructs an object graph rather than invoking only ordinary public constructors. `Ipstream` reads registered streamable types and references; `Opstream` writes the corresponding managed graph through `System.IO.Stream`.

`TView.Awaken()` is the post-reconstruction hook. Ordinary construction does not call it. After a streamed graph is complete and its owner relationships are available, awakening gives views a chance to restore derived runtime relationships that cannot be finalized while individual objects are still being read.

`TGroup.Awaken()` propagates the hook through its children in group order. Nested groups therefore awaken their own descendants after ownership has been reconstructed. Overrides should preserve base behavior when they depend on group propagation.

Awaken is not object disposal, application startup, or aggregate control-data transfer. In particular, it is unrelated to `TDataRecord`: streaming reconstructs framework objects and references, while group `GetData`/`SetData` exchanges the current values of controls in an already-live hierarchy.

Managed streams and registrations are C# adaptations of the original streaming concept; automatic byte-for-byte compatibility with historical Turbo Vision resource or object streams is not promised.
