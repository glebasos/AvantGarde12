# AvantGarde — Avalonia 12 previewer work

Fork of [kuiperzone/AvantGarde](https://github.com/kuiperzone/AvantGarde) v1.6.0: a standalone,
IDE-agnostic Avalonia XAML previewer. The app itself now runs on Avalonia **12.1.0 / net10.0**. The
remaining work is making it *preview* Avalonia **12.x user projects** correctly.

Those two are decoupled: the designer host is loaded from the **user's** Avalonia package, not this
app's, and 12.1.0's remote protocol is wire-identical to 11.3.2. Bumping this app bought nothing for
previewer compatibility — that is Milestones 1 and 2.

## Read these first

- **[AvantGarde/docs/PLAN.md](AvantGarde/docs/PLAN.md)** — the implementation plan (milestones 0–5,
  verified facts, deferrals). Authoritative for *what* to build and in what order.
- **[AvantGarde/docs/milestone-0-result.md](AvantGarde/docs/milestone-0-result.md)** — the spike
  outcome that orders milestones 1 and 2, plus corrections to the plan's facts table. Read before
  acting on PLAN.md's `tools/` layout claims.
- **[AvantGarde/docs/milestone-1-result.md](AvantGarde/docs/milestone-1-result.md)** — the MSBuild
  discovery spine as built, with three probe findings (notably: the MSBuild `AvaloniaVersion`
  property is a trap) and the list of known-and-deliberately-unfixed issues.
- **[AvantGarde/docs/milestone-3-result.md](AvantGarde/docs/milestone-3-result.md)** — what the
  Avalonia 12 migration actually changed, which predicted breakage never happened, and what is still
  unverified.

## Where things stand

**Milestone 3 is done** — app on Avalonia 12.1.0 / net10.0, 0 warnings (Debug *and* Release), 48/48
tests, launches and renders. Done ahead of Milestones 1–2 at the user's direction.

Dependency outcomes, which differ from what PLAN.md assumes:

- **ReactiveUI kept**, contrary to PLAN.md Milestone 3 step 1. The package was **renamed**
  `Avalonia.ReactiveUI` → **`ReactiveUI.Avalonia`** (old ID stops at 11.3.8), so searching the old
  name falsely suggests no 12.x. Brings ReactiveUI core 24.0.0. Two call-site changes: the `using`,
  and `UseReactiveUI()` now requires a builder action — see `Program.BuildAvaloniaApp`.
- **Clowd.Clipboard removed** — Avalonia 12's in-box `IClipboard.SetBitmapAsync` replaces it.
- **Avalonia.Diagnostics removed** — genuinely no 12.x and *not* a rename, so DevTools is gone from
  Debug builds.

Lesson from the first two: when an Avalonia-adjacent package looks absent on 12, check for a rename
before concluding it was discontinued.

**Milestones 0, 1 and 3 are done.** Both 12.0.5 fixtures preview correctly end to end, including the
two-assembly path, with no fixture modification. Every Milestone 0 failure was **discovery**, never
protocol, and Milestone 1 retired all three (version resolution, `TargetFramework` in
`Directory.Build.props`, host-TFM selection).

**`dotnet msbuild -getProperty:` is now the spine.** `MsBuildEvaluator` supplies `TargetFramework`,
`TargetPath`, `OutputType`, `ProjectAssetsFile` and — crucially —
`AvaloniaPreviewerNetCoreToolPath`, which locates the designer host directly. The project XML parse
and `RemoteLoader.FindDesignerHost` survive **only as fallbacks** for a project that cannot be
evaluated. Do not reintroduce XML parsing as a "fast path".

Evaluation costs ~0.6 s per project, so it is worker-thread only: `BeginEvaluation()` on the UI
thread marks projects and shows "Resolving project...", then `Evaluate()` runs on a worker, then
`Refresh()` applies. `UpdateLoader` defers previewing while it is in flight.

**Milestone 2 is next**, but note it is not what blocks users — nothing observed in Milestone 0
pointed at the protocol. Its strongest justification is real though: the host sends
`RequestViewportResizeMessage` three times per preview and `MessageHandler` drops it silently.

The three Milestone 0 diagnostics in `RemoteLoader.cs` (explicit `--method avalonia-remote`; buffer
all host output; capture output before `StopNoSync()`) are committed. Don't reimplement them — see
the milestone-0 note for why each exists.

Everything before `808f084 v1.6.0` is upstream; the `wip` commits on top are this work.

## Architecture worth knowing

- `AvantGarde/Loading/` + `AvantGarde/Projects/` are the previewer core — project discovery, designer
  host launch, the remote protocol client, XAML pre-processing. `Views/` and `ViewModels/` are the
  Avalonia UI.
- **Keep `Loading/` and `Projects/` free of `Views/`/`ViewModels/` references.** They should be
  splittable into their own assembly later (a VS extension is a plausible future milestone). This is
  architectural discipline, not a current build constraint — nothing enforces it.
- `RemoteLoader` owns the whole host lifecycle: `FindDesignerHost` → `dotnet exec` → `BsonTcpTransport`
  listener → `MessageHandler`. Most of the plan's milestones 1, 2 and 4 land in this one file.
- The two-assembly split (send `Load.ProjectAssembly` as `UpdateXamlMessage.AssemblyPath` while the
  CLI gets `Load.AppAssembly`) **already exists** in `RemoteLoader.SendXaml`. Don't rebuild it.

## Machine facts

- `NUGET_PACKAGES=D:\NugetCache`. Note this is env-set on *this machine only*; code must not assume
  it (see Milestone 1). Local Avalonia packages: 11.2.3, 11.3.2, 11.3.12, 12.0.0, 12.0.2, 12.1.0.
- Designer host layout — 11.x: `tools/netstandard2.0/designer/Avalonia.Designer.HostApp.dll`;
  12.x: **both** `tools/net8.0/designer/` and `tools/net10.0/designer/`. **Prefer `net8.0`** (net8 IL
  rolls forward onto a net10 runtime; the reverse throws `FileNotFoundException` on `System.Runtime`).
- SDKs: 8.0.129, 8.0.423, 9.0.316, 10.0.103, 10.0.302.
- Reference implementations sit alongside this checkout at `E:\Projects\dotnet\AvaloniaPreviewer\`:
  `AvaloniaRider\` and `AvaloniaVSCode\` (branch `ARCHIVE`). Both MIT — **read for ideas only, do not
  copy code.** This repo is GPL-3.0-or-later, single codebase.

## Build, test, verify

```
dotnet build AvantGarde.sln          # must stay clean, zero new warnings
dotnet test AvantGarde.Test          # 64 facts; still zero coverage of Loading/
```

Two Avalonia 12.0.5 fixtures, in **different** places — the second is not where PLAN.md says:

- `AvaloniaRider\testData\solutions\AvaloniaMvvm` — single project; `Views/MainWindow.axaml` is the
  smoke test. Used in place.
- **`E:\Projects\dotnet\AvaloniaPreviewer\fixtures\MultiProjectSolution`** — a working copy, because
  AvaloniaRider's original **does not compile** (missing `partial`, `LogToDebug()`,
  `Avalonia.Themes.Default`). `ClassLibrary1` + `AvaloniaApp1` exercise the two-assembly path.
  Its `TargetFramework` lives in `Directory.Build.props` **on purpose** — that is Milestone 1's
  acceptance test. Don't "fix" it by inlining the property.

**Restore and build a fixture before pointing AvantGarde at it.** `AvaloniaMvvm` ships without
`obj/`, so NuGet's `.g.props` is not imported and `AvaloniaPreviewerNetCoreToolPath` evaluates empty
— that is the exact unrestored-project case Milestone 1 must report actionably.

Driving the app without clicking (this is how Milestone 0 part 2 was done — see that note):

- Launch the **Debug** build with stdout redirected: `Debug.WriteLine` goes to stdout, giving the
  full internal trace (discovery steps, every protocol message type, frame sizes).
- `AvantGarde.exe <sln|csproj|file> -s=<leaf name>` opens and selects, no UI interaction.
- Screenshot with `PrintWindow` + `PW_RENDERFULLCONTENT` (flag `2`); `CopyFromScreen` returns black.
- `Get-CimInstance Win32_Process | ? CommandLine -like '*Designer.HostApp*'` shows which designer
  host TFM was actually launched.

To isolate a failure from AvantGarde entirely, launch the designer host by hand (the plan's
differential probe) — copy the `Exec` line from
`D:\NugetCache\avalonia\12.1.0\build\AvaloniaBuildTasks.targets:212`.

## Conventions

- `.editorconfig` contains **no formatting rules** — it exists solely to switch off IDE "modernize
  this" analyzers (IDE0017/0022/0028/0042/0057/0066/0301/0305), with an explicit rant explaining why.
  Read as a convention: write plain, explicit C#. No collection expressions, switch expressions,
  range operators, or expression-bodied members. Match the surrounding file.
- `Nullable` and `ImplicitUsings` are enabled; compiled bindings are on by default. Tabs in `.csproj`.
- `RemoteLoader` concurrency: `v_`-prefixed fields are `volatile`, **not** lock-guarded — they are
  touched from the host's process callbacks and the TCP thread. Two separate locks exist,
  `_startSync` (lifecycle, scale) and `_outputSync` (the output ring buffer, incl. `AppendOutput`).
  Know which one applies before adding state; don't assume `v_` means "protected."
- Upstream comment style is sparse; the migration comments explaining *why* a non-obvious workaround
  exists (as in `RemoteLoader.ProcessOutputHandler`) are deliberate. Match that: explain the
  non-obvious, not the obvious.
- The plan repeatedly flags **silent failure** as the enemy — empty `catch` blocks, dropped protocol
  messages, the one-shot verbatim XAML resend. Prefer surfacing to the OUTPUT pane over swallowing.
