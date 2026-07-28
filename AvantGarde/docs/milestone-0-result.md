# Milestone 0 result — 12.x round-trip spike

Date: 2026-07-28. This is the written note [PLAN.md](PLAN.md) Milestone 0 asks for. Harvested from a
session scratchpad (`h1.log`, `h2.err`, `host-net8.err`) that is temp-cleaned; the raw logs are gone,
the findings are here.

The spike ran the designer host **by hand** against a hand-built Avalonia 12 fixture (`Fx12`, a
minimal `net8.0` app), bypassing AvantGarde entirely — the plan's verification step 9 (differential
probe), which isolates host + fixture + `dotnet exec` from AvantGarde's own code.

## Scope of what was actually run

This covers the **differential probe only** (plan verification step 9). Milestone 0 steps 2–4 —
running AvantGarde itself against `AvaloniaMvvm` and `MultiProjectSolution` — have **not** been done.
The classification below is therefore from the host's own behaviour, which is stronger evidence about
the host than an in-app run would be, but it does not exercise AvantGarde's discovery or UI path.

## Outcome: the failure is host-TFM selection

### 1. Wrong host TFM → hard startup failure (→ Milestone 1)

Running `tools/net10.0/designer/Avalonia.Designer.HostApp.dll` against the `net8.0` fixture:

```
Unhandled exception. System.IO.FileNotFoundException: Could not load file or assembly
'System.Runtime, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a'.
```

The host dies before opening the TCP connection, so from AvantGarde's side this would present as the
**10 s `SpinWait` timeout** with no diagnostic — the stderr carrying the cause was being discarded.

**Careful with the scope of this result.** It was a hand-launched probe, so it proves a wrong host TFM
is *fatal*; it does **not** show that AvantGarde selects one. Reading `NodeItem.FindInternal`, it
iterates `_contents` in reverse over an ascending `SortedList`, so `net8.0` is visited before
`net10.0` and `netstandard2.0` holds no `designer/` to match — meaning today's traversal appears to
land on the **correct** host by accident. Not measured; derived by reading.

So PLAN.md's "can pick `net10.0`" describes fragility rather than an observed failure. The
prescription is unaffected — resolve `net8.0` explicitly and stop depending on `FindFile` traversal
order — it just isn't the first thing a user hits.

**The first thing a user hits is version resolution.** `FindDesignerHost` does
`Path.Combine(nugetRoot, "avalonia", version)`, an exact directory-name match. The `AvaloniaMvvm`
fixture is Avalonia **12.0.5**, and `D:\NugetCache\avalonia\12.0.5` **does not exist** (cache has
12.0.0, 12.0.2, 12.1.0) because the fixture is unrestored. That is a `FileNotFoundException` before
the TFM question ever arises. Restoring the fixture should populate it — which is exactly why the
plan insists on restore-first. Both failure modes are Milestone 1.

One thing that does work: `DotnetProject.GetAvaloniaVersion` matches `Include="Avalonia"` exactly, so
it correctly reads `12.0.5` and ignores the fixture's mismatched `Avalonia.Diagnostics 11.3.18` /
`Avalonia.ReactiveUI 11.3.9` references. (Those two also hint that 12.x builds of both packages may
not exist — relevant to Milestone 3 item 3.)

### 2. `StartDesignerSessionMessage` sequencing confirmed; `--method` remains defensive

A second probe used `--transport file://…` (the pseudo-transport that watches a XAML file), which per
the host's own usage text **always uses the HTTP preview method** regardless of `--method`:

```
Initializing application in design mode
Obtaining AppBuilder instance from Fx12.Program
HtmlTransportStartedMessage
    Uri: http://127.0.0.1:34567/
Triggering XAML update
Sending StartDesignerSessionMessage
StartDesignerSessionMessage
    SessionId: 2d3120d3-da05-4eb4-883f-cd6c67e60e71
```

The valuable part is the ordering: the host sends `StartDesignerSessionMessage` **after** announcing
its transport and *before* it is ready for real XAML traffic — the sequencing premise behind
Milestone 2 item 1, now observed rather than assumed.

**What this probe does _not_ show:** that the host defaults to HTML when `--method` is omitted. The
`file://` transport forces HTML on its own, so `HtmlTransportStartedMessage` here proves nothing
about the default. That question is still open. The `--method avalonia-remote` change already applied
to `RemoteLoader.cs` therefore stands as **defensive**, justified by PLAN.md fact #26 (both reference
implementations pass it explicitly, as does the host's own usage example) — not by measurement. It
costs nothing and removes a whole misdiagnosis branch; leave it in. If the default ever is HTML,
`MessageHandler` drops `HtmlTransportStartedMessage` silently and the symptom is an indefinitely
blank preview, which is precisely the branch worth pre-empting.

### 3. Host CLI surface (unchanged from 11.x)

A malformed invocation dumped the usage text, which is the authoritative statement of the 12.1.0 CLI:

- `--transport`: `tcp-bson://…` or `file://…` (the `file` pseudo-transport **always uses HTTP preview**)
- `--session-id`: available, currently unused by AvantGarde (Milestone 2 item 4)
- `--method`: `avalonia-remote` | `win32` | `html`
- `--html-url`

## Corrections to PLAN.md's facts table

Verified against `D:\NugetCache` on 2026-07-28. Both are refinements, not reversals — every
consequence the plan draws still holds.

1. **`tools/netstandard2.0` was not dropped in 12.x.** It still exists in 12.1.0, but now contains
   only `Avalonia.Build.Tasks.dll` — no `designer/` subdirectory. The load-bearing claim (exactly two
   *designer host* candidates in 12.x, `net8.0` and `net10.0`) is correct.

   | | 11.3.2 | 12.1.0 |
   |---|---|---|
   | `tools/netstandard2.0/` | Build.Tasks **+ designer/** | Build.Tasks only |
   | `tools/net461/` | `designer/*.exe` | absent |
   | `tools/net8.0/`, `tools/net10.0/` | absent | `designer/*.dll` (both) |

2. **`AvaloniaPreviewerNetCoreToolPath` confirmed** — 12.1.0 `build/Avalonia.props` points at
   `tools\net8.0\designer\`, 11.3.2 at `tools\netstandard2.0\designer\`;
   `AvaloniaPreviewerNetFullToolPath` is present only in 11.3.2. Preferring `net8.0` mirrors
   Avalonia's own default, as the plan says.

3. Local Avalonia packages available for testing: `11.2.3`, `11.3.2`, `11.3.12`, `12.0.0`, `12.0.2`,
   `12.1.0`. SDKs installed: 8.0.129, 8.0.423, 9.0.316, 10.0.103, 10.0.302.

## Working-tree changes this produced

Uncommitted in `AvantGarde/Loading/RemoteLoader.cs`:

- `--method avalonia-remote` passed explicitly (finding 2).
- `ProcessOutputHandler` now buffers **all** host output, not just output arriving after `v_factory`
  is set — fatal startup errors like finding 1 appear before that and were being thrown away. Only
  the UI notification stays gated on `v_factory`.
- The `catch` in `UpdateThread` captures output **before** `StopNoSync()` clears the buffer, so the
  error payload carries the host's stderr instead of a bare "Timed out waiting for …".

Without these three, finding 1 is invisible from inside the app.
