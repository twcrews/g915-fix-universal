# Release Process

This project uses GitHub Actions, GitHub releases, and git tags for versioned distribution.
Pushing a version tag triggers `.github/workflows/release.yml`, which builds the solution, runs any
compiled test assemblies it finds, validates the release payload, uploads the workflow artifact, and
creates or updates the GitHub Release.

The workflow accepts bare semver tags such as `3.3.1` and prerelease/build suffixes such as
`3.3.1-beta.1`. It also accepts the repository's existing `v`-prefixed convention, such as
`v3.3.1`.

## 1) Update version and changelog

- Update the matching version in `KeyboardRepeatFilter.csproj`:
  - `<Version>x.y.z</Version>`
- If `KeyboardHeatmap` changed, bump its version in `KeyboardHeatmap/Properties/AssemblyInfo.cs`
  (it is versioned independently of the main app).
- Add a new dated section in `CHANGELOG.md` for that version.

## 2) Build and smoke-test locally

- Build the `Release` configuration.
- Confirm `releases` is refreshed and contains:
  - `KeyboardRepeatFilter.exe`
  - `KeyboardHeatmap.exe`
  - `GameListUpdater.exe`
  - `Newtonsoft.Json.dll`
  - `config.json`
  - `gaming.json`
  - `WoW.json`
- Execute the checklist in `docs/SMOKE_TESTS.md`.
- Fix issues before tagging.

## 3) Commit and push

Example:

```bash
git add .
git commit -m "Release 3.3.1"
git push origin master
```

## 4) Tag the release

Use a semver version tag. Examples:

```bash
git tag -a 3.3.1 -m "Release 3.3.1"
git push origin 3.3.1
```

or, to follow the repository's existing tag convention:

```bash
git tag -a v3.3.1 -m "Release v3.3.1"
git push origin v3.3.1
```

## 5) Monitor GitHub Actions

After the tag is pushed, the `Build, Test, and Release` workflow will:

1. Restore NuGet packages.
2. Build `KeyboardRepeatFilter.sln` in `Release` configuration on Windows.
3. Run discovered test assemblies, if any are present.
4. Validate the `releases` payload.
5. Create a zip package and upload it as a workflow artifact.
6. Create or update the matching GitHub Release and attach the zip plus individual files from
   `releases`.

## 6) Post-release checks

- Download release assets from GitHub and run once on a clean machine/profile.
- Verify tray startup, filtering, and logs.
- Run `KeyboardHeatmap.exe` and confirm the HTML report is generated correctly.
- Confirm docs reflect final shipped behavior.
