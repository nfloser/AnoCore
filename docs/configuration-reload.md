# Module configuration reloads

`IConfigReloadRegistry` exposes bounded, module-owned reload registrations. A registration has a stable descriptor and a typed `Current` value. Reloads are serialized per registration. A loaded candidate replaces `Current` only after validation succeeds and the registration is still alive.

Failed, cancelled or invalid loads retain the last accepted value. Disposing the registration removes it and prevents delayed work from publishing. Modules should give the handle to `IAnoModuleContext.Own(...)`.

This registry supplies atomic in-process publication. Concrete modules still decide how files are loaded and must adopt the registry before their configuration is reloadable.
