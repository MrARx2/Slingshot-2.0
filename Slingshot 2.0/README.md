# Slingshot 2.0

Unity project for Slingshot 2.0.

## Unity Version

Open this project with Unity 6000.5.0f1.

## Repository Setup

This repository is prepared for GitHub with Unity generated folders ignored and binary assets configured for Git LFS.

Before the first add/commit on a fresh machine, install Git LFS once:

```bash
git lfs install
```

Then add the project files normally:

```bash
git add .
git status
```

Do not commit Unity-generated folders such as `Library/`, `Temp/`, `Logs/`, or `UserSettings/`; they are intentionally ignored.

## Project Files to Keep

Commit these top-level Unity folders/files:

- `Assets/`
- `Packages/`
- `ProjectSettings/`
- `.gitignore`
- `.gitattributes`

Unity will regenerate local editor files such as `.sln`, `.csproj`, `Library/`, and `Temp/` when the project is opened.
