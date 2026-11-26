# Dystopographica

A Unity Editor package that turns a greyscale heightmap into a landscape mod for the
game Dystopika. Design and reference notes: `docs/pipeline.md`.

## Development rules

1. All nontrivial logic must be tested (unit, integration, or whatever fits).
   Style-only changes, and components that are very difficult to test such as
   top-level UI, may be excluded. Every exclusion must be called out clearly in the
   PR description.
2. To the greatest reasonable extent, use test-driven development: write the test
   first and watch it fail before making it pass.
3. Keep it simple.

## Layout

- `modkit/` is the Unity project: an unzipped copy of the Dystopika ModKit template.
  Git ignores all of it except our package. Never commit ModKit files; the Voids
  Within EULA forbids redistributing them.
- `modkit/Assets/Dystopographica/` is the package and the only tracked code.
  `Editor/` is the tool, `Tests/Editor/` holds its edit-mode tests.
- Unity writes a `.meta` file next to every file and folder. Commit each one with
  the file it belongs to.
- `tools/dev.ps1` runs headless Unity. `docs/pipeline.md` is the design.

## Commands

```
pwsh tools/dev.ps1 test                           # compile, then run the edit-mode tests
pwsh tools/dev.ps1 run Dystopographica.Cli.Info   # run one static method headlessly
pwsh tools/dev.ps1 run Dystopographica.Cli.RenderLandscape "mod=<mod name>" out=Logs/shot.png
```

`RenderLandscape` photographs a made landscape in 3D. Use it to look at a result
before sending anyone into the game.

Unity allows one instance per project, so both fail while the Unity editor has
`modkit/` open. Logs and test results land in `modkit/Logs/`.

There is no hosted CI, because the tests need the ModKit and it cannot be
redistributed. Instead `.githooks/pre-push` runs the tests before every push and
stops the push if they fail or cannot run (no Unity project in this checkout, or the
editor has it open). Never push with `--no-verify`.

Arguments to `run` are `name=value` pairs; `Cli.cs` lists the entry points. The tests
build and compile real mods named `Dystopographica Test ...` under `modkit/Assets/`
and delete them afterwards. Installing is only ever tested against a temporary
folder: never point a test at the game's Mods folder.

## First-time setup

1. Install Unity at the version named in `modkit/ProjectSettings/ProjectVersion.txt`
   (2022.3.62f2 for ModKit 1.2.2).
2. Unzip the ModKit template from the Dystopika modding wiki into `modkit/`, then
   delete `modkit/.gitignore`. The template ships its own, which overrides ours.
3. Run `pwsh tools/dev.ps1 test`. The first run imports the project and takes
   several minutes.
4. Run `git config core.hooksPath .githooks` to turn on the pre-push hook. The
   setting is shared by every worktree of the clone.
