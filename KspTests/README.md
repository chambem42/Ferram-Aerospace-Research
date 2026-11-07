# FAR in-game tests

FAR's tests that run in KSP, on [KspTest](https://github.com/chambem42/KspTest): `dotnet test` starts your KSP, flies
or inspects crafts with FAR's own numbers, and passes or fails each test from what it measured. FAR's build doesn't
depend on any of this.

| Project | What |
|---|---|
| `FerramAerospaceResearch.KspTests` | the in-game tests on stock KSP and FAR, with the small mods FAR talks to (procedural parts and wings, AtmosphereAutopilot) |
| `FerramAerospaceResearch.KspTests.MechJeb` | in-game tests that fly on MechJeb; only deployed where MechJeb is installed |
| `FerramAerospaceResearch.KspTests.RP1` | heating in an RP-1 career; only deployed where RP-1 is installed |
| `FerramAerospaceResearch.KspTests.Run` | the `dotnet test` project: one test per in-game test |
| `Coverage/` | for coverage runs only: `FarCoverage.FastVisit`, which stands in for AltCover's recorder and passes on only a point's first visit, and `CovPatch`, which points the instrumented FAR's visit calls at it (the workflow's Windows in-game leg) |

## Running

You need the .NET 8 SDK and a KSP 1.12 install with FAR (the build you want to test) and Harmony. KspTest comes in
as a git submodule (`KspTests/KspTest`): clone with `--recursive`, or run `git submodule update --init` once.

```
cd KspTests
set KSPDIR=C:\path\to\Kerbal Space Program        (the build and the runs both use it)
dotnet test FarKspTests.slnx
dotnet test FarKspTests.slnx --filter Kind=Check
set KSPTEST_EXPLICIT=Far_ArCruise
dotnet test FarKspTests.slnx --filter Far_ArCruise
```

Each test deploys the harness and the test assemblies into `GameData/KspTest`, starts KSP on a new sandbox game in
`saves/ksptest` (emptied first; anything you saved there is backed up), runs the in-game test and quits. Only one KSP
runs at a time; a test waits for a KSP that is already running. The results (result.json, CSVs, screenshots, KSP.log)
land in `FerramAerospaceResearch.KspTests.Run/bin/<config>/net8.0/results/<time>-<test>/`; each test's output names
its folder.

Explicit tests (`[UITest(Explicit = true)]`) are skipped unless `KSPTEST_EXPLICIT` names them (`;`-separated method
names, or `1` for all): the long thermal flights, the tests flown on AtmosphereAutopilot or MechJeb, the RP-1 ones and
the older `Far_*` tests still to be ported. The examples here that name one set it too.

`Kind=Check` tests assert what they measure. `Kind=Measure` tests record numbers and pictures for comparing FAR builds
(run them on one build, then the other, and compare the results folders) and fail only when the flight itself does.

## On GitHub Actions

`.github/workflows/tests.yml` runs these and the unit tests on GitHub's runners: five Windows jobs, which also run the
unit tests and measure FAR's line coverage, and five Linux jobs, the in-game tests dealt between each leg's jobs by
their timings in earlier runs. A run's page has each leg's test summary and the coverage report; its Artifacts have a
page of screenshots per leg and every session's results.

The game comes from encrypted archives in a private repository's release, so the in-game jobs need its secrets:

- Pushes, manual runs and pull requests from branches of this repository run everything at once.
- A pull request from a fork runs the build at once; its tests wait for a maintainer. The workflow is always the
  default branch's (GitHub takes `pull_request_target`'s from there), so a pull request cannot change what runs with
  the secrets, but its code (FAR, the tests, their build) does run on runners with the game and its key. Read it for
  anything reaching beyond the tests, such as the network, files outside the game or the environment, then approve
  with **Review deployments** on the run. Every new push to the pull request is a new run, waiting again.
- Contributors can run the unit tests themselves: build FAR (Release), then
  `dotnet test KspTests/FerramAerospaceResearch.UnitTests -p:KspDir=<your KSP>`.

Setting it up, once, in the repository's settings:

- Environments: `ksp-game`, without protection rules, and `ksp-game-fork`, with **Required reviewers** (the
  maintainers) and administrators not allowed to bypass them.
- In both environments, the secrets `KSP_GAME_KEY` (the archives' key) and `KSP_GAME_TOKEN` (a fine-grained token with
  Contents: read-only on the game repository alone). Environment secrets rather than repository ones: a job sees
  them only once its environment lets it.
- The variable `KSP_GAME_REPO` (owner/name of the game repository), a repository variable.

## Your own crafts

Copy `FerramAerospaceResearch.KspTests.Run/ksptest.example.json` to `ksptest.json` beside it, or use the environment:

- `crafts` / `KSPTEST_CRAFTS`: craft files, or folders of them, put into the test save (SPH or VAB, from the craft).
- `params` / `KSPTEST_PARAMS`: parameters over the tests' own, `k=v;k=v`. Each in-game test lists its parameters in its
  doc comment; for example the aspect-ratio cruise on your planes:

```
set KSPTEST_CRAFTS=C:\my crafts
set KSPTEST_PARAMS=crafts=My Plane+My Other Plane
set KSPTEST_EXPLICIT=Far_ArCruise
dotnet test FarKspTests.slnx --filter Far_ArCruise
```

The crafts the tests fly by default are KSP's and FAR's stock planes, and the ones in
`FerramAerospaceResearch.KspTests.Run/crafts/`.

## Writing a test

An in-game test is a static `IEnumerator` method with `[UITest("Name", StartsAt = ...)]` in one of the in-game
projects; the harness gets the game to its start state (the space center, an editor with a craft, a craft on the
runway), then runs it as a coroutine. It is a `dotnet test` test too, of the same full name: the Run project lists every
`[UITest]` in the in-game projects. Its `Kind` (`Check` or `Measure`, for `--filter Kind=Check`), the mods it needs
besides the project's (`Mods`, CKAN identifiers or a local mod's GameData folder) and its new game (`NewGame = "career"`)
go on the `[UITest]`. See KspTest's README for the harness's API (scenes, flight, FAR's numbers
through `FarApi`/`FarSim`, screenshots, recording values).
