# Highlight Colour Picker Comparison

This folder contains a zero-install HTML comparison and a standalone Windows
Forms project.  Together they compare these interfaces:

1. the current Cyotek colour wheel plus editor;
2. a native colour square;
3. a compact preset-swatch palette;
4. direct RGBA sliders;
5. HSL plus alpha sliders;
6. a compact numeric editor;
7. the standard Windows colour dialog.

Every tab edits one shared colour.  The bottom row displays and copies the
selected `ARGB(...)` and `#AARRGGBB` value.

Open `ColourPickerComparison.html` directly for the zero-install test.

## Build the Windows Forms comparison

```text
build.cmd
```

## Run

```text
run.cmd
```

Requirements: Windows 11 and the .NET 10 SDK.
