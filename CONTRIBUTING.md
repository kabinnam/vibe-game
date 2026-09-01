# Contributing

## Code style & formatting (C#)

Our C# style follows Godot's official C# style guide:
<https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_style_guide.html>

[.editorconfig](.editorconfig) is the single source of truth — 4-space indentation,
LF line endings, UTF-8, Allman braces, and explicit access modifiers. Both your editor
and the `dotnet format` CLI read it, so everyone gets the same result.

### One-time editor setup

This repo uses 4-space indentation for C#. If your global editor settings use a
different width (for example, 2 spaces for TypeScript), add a C#-scoped override so
format-on-save matches `.editorconfig`. In Cursor / VS Code `settings.json`:

```json
"[csharp]": {
    "editor.formatOnSave": true,
    "editor.defaultFormatter": "anysphere.csharp",
    "editor.tabSize": 4,
    "editor.insertSpaces": true,
    "editor.detectIndentation": false
}
```

This block is language-scoped, so it only affects `.cs` files — your other languages
keep their own settings. It requires the Anysphere C# extension (`anysphere.csharp`).

With this in place, saving a `.cs` file auto-formats it to match the project style.

### Checking / applying formatting from the CLI

Format-on-save covers day-to-day work. Reach for the `dotnet format` CLI when you want
to verify or bulk-apply style across many files:

```bash
# Check only (no changes), scoped to the files you own:
dotnet format VibeGame.sln --verify-no-changes --include Scenes/Enemies/

# Apply fixes to those files:
dotnet format VibeGame.sln --include Scenes/Enemies/
```

Rules of thumb:

- Always name the solution explicitly (`VibeGame.sln`). Both a `.sln` and a `.csproj`
  live in the repo root, and `dotnet format` errors out if you don't pick one.
- Always scope with `--include <your folders>`. Never run a bare, repo-wide
  `dotnet format` — it would reformat files other people own.
- Plain `dotnet format` also applies code-style and analyzer fixes (for example, sorting
  `using`s). For a faster whitespace-only pass, use `dotnet format whitespace ...`.
