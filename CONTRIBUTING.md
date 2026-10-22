# Contributing to Quire

Thanks for helping. This guide covers how to propose changes and what reviewers look for.

## Before you start

- **Bugs:** open an issue with the bug template. A small `.quire` file that reproduces the problem helps a lot.
- **Features:** open an issue first and explain the use case. Quire stays deliberately small (see the [philosophy](README.md#philosophy)), so a feature that adds permanent UI, accounts, sync or plugins will probably be declined, however well built.
- **Security problems:** do not open an issue. Follow [SECURITY.md](SECURITY.md).

## Setting up

You need Windows 10 or 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```
dotnet build Quire.slnx
dotnet test Quire.slnx
dotnet run --project src/Quire.App
```

`python tools/gen_samples.py sample-docs` creates a folder of awkward test files: broken images, odd encodings, invalid `.quire` files and a 500-cell document. It is gitignored.

### Pre-commit hooks

The repository uses [pre-commit](https://pre-commit.com) to format and check changes. CI runs the same checks, so installing the hooks saves a round trip:

```
pip install pre-commit
pre-commit install
```

On each commit this trims whitespace, validates YAML, JSON and XAML, blocks large files and private keys, lints Python with Ruff, lints the GitHub workflows with actionlint, and formats C# with `dotnet format` using `.editorconfig`. Before each push it also builds with warnings as errors and runs the tests. When a hook fixes files, review the changes, stage them and commit again. Run `pre-commit run --all-files` to check everything at once.

The dotnet hooks call `dotnet` from PATH, so it must be the .NET 10 SDK and not just a runtime.

## Making a change

1. Fork the repository and create a branch from `main`.
2. Keep each pull request to one topic. Small pull requests are reviewed faster.
3. Add or update tests. Storage, editing, export and search code is tested without a window, so most changes can be covered by unit tests.
4. Run `dotnet test Quire.slnx` (the pre-push hook does this too) and check the app by hand in both light and dark themes if you touched the UI.
5. Add a line under **Unreleased** in [CHANGELOG.md](CHANGELOG.md) for anything a user would notice.
6. Open the pull request and fill in the template.

## Code guidelines

- **Keep layers apart.** `Models`, `Storage`, `Editing` and `Export` must not reference WPF controls or the UI. Exporters read the document model, never the screen.
- **Match the surrounding code:** naming, comment density, and the `.editorconfig` settings. Comments explain why, not what.
- **Never lose user data.** Saves must stay atomic, unknown content must survive a round trip, and failures must leave the previous file intact.
- **Format changes** to `.quire` need a new `formatVersion`, a migration in `DocumentSerializer`, tests for the migration, and an update to the format section of the README.
- **Visual design:** square corners, no shadows or gradients, colors from the palette keys in `Themes/Palettes` (never hard-coded), and support for light, dark and high contrast.
- **Dependencies:** add one only when it clearly pays for itself, and check its license. Quire is MIT-licensed and cannot take GPL or AGPL dependencies. PDFsharp is pinned to 6.2.x for this reason.

## Commit messages

Use a short imperative subject line ("Add PDF page numbers", not "Added…"), then a blank line and, if needed, a body explaining why.

## Releases

Maintainers release by tagging `vX.Y.Z` on `main` after moving the **Unreleased** notes into a version section of the changelog. The release workflow builds, tests and publishes the binaries.

## Code of Conduct

By taking part you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).
