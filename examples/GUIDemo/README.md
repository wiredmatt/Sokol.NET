# Sokol.GUI Demo

An interactive showcase for **Sokol.GUI** — the custom immediate-style retained-mode UI framework built on top of [Sokol.NET](../../README.md).

> **⚠️ Work in Progress**
> Sokol.GUI is under active development and is **not yet production-ready**.
> APIs, widget behaviour, and visual styling may change without notice.

---

## Running

```bash
# Desktop (JIT — fastest for iteration)
dotnet run --project examples/GUIDemo/GUIDemo.csproj

# Android
dotnet run --project tools/SokolApplicationBuilder -- --task build --architecture android --type release --path examples/GUIDemo

# iOS
dotnet run --project tools/SokolApplicationBuilder -- --task build --architecture ios --type release --path examples/GUIDemo
```

---

## Tabs

### Widgets
Core widget gallery: `Button`, `CheckBox`, `RadioButton`, `Slider`, `ProgressBar`, `TextBox`, `ComboBox`, and `Label`. Demonstrates event handling and live status feedback.

### Theming
Runtime theme switching between built-in Dark, Light, and High-Contrast themes. Includes a custom skin preview showing per-widget colour and corner-radius overrides.

### MVVM
Data-binding demo using a simple `ViewModel`. A counter and a live `TextBox` → `Label` binding illustrate two-way property notification without boilerplate.

### MVC
Classic Model-View-Controller pattern applied to a todo list. Shows how a `Controller` mediates between a plain C# model and a `ListBox`-based view.

### Animation
Tweened property animations with multiple easing curves (linear, ease-in/out, bounce, elastic). Demonstrates the `AnimationManager` API for animating widget size, position, and opacity.

### XML
UI tree built entirely from an XML markup string at runtime. Shows the declarative markup parser as an alternative to code-based construction.

### Extended
Advanced widgets: `SpinBox`, `NumberInput`, `ColorButton`, `GroupBox`, `Accordion`, `TreeView`, `SplitView`, `ListBox`, and toast `Notifications`. Includes a live event log.

### More
`MenuBar`, `ToolBar`, editable `TextArea`, `RichLabel` (inline colour/bold markup), `ScrollView`, `VirtualList` (100-item virtualized list), multi-select `TreeView`/`ListBox`, `ContextMenu`, `Image`, and `StatusBar`.

### ColorPicker
Full HSV colour picker with hex display, alpha channel, and preset swatches.

### PropGrid
`PropertyGrid` bound to a live object. Edit typed properties (string, float, bool, enum) and see them reflected instantly in a preview widget.

### RichText
`RichLabel` with inline colour, bold, and size tags. Also demonstrates system toast notifications triggered from code.

### BiDi
Bi-directional text in editable `TextArea` and read-only `RichLabel` — Hebrew, Arabic, and mixed LTR/RTL content rendered correctly via the built-in BiDi algorithm.

### HScroll
Horizontal scrolling `Panel` containing many widgets wider than the viewport.

### Docking
Full docking system: drag tabs between panels to split the workspace, drag divider bars to resize, and tear panels off into floating windows. Mirrors the layout structure used by the main demo shell.

### Layout
Side-by-side comparison of the available layout managers: `StackLayout` (vertical/horizontal), `WrapLayout`, `GridLayout`, `DockLayout`, and `CanvasLayout` (absolute positioning).

### DragDrop
Cross-widget drag-and-drop: reorder `ListBox` items, move `TreeView` nodes, and drag between two separate lists. Visual drop-target highlighting included.

### KbScroll
Keyboard-driven scroll navigation — demonstrates scrolling long content panels entirely via arrow keys, Page Up/Down, and Home/End without touching the mouse.

---

## Mobile Notes

On **iOS** and **Android** a floating overlay appears above the virtual keyboard whenever a `TextBox` or `TextArea` is focused, keeping input visible while the keyboard is shown.

- **Enter** on a `TextBox` dismisses the keyboard and moves focus to the next focusable widget.
- **Enter** on a `TextArea` inserts a newline (standard multi-line behaviour).
- **Tab** / **Shift+Tab** (desktop only) cycles focus through all interactive inputs in document order.

---

## Project Structure

```
examples/GUIDemo/
├── Source/
│   ├── GUIDemo-app.cs   # All tab builders and application logic
│   └── Program.cs       # Entry point
├── Assets/              # Fonts, images
└── README.md
```

The GUI framework itself lives in [`src/GUI/`](../../src/GUI/).
