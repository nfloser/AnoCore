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
