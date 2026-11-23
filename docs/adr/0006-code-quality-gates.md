# 0006. Code quality gates and dependency policy

- Status: Accepted
- Date: 2026-11-07

## Decision
- Compiler and analyzer warnings are errors in every project, the Godot client included (amended 2026-11-23).
  Godot's generated code builds clean today. If a Godot SDK update breaks that, narrow the client to
  `WarningsAsErrors` `Nullable;NU1901;NU1902;NU1903;NU1904` and record why here.
- The .NET analyzers run at `AnalysisLevel` `10.0-recommended`. It is pinned rather than `latest`, so an SDK
  roll-forward cannot add new errors. `.editorconfig` raises CA1305 (culture-sensitive formatting), IDE0005 (unused
  usings) and IDE0072 (a switch expression on an enum must name every member) to warnings. Switch statements fall
  under IDE0010, which stays off. CA1707 is off for tests, whose method names are sentences with underscores. A
  suppression needs a `SuppressMessage` with a justification.
- XML documentation files are generated for every project, because IDE0005 reports on build only when they are.
  Missing doc comments (CS1591) are errors only in `Twin.Sim`. In any project, a broken `<see cref>` or `<param>`
  fails the build.
- Code style is defined in `.editorconfig` and applied by `dotnet format`; CI verifies it. Style is never a review topic.
- Every NuGet version is pinned exactly in `Directory.Packages.props` (central package management).
- A package version must have been public for at least 14 days before we adopt it (supply-chain safety),
  and each upgrade is its own pull request.
- Prefer few dependencies. The scenario kit has its own assertions instead of an assertion library; this also
  avoids licence surprises (FluentAssertions 8, for example, moved to a commercial licence).
- NuGet vulnerability auditing is on; a vulnerable package fails the build.

## Consequences
Builds are reproducible and stricter. Occasionally a warning must be fixed before a change can merge.
