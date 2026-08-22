<p align="center">
  <img src="src/ThemeForge/assets/images/favicon-96x96.png" width="96" height="96" alt="XoCrazy" />
</p>

<h1 align="center">XoCrazy</h1>

<p align="center"><b>Recolour Visual Studio while you watch.</b></p>

<p align="center">
Pick a colour. The editor repaints as you drag.<br/>
No OK button. No 400-row list. No "Default" where a colour should be.
</p>

---

## The pitch

Visual Studio's **Tools → Options → Fonts and Colors** makes you guess. You scroll a few hundred
alphabetical rows, click a colour, press OK, and only then find out what you did.

XoCrazy is a normal tool window that sits open beside your code. Change something and the code
behind it changes. That's the whole idea.

## What you get

🎨 **Live.** Drag the picker, watch `if` change colour in the file behind the window.

🎯 **Stop scrolling.** Put the caret on any token and press <kbd>Ctrl</kbd>+<kbd>K</kbd>,
<kbd>Ctrl</kbd>+<kbd>;</kbd>. The window jumps straight to the thing that painted it.

🌈 **40 themes built in.** Dracula, Nord, Gruvbox, Monokai, Solarized, Catppuccin, Tokyo Night,
One Dark, GitHub, Ayu, Rosé Pine, Everforest, Kanagawa and more. Hover a card and your actual
code previews it. Cancel puts everything back.

🧩 **Mix them.** Syntax from one theme, background from another, margins from a third. Three
independent slots, because liking Dracula's keywords doesn't mean liking its grey.

👁 **Real colours, not "Default".** Themed values are decoded to their actual RGB and shown with
an `inherited` badge, so you always know what you're looking at.

💧 **Proper picker.** HSV field, hex box, screen eyedropper, harmony generator, recent colours,
and a live WCAG contrast readout against the background actually behind the text.

↩️ **Undo that works.** <kbd>Ctrl</kbd>+<kbd>Z</kbd> per change, or **Revert all** to jump back
to how things looked when you opened the window.

💾 **It sticks.** Your theme is saved and re-applied on restart — including for the items
Visual Studio itself refuses to persist.

## Install

Grab it from the Visual Studio Marketplace, or download the `.vsix` from
[Releases](https://github.com/) and double-click it.

Works with **Visual Studio 2022** and **2026**, Community and up.

Then open it:

> **View → Other Windows → XoCrazy — Live Colors**
>
> or **Tools → XoCrazy — Live Colors**

## Build it yourself

You do **not** need the VS extension workload — the build pulls its tools from NuGet.

```bash
msbuild src/ThemeForge/ThemeForge.csproj -t:restore
```

```bash
msbuild src/ThemeForge/ThemeForge.csproj -t:rebuild -p:Configuration=Release
```

The `.vsix` lands in `src/ThemeForge/bin/Release/`. Press <kbd>F5</kbd> instead to debug into the
experimental instance.

## Honest limits

- **Editor colours only.** Tool window chrome and the command bar are a different subsystem
  driven by `.vstheme` XML, and cannot be changed live. Out of scope.
- **Bold yes, italic no.** Bold is stored per item and persists. Italic comes from the MEF format
  definition and cannot be written through this API — so the UI doesn't offer a checkbox that
  silently does nothing.
- **Switching the Visual Studio theme wipes the shell's copy of every colour.** That's the shell,
  not a bug here. XoCrazy notices and re-asserts your theme a moment later. Use **Forget saved**
  if you'd rather the new theme stand as shipped.
- **In-process extension.** The newer out-of-process model doesn't expose Fonts and Colors at all.

## Where things live

| | |
|---|---|
| Your theme | `%APPDATA%\XoCrazy\current.themeforge.json` |
| Slot choices | `%APPDATA%\XoCrazy\selection.json` |
| Diagnostic log | `%TEMP%\xocrazy.log` |

The theme file is plain JSON and shareable — the same format **Export** writes and **Import**
reads.

## Under the hood

Colours reach the screen through two layers that have to agree. `IVsFontAndColorStorage` is where
a value is *persisted*; the WPF editor paints from two MEF maps — `IClassificationFormatMap` for
text runs, `IEditorFormatMap` for margins, adornments and everything without a classification.
Writing storage alone changes nothing on screen, which is why the built-in page only repaints when
you press OK. XoCrazy writes both, on a 50 ms debounce, so a drag stays smooth and the editor
keeps up.

```
src/ThemeForge/
├─ ThemeForgePackage.cs        AsyncPackage; owns the commands
├─ ThemeForge.vsct             menu placement + the Ctrl+K,Ctrl+; binding
├─ Core/
│  ├─ FontColorStore.cs        storage read/write + cache refresh
│  ├─ EditorFormatBridge.cs    pushes colours into the maps the editor paints from
│  ├─ ColorResolver.cs         encoded colour -> real RGB (the "Default" fix)
│  ├─ ClassificationCatalog.cs the curated short list
│  ├─ FontColorNameMap.cs      classification name -> Fonts and Colors display name
│  ├─ SurfaceCatalog.cs        discovers the editor's paintable surfaces from MEF
│  ├─ ThemeStore.cs            the durable theme on disk
│  ├─ ThemeApplier.cs          re-applies it at startup / first document / theme switch
│  ├─ ThemePresets.cs          role-based palettes for the shipped themes
│  ├─ ThemeForgeSession.cs     working set, live apply, revert, import
│  ├─ LiveApplyQueue.cs        50 ms trailing debounce, coalesced per item
│  ├─ EditHistory.cs           undo/redo steps, grouped by gesture
│  ├─ Snapshot.cs              JSON capture/restore
│  ├─ CaretTargeting.cs        classification under the caret
│  ├─ ColorMath.cs             COLORREF/ARGB, HSV, WCAG contrast, harmony
│  └─ Json.cs                  minimal reader/writer, no dependencies
└─ UI/
   ├─ ThemeForgeControl.xaml   list + detail pane
   ├─ ColorPicker.xaml         HSV picker with live + committed events
   ├─ PresetPicker.xaml        preset cards, each rendered in its own palette
   └─ Eyedropper.cs            screen sampling in physical pixels
```
