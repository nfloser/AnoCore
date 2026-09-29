# Module configuration reloads

`IConfigReloadRegistry` exposes bounded, module-owned reload registrations. A registration has a stable descriptor and a typed `Current` value. Reloads are serialized per registration. A loaded candidate replaces `Current` only after validation succeeds and the registration is still alive.

Failed, cancelled or invalid loads retain the last accepted value. Disposing the registration removes it and prevents delayed work from publishing. Modules should give the handle to `IAnoModuleContext.Own(...)`.

Operators with `ano.core.reload` can use `anoconfigs` to inspect the stable registered name/owner list and `anoreloadconfig <name>` to reload exactly one entry. The server console may use both commands. Unknown names and loader or validation failures return the normal bounded command failure and do not replace the last accepted value.

This registry supplies atomic in-process publication for each registration. Concrete modules still decide how files are loaded and must adopt the registry before their configuration appears in `anoconfigs`. Cross-registration reloads are intentionally not implied: an operator names one independently validated configuration, avoiding partially applied bulk reloads.
