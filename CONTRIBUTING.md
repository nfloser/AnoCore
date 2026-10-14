# Contributing to AnoCore

AnoCore uses an issue-driven workflow. Every independent feature, bug fix or significant refactor should have an issue and a dedicated branch.

## Local checks

Run the following from the repository root:

```bash
dotnet restore AnoCore.sln
dotnet build AnoCore.sln --configuration Release --no-restore
dotnet test AnoCore.sln --configuration Release --no-build
dotnet format AnoCore.sln --verify-no-changes --no-restore
```

## Pull requests

PRs should describe the problem, design/solution, tests, risks, compatibility impact and documentation changes. Link the owning issue and use `Closes #<issue>` when the PR fully resolves it.

Keep core contracts small and stable. Optional CS2 server behavior belongs in modules rather than `AnoCore.Runtime`.


## Native UI delivery

Panorama and Workshop-delivered UI changes are tested locally before publishing. Build and install the exact revision on the owner's local CS2 client first, complete the native single-client smoke test, and only then update the existing shared Workshop addon.

After publishing, remove or disable loose local overrides and verify the Workshop-delivered copy separately. Keep existing Workshop IDs and server configuration unless the owning issue explicitly changes them. Do not treat a successful local UI test as multi-client or Workshop acceptance, and do not mount overlapping copies of the same Panorama resources during normal testing.
