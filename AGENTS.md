# AnoCore contributor instructions

AnoCore is maintained as a long-lived software project.

## Development workflow

Use: Issue → Branch → Tests/TDD → Implementation → Documentation → Pull Request → CI → Review → Corrections → Merge → Release.

Do not develop directly on `main`. Keep commits small and logically scoped. Prefer a failing regression/contract test before implementation when behavior can be tested independently of a running CS2 server.

## Architecture rules

- `AnoCore.Abstractions` must remain independent of CounterStrikeSharp and infrastructure packages.
- `AnoCore.Runtime` may depend on `AnoCore.Abstractions`, but not on optional server feature modules.
- `AnoCore.Plugin` is the CounterStrikeSharp adapter/composition root.
- Features such as Admin, Stats, Ranks, Maps, Vote, Veto and Tournament are modules, not core responsibilities.
- Do not introduce absolute local file paths or committed third-party DLLs when a package/source dependency exists.
- Public contracts require tests and compatibility consideration.

## Licensing

Preserve GPL-3.0 compatibility and upstream attribution for K4-Zenith-derived code. Do not remove third-party copyright or license notices.

## Quality gates

A change is not complete until build, tests, formatting, documentation and applicable CI checks pass. Passing tests alone do not prove server behavior; server-facing changes also require explicit acceptance criteria and, where practical, integration/test-server verification.

### Native Panorama / Workshop acceptance

For changes to Panorama layouts, styles, textures or other Workshop-delivered UI, use a local-client-first workflow:

1. Build the matching plugin/UI revision and install the compiled resources only on the owner's local CS2 client.
2. Perform the native single-client acceptance there first. Do not update the shared Workshop item merely to test an unaccepted UI revision.
3. After local acceptance, rebuild the same revision for the existing shared addon and update its existing Workshop entry. Preserve Workshop IDs and server configuration.
4. Remove or disable loose local overrides before Workshop verification so they cannot mask the published addon.
5. Verify Workshop delivery separately, including a second client when the feature requires multi-client behavior.

Local-client acceptance proves only the local compiled-resource path. It does not count as Workshop, multi-client or production acceptance. Avoid mounting two addons that provide the same Panorama resource paths unless an issue explicitly requires and documents that arrangement.
