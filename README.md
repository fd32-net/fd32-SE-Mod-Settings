# Mod UI

*Formerly **Mod Settings**.*

**A UI hub for Space Engineers mods.** It draws two kinds of vanilla-style windows for your mod:

- **Settings.** Your mod describes its settings in a few lines of C#. Mod UI adds a gear button next to **Options**
  in the in-game Esc menu, lists every mod that uses it, and draws a settings window for each one.
- **Values for toolbar actions.** Your mod adds *Set* actions with values (number, choice, on/off, text). When the player puts
  one on a toolbar, Mod UI asks for the values and the slot label in one window. Your mod can also open this window itself,
  for example from a terminal button. See [Input window and toolbar Set actions](#input-window-and-toolbar-set-actions).
- **Your own windows.** Your mod describes a whole window: the same controls, multi-line text and pictures, and its own
  buttons at the bottom. See [Your own windows](#your-own-windows).

[Русская версия](README.ru.md)

> **Status:** V0.10.0, published on the Steam Workshop: [fd32: Mod UI](https://steamcommunity.com/sharedfiles/filedetails/?id=3842864536) (ID `3842864536`). The settings API has been checked in-game; toolbar Set actions are new in V0.9, your own windows in V0.10.

- No chat commands, no config files to explain to players: everything happens in a familiar game window.
- Vanilla look and feel: checkboxes, sliders, text fields, combo boxes and key bindings look and behave like the
  ones in the game's own Options and Controls screens.
- One file to copy. There is no DLL to reference and nothing to install except a Workshop dependency.
- **The hub only draws the window.** Your mod keeps its values, validates them and decides what is enabled or visible.

---

## Contents

- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Settings window (Esc menu)](#settings-window-esc-menu)
  - [The window](#the-window)
  - [Building the menu](#building-the-menu)
    - [SettingsMenu](#settingsmenu)
    - [Tabs and groups](#tabs-and-groups)
    - [Info panel](#info-panel)
  - [Setting types](#setting-types)
    - [Toggle](#toggle)
    - [Number](#number)
    - [Choice](#choice)
    - [Text](#text)
    - [Color](#color)
    - [Hotkey](#hotkey)
    - [Label, Separator, Button](#label-separator-button)
    - [Common to all settings](#common-to-all-settings)
  - [Events: from opening the window to OK](#events-from-opening-the-window-to-ok)
    - [Editing: live changes, Enabled, Visible, presets](#editing-live-changes-enabled-visible-presets)
    - [Applying: validate and refuse](#applying-validate-and-refuse)
  - [Messages and confirmations](#messages-and-confirmations)
  - [Saving values](#saving-values)
    - [Option 1: built-in saving](#option-1-built-in-saving)
    - [Option 2: your mod stores the values itself](#option-2-your-mod-stores-the-values-itself)
  - [Your own window](#your-own-window)
- [Input window and toolbar Set actions](#input-window-and-toolbar-set-actions)
  - [The input window](#the-input-window)
  - [Toolbar Set actions](#toolbar-set-actions)
  - [Your own action](#your-own-action)
  - [Window without a toolbar](#window-without-a-toolbar)
- [Your own windows](#your-own-windows)
  - [Layout](#layout)
  - [Values are live](#values-are-live)
  - [Multi-line text and pictures](#multi-line-text-and-pictures)
  - [Buttons at the bottom](#buttons-at-the-bottom)
  - [ModWindow members](#modwindow-members)
- [Without the hub, load order](#without-the-hub-load-order)
- [Upgrading from Mod Settings](#upgrading-from-mod-settings)
- [Rules and limitations](#rules-and-limitations)
- [Troubleshooting](#troubleshooting)
- [Repository layout](#repository-layout)
- [License](#license)

---

## Quick start

**1. Copy the client file** [`Client/ModUI.cs`](Client/ModUI.cs) into your mod **unchanged**:

```
<YourMod>\Data\Scripts\<YourMod>\ModUI.cs
```

Each mod is compiled separately, so the same class names in different mods do not conflict.

**2. Make [Mod UI](https://steamcommunity.com/sharedfiles/filedetails/?id=3842864536) (Workshop ID `3842864536`) a required dependency** of your mod on the Steam Workshop,
so the game downloads the hub automatically. To test locally, put this repository into `%AppData%\SpaceEngineers\Mods\Mod UI` and add both mods to the world.

**3. Describe your settings** in a session component:

```csharp
using Fd32.ModUI;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Input;

[MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
public class MyModSettings : MySessionComponentBase
{
    public static SettingsMenu Menu;
    public static NumberSetting Speed;
    public static ToggleSetting ShowHints;
    public static HotkeySetting LinkKey;

    public override void LoadData()
    {
        // id: a permanent key (never change it); name and version are shown in the menu
        Menu = new SettingsMenu("mymod", "My Mod", "1.0");
        Menu.Info("# My Mod\nWhat the mod does and how to use it.");

        Speed     = Menu.Number("speed", "Cruise speed", 20).Range(0, 100).Step(5).Unit("m/s");
        ShowHints = Menu.Toggle("hints", "Show hints", true);
        LinkKey   = Menu.Hotkey("link", "Link", MyKeys.R);

        Menu.LoadFromLocalStorage();                       // optional: built-in saving,
        Menu.Applied += () => Menu.SaveToLocalStorage();   // or store values yourself
        Menu.Register();                                   // hand the menu to the hub
    }

    public override void UpdateAfterSimulation()
    {
        if (LinkKey.IsNewPressed())
            MyAPIGateway.Utilities.ShowNotification("Link! Speed = " + Speed.Value);
    }

    protected override void UnloadData()
    {
        Menu.Dispose();
    }
}
```

**4. In the game:** press **Esc**, click the **gear** to the right of **Options**, and pick **My Mod**.
Change something and press **OK**. The new values are now in `Speed.Value`, `ShowHints.Value`, `LinkKey`.

> **Saving is optional.** The two `LocalStorage` lines turn on the built-in saving of Mod UI. Remove them if your
> mod stores its values itself; see [Saving values](#saving-values) for an example.

That is all a typical mod needs. The rest of this document covers every feature in detail.

For a toolbar *Set* action or an input window, see [Input window and toolbar Set actions](#input-window-and-toolbar-set-actions).

---

## How it works

```
 your mod                                       Mod UI (hub)
┌──────────────────────────┐   SendModMessage   ┌────────────────────────────┐
│ ModUI.cs (client)        │ ─────────────────► │ gear in the Esc menu       │
│  menu description,       │                    │ list of mods               │
│  values, events          │ ◄───────────────── │ settings window            │
└──────────────────────────┘ edits, OK, buttons └────────────────────────────┘
```

- **The hub** is a separate Workshop mod. It draws the gear, the list of mods and the window. It does **not** store
  or validate anything.
- **The client file** holds the whole API: the menu description, the current values, events and hotkey checks.
  Your code talks only to it and never sees the protocol.
- Settings are **personal** (client-side, per player). Each player sees their own values.

---

## Settings window (Esc menu)

Everything about the settings window: the gear in the Esc menu, the menu your mod describes, events and saving.

### The window

```
┌───────────────────────────── My Mod ───────────────────────────────[×]┐
│ General        │ Cruise speed                    │ # My Mod           │
│ HUD            │ ═════●═══════════════  [ 20 ]   │ What the mod does  │
│ Keys           │ Show hints                 [✓]  │ and how to use it. │
│                │ ▼ Advanced                      │                    │
│                │   Units                         │                    │
│                │   [ m/s                     ▼ ] │                    │
│                │                                 │                    │
│ [ Defaults ]                                [ Cancel ]  [   OK   ]    │
└───────────────────────────────────────────────────────────────────────┘
   tabs              settings of the selected tab       info panel
```

- **Left**: the tabs. Settings added directly to the menu go to the first tab, **General**.
- **Middle**: the settings of the selected tab. It scrolls with the mouse wheel and a scroll bar.
- **Right**: the info panel, free text from your mod. It is omitted when the mod sets no info.
- **OK / Cancel / Defaults** work as in the vanilla Options screen:
  - nothing reaches your mod's `.Value` before **OK**; **Cancel**, **Esc** and **×** discard the changes;
  - **OK** is disabled while nothing has changed. **Enter** never presses OK: it only confirms the text field;
  - **Defaults** resets **the whole mod** (all tabs) to its defaults in the window. They still need **OK**.
- The hub's own buttons and captions are in Russian when the game language is Russian, and in English otherwise.

### Building the menu

Items appear in the window in the order you add them. `SettingsMenu`, `SettingsTab` and `SettingsGroup` share the
same methods for adding items (`Toggle`, `Number`, `Choice`, `Text`, `Hotkey`, `Label`, `Separator`, `Button`).

```
SettingsMenu            a line in the gear menu + the window titled with the mod name
 ├─ items               → the "General" tab
 ├─ Group               → a section header on "General"
 └─ Tab                 → a tab in the list on the left
     ├─ items
     └─ Group           → a section header; .Collapsible() folds it
         └─ items
```

#### SettingsMenu

```csharp
Menu = new SettingsMenu("mymod", "My Mod", "1.0");
```

| Member | Description |
|---|---|
| `new SettingsMenu(id, name, version)` | `id` is the permanent key of your mod. Do not change it between versions. `name` and `version` are shown in the gear menu; `name` is also the window title. |
| `Tab(title)` | Adds a tab. |
| `Group(title)` | Adds a group to the **General** tab. |
| `Info(text)` / `Info(Func<string>)` | Text for the info panel. See [Info panel](#info-panel). |
| `CustomWindow(Action)` | Opens your own window instead of the standard one. See [Your own window](#your-own-window). |
| `Register()` | Hands the menu to the hub. Call it once, after describing all items, usually at the end of `LoadData`. |
| `Dispose()` | Removes the menu. Call it in `UnloadData`. |
| `IsConnected` | `true` once the hub has accepted the menu. |
| `Opened`, `Editing`, `Applying`, `Applied`, `Closed` | Window events. See [Events](#events-from-opening-the-window-to-ok). |
| `ShowMessage`, `ShowConfirm` | Dialogs. See [Messages and confirmations](#messages-and-confirmations). |
| `LoadFromLocalStorage()`, `SaveToLocalStorage()` | Optional saving helper. See [Saving values](#saving-values). |

The gear menu lists mods in alphabetical order of their names.

#### Tabs and groups

```csharp
var hud = Menu.Tab("HUD");
var hints = hud.Group("Hints");                     // a section header
var adv   = hud.Group("Advanced").Collapsible(true); // folds on click; true = starts folded
adv.Toggle("debug", "Debug overlay", false);
```

- If the mod adds no tabs, the window has a single **General** tab.
- If you add items directly to the menu **and** add tabs, **General** comes first, followed by your tabs.
- `Collapsible(startCollapsed = false)` makes a group fold and unfold when its header is clicked.
- Groups are one level deep: a group is created from a menu or a tab, not from another group.

#### Info panel

```csharp
Menu.Info("# My Mod\nLine one.\nLine two.");                     // shared by all tabs
hud.Info("# HUD\nThese settings change the on-screen hints.");    // this tab only
Menu.Info(() => "# Status\nTrains running: " + TrainCount);       // live text
```

- A line starting with `# ` is shown as a heading. Long lines wrap.
- `Func<string>` is called again about twice a second. Use it for live status.
- A tab's own `Info` replaces the menu's `Info` for that tab.
- When there is no info at all, the info panel is hidden and the middle column becomes wider.

### Setting types

Each method returns a **setting object**. Keep it in a field and read `.Value` wherever you need it.
Fine-tuning uses chained calls: `.Range(0, 100).Step(5).Unit("m/s")`.

`key` is the setting's permanent key, used by saving and by the protocol. It must be **unique within the menu**.

| Method | `.Value` type | In the window |
|---|---|---|
| `Toggle(key, label, default)` | `bool` | vanilla checkbox |
| `Number(key, label, default)` | `float` (+ `IntValue`) | slider, text field and −/+ buttons in any combination |
| `Choice(key, label, defaultKey)` | `string` (option key) | vanilla combo box |
| `Text(key, label, default = "")` | `string` | vanilla text field |
| `Hotkey(key, label, MyKeys, MyKeyboardModifiers = None)` | `HotkeyValue` | key binding row, like the vanilla Controls screen |
| `Color(key, label, default)` | `Color` (VRageMath) | swatch and Hex field; H, S, V sliders on click |

#### Toggle

```csharp
ShowHints = Menu.Toggle("hints", "Show hints", true);
if (ShowHints.Value) { ... }
```

#### Number

```csharp
Speed = Menu.Number("speed", "Cruise speed", 20)
            .Range(0, 120)          // the value always stays within it; no range → no slider
            .Step(5)                // slider moves in steps, −/+ add a step, typed values round to the step
            .Decimals(0)            // digits after the point (default: derived from the step)
            .Unit("m/s")            // shown next to the value
            .Slider().Field().Buttons();   // which controls to show

float s = Speed.Value;
int   i = Speed.IntValue;           // rounded to an integer
```

| Call | Effect |
|---|---|
| `Range(min, max)` | The value is clamped to the range. The slider needs a range. |
| `Step(step)` | Applies everywhere: slider, −/+ buttons and typed input (rounded from `min`). |
| `Decimals(n)` | 0–6 digits. By default it follows the step (step 0.1 → 1 digit). |
| `Unit(text)` | Unit label. |
| `Slider()`, `Field()`, `Buttons()` | Choose the controls. If none is called: **with a range**, slider + field; **without**, field + buttons. |

Values set from code are normalized too: `Speed.Value = 1000` with `Range(0, 120)` gives `120`.
Commas and dots are both accepted as the decimal separator in the field.

#### Choice

```csharp
Units = Menu.Choice("units", "Speed units", "ms")
            .Option("ms",  "m/s")
            .Option("kmh", "km/h")
            .Option("kn",  "knots");

if (Units.Value == "kmh") { ... }
```

- `Option(key, label = key)`: `key` is permanent (code and saved files use it), `label` is what the player sees.
- `.Value` is the option **key**. An unknown key (for example, an option removed in a new version) falls back to the default.
  If the default key is not among the options, the first option becomes the default.
- The list shows up to 10 rows and scrolls beyond that. It opens upwards when there is no room below.
- `Keys` returns all option keys.

#### Text

```csharp
ShipName = Menu.Text("name", "Ship name", "Wanderer")
               .MaxLength(32)               // default 256
               .Placeholder("Enter a name...");
```

- A single line; line breaks are replaced with spaces. Longer text is cut to `MaxLength`.
- It works like the vanilla text field: click to place the cursor, select with Shift + arrows / Home / End,
  drag with the mouse or Shift + click, Ctrl+A, Ctrl + arrows move by word, Ctrl+Backspace / Ctrl+Delete delete a word,
  Ctrl+C / Ctrl+X copy or cut the selection, and held keys repeat.
- **Enter** confirms the input and keeps the window open.
- **Ctrl+V is not available**: the mod whitelist does not allow reading the clipboard.
- An empty or otherwise invalid value is refused in [`Applying`](#applying-validate-and-refuse).

#### Color

```csharp
Glow = Menu.Color("glow", "Glow color", new Color(255, 128, 0));          // using VRageMath;
Tint = Menu.Color("tint", "HUD tint", new Color(80, 160, 255, 128)).Alpha();
```

- The row shows a color swatch and a **Hex** field. Click the swatch to show the **H** (hue), **S** and **V** sliders,
  as in the vanilla crosshair color picker (Options → Game); click again to hide them.
- `.Alpha()` adds the **Transparency** slider, and Hex gets 8 digits: `#RRGGBBAA`. Without it the color is opaque (A = 255)
  and Hex is `#RRGGBB`.
- Hex accepts the value with or without `#`; Enter or clicking elsewhere confirms it, and a wrong value is reverted.
- Saving and the protocol use the string `"#RRGGBBAA"`.
- Needs Mod UI V0.9.4 or newer (the Workshop item updates by itself). An older hub just does not show the row.

#### Hotkey

```csharp
LinkKey  = Menu.Hotkey("link",  "Link",         MyKeys.R);
MultiKey = Menu.Hotkey("multi", "Multi-select", MyKeys.R, MyKeyboardModifiers.Control)
               .CheckConflicts()   // highlight in red if the combination is taken
               .EvenInMenus();     // also fire while chat or a window is open

public override void UpdateAfterSimulation()
{
    if (LinkKey.IsNewPressed())  StartLink();   // pressed this frame
    if (MultiKey.IsPressed())    ...            // held now
    if (LinkKey.IsNewReleased()) ...            // released this frame
}

string hint = "Press " + LinkKey.DisplayText + " to link";   // "Ctrl + R" in the game's key names
```

**One key per action**, plus optional Ctrl / Shift / Alt. Any keyboard key or **any mouse button** can be bound,
including the left and right buttons (`MyKeys.LeftButton`, `RightButton`, `MiddleButton`, `ExtraButton1/2`).

**What the player sees:** a row like the vanilla Controls screen, with the label on the left and a key button on the right.
Clicking the button opens a "Press a key or mouse button" prompt: **Esc** cancels and **Delete** clears the binding.
The cursor is hidden while the prompt is waiting.

**Modifiers are exact:**
- `R` does **not** fire while Ctrl is held, because `R` and `Ctrl + R` are different hotkeys.
- If a modifier itself is bound (for example, just `Ctrl`), it fires even with other keys held.
- `IsNewReleased()` does not check modifiers.

**Chat and windows:** `IsPressed` / `IsNewPressed` / `IsNewReleased` return `false` while chat or any window
with a mouse cursor is open, so the key does not fire while the player is typing. `.EvenInMenus()` turns this off for one hotkey.

**Conflicts** (`.CheckConflicts()`, **off by default**): the key is drawn in red when the same combination is used by
the game's controls, by another hotkey of your mod, or by a hotkey of another mod in the hub. The tooltip says where.
This is only a warning: the player can still save. It is off by default because only your mod knows where its key is
active (in the game, R is taken only while holding a weapon), and mods that do not use the hub are invisible anyway.

**An unbound key** (the player pressed Delete) has `Value.IsNone == true`. Such a hotkey never fires.
If your mod needs a key, refuse an empty one in `Applying`.

`HotkeyValue` (a struct):

| Member | Description |
|---|---|
| `Key`, `Ctrl`, `Shift`, `Alt` | The key and its modifiers. Left/right Ctrl, Shift and Alt are treated as one. |
| `IsNone` | No key bound. `HotkeyValue.None` is the empty value. |
| `IsModifierKey` | The bound key is Ctrl, Shift or Alt itself. |
| `DisplayText` | `"Ctrl + R"` in the game's key names, for your HUD hints. |
| `new HotkeyValue(MyKeys, MyKeyboardModifiers)` | Creates a value, for example for `e.Set` or `.Value =`. |
| `ToString()` / `TryParse` | `"Ctrl+Shift+R"`, the format used in saved files. `""` means no key. |

#### Label, Separator, Button

These are not settings but layout and actions:

```csharp
Menu.Label("Text across the whole column. It wraps by words and is drawn grey like vanilla hints.");
Menu.Separator();
Menu.Button("Reset statistics", () => ResetStats())
    .Tooltip("Clears all collected statistics");
```

- `Label(text)` and `Separator()` return an `Entry`. You can toggle its `Enabled` / `Visible`.
- `Button(label, action)` runs **immediately**, without OK. To ask the player first, call
  [`ShowConfirm`](#messages-and-confirmations) inside the action.

#### Common to all settings

```csharp
T     Value { get; set; }    // current value; setting it from code does not raise Changed and saves nothing
T     Default { get; }       // used by the Defaults button
event Action<T> Changed;     // the player changed it (after an OK that the mod did not refuse)
.Tooltip("text")             // hover tooltip, like vanilla
bool  Enabled, Visible       // on any item: setting, button, label, separator, group
string Key, Label            // what you passed in
```

- `Enabled = false` draws the item grey and makes it non-interactive. On a group, it applies to the whole group.
- `Visible = false` hides the item, and the space it took collapses.
- You can change `Enabled` / `Visible` at any time. The window picks the change up by itself. The usual place is `Menu.Editing`.
- If you set `.Value` while the window is open, the window shows the new value only on rows the player has not touched yet.

### Events: from opening the window to OK

The hub does not try to guess your mod's logic. It reports every step, and your mod decides what happens.

```
Opened ─► Editing (Changed = null) ─► Editing ... Editing ─► OK ─► Applying ─┬─ refused ─► error message, window stays open
                                                                            └─ accepted ─► .Value updated ─► Changed ─► Applied ─► Closed(true)
                                                       Cancel / Esc / × ─► Closed(false)
```

| Event | When |
|---|---|
| `Opened` | The window was opened. `Editing` with `Changed == null` follows immediately. |
| `Editing(SettingsEdit e)` | The player changed something in the window, **before OK**. |
| `Applying(SettingsApply e)` | **OK** was pressed. The new values are not applied yet, and you can refuse them. |
| setting `.Changed(value)` | For each changed setting, after its `.Value` is updated. |
| `Applied` | Once, after all values are applied. This is the place to save. |
| `Closed(bool ok)` | The window closed: `true` via OK (after `Applied`), `false` via Cancel, Esc or ×. |

Until OK, values live in the **window draft**. `.Value` changes only after OK is accepted.

#### Editing: live changes, Enabled, Visible, presets

```csharp
Menu.Editing += e =>
{
    // e.Changed: which setting changed (null when the window just opened or after Defaults)
    // e.New(setting): its value in the window, not yet applied
    HudGroup.Enabled = e.New(ShowHints);           // grey out the group while the box is unchecked
    Code.Visible     = e.New(Units) != "ms";       // show a row only for some options

    if (e.Changed == Preset)                       // a preset fills in other values
    {
        if (e.New(Preset) == "fast") { e.Set(Accel, 2f); e.Set(Speed, 90f); }
    }
    else if (e.Changed == Accel || e.Changed == Speed)
        e.Set(Preset, "custom");                   // edited by hand → "custom"
};
```

- **When it fires:** a checkbox, combo box or hotkey fires at once; a slider fires continuously while being dragged;
  a text or number field fires when the input is confirmed (Enter or clicking elsewhere).
- `e.New(s)` gives the value in the window. `e.IsChanged(s)` tells whether it differs from the current `.Value`.
- `e.Set(s, value)` changes another value in the window, as if the player had changed it. It still needs OK.
  The value is normalized (range, step, options, length).
- After Defaults, `Editing` is called with `Changed == null`, so the same code puts Enabled / Visible right.

#### Applying: validate and refuse

```csharp
Menu.Applying += e =>
{
    if (e.New(ShowHints) && e.New(HudScale) > 1.5f)
        e.Cancel("Hints do not fit on screen at a scale above 1.5", HudScale);

    if (e.New(ShipName).Trim().Length == 0)
        e.Cancel("Ship name cannot be empty", ShipName);

    if (e.New(LinkKey).IsNone)
        e.Cancel("Assign a key for Link", LinkKey);
};
```

- `e.Cancel(message, focus = null)`: the hub shows the message in a vanilla-style **Error** window over the settings.
  The settings window **stays open** and keeps everything the player entered. `focus` highlights that setting's label in red.
- `e.New(s)` / `e.IsChanged(s)` work as in `Editing`. `e.IsCancelled` tells whether a handler has already refused.
- You do not need to check simple bounds: `Number` already keeps `Range` / `Step`, `Text` keeps `MaxLength`,
  and `Choice` accepts only its options.
- Write the message in the player's language. Your mod can read the game language as well.
- If `Applying` throws, the hub shows "The mod failed to apply the settings", and the exception goes to the game log.

### Messages and confirmations

```csharp
Menu.ShowMessage("Statistics cleared.");                         // window with OK
Menu.ShowMessage("Done.", "My Mod");                             // with a caption

Menu.Button("Reset everything", () =>
    Menu.ShowConfirm("Reset all settings and statistics?", yes =>
    {
        if (yes) ResetAll();
    }));                                                          // Yes / No, Esc = No
```

- Both are shown over the game or over the settings window.
- Without the hub, `ShowMessage` falls back to an on-screen notification. `ShowConfirm` does not ask at all, and
  writes a line to the log instead.

### Saving values

**Saving is optional.** The hub itself stores nothing. The client file can save and load the values for you,
but you do not have to use that: your mod can store them any way it likes. Choose one of the two options below.

#### Option 1: built-in saving

Two lines, and the values survive a game restart:

```csharp
// after describing all settings, before Register()
Menu.LoadFromLocalStorage();
Menu.Applied += () => Menu.SaveToLocalStorage();
```

- File: `ModSettings_<id>.txt` in **your mod's** local storage (`%AppData%\SpaceEngineers\Storage\<your mod>\`).
- Format: `key=value` lines; `#` starts a comment.
  ```
  # My Mod 1.0: settings (Mod UI), key=value lines
  speed=20
  hints=true
  units=kmh
  link=Ctrl+R
  ```
- On loading: a key missing from the file keeps its current value, unknown keys are skipped, and a corrupt value
  becomes the default (with a line in the log). `Changed` is not raised.

#### Option 2: your mod stores the values itself

Use this when the mod already has its own config, or needs a different file, format or place.
Do not call `LoadFromLocalStorage` / `SaveToLocalStorage`. Instead, put your values into `.Value` at load time and
read them back after OK. For example, a mod that keeps an XML config:

```csharp
using System;
using Fd32.ModUI;
using Sandbox.ModAPI;
using VRage.Utils;

// Your mod's own config: any class SerializeToXML can handle (public fields or properties)
public class MyConfig
{
    public float  Speed     = 20;
    public bool   ShowHints = true;
    public string LinkKey   = "R";              // HotkeyValue.ToString(): "R", "Ctrl+R", "" = none

    const string FileName = "MyMod.xml";

    public static MyConfig Load()
    {
        try
        {
            if (MyAPIGateway.Utilities.FileExistsInLocalStorage(FileName, typeof(MyConfig)))
                using (var r = MyAPIGateway.Utilities.ReadFileInLocalStorage(FileName, typeof(MyConfig)))
                    return MyAPIGateway.Utilities.SerializeFromXML<MyConfig>(r.ReadToEnd());
        }
        catch (Exception e)
        {
            MyLog.Default.WriteLine("MyMod: config not loaded: " + e.Message);
        }
        return new MyConfig();
    }

    public void Save()
    {
        using (var w = MyAPIGateway.Utilities.WriteFileInLocalStorage(FileName, typeof(MyConfig)))
            w.Write(MyAPIGateway.Utilities.SerializeToXML(this));
    }
}
```

```csharp
public override void LoadData()
{
    Menu      = new SettingsMenu("mymod", "My Mod", "1.0");
    Speed     = Menu.Number("speed", "Cruise speed", 20).Range(0, 100).Step(5).Unit("m/s");
    ShowHints = Menu.Toggle("hints", "Show hints", true);
    LinkKey   = Menu.Hotkey("link", "Link", MyKeys.R);

    // 1. your config → settings (no LoadFromLocalStorage)
    Config = MyConfig.Load();
    Speed.Value     = Config.Speed;
    ShowHints.Value = Config.ShowHints;
    HotkeyValue key;
    if (HotkeyValue.TryParse(Config.LinkKey, out key))
        LinkKey.Value = key;

    // 2. after OK → settings back into your config and its file (no SaveToLocalStorage)
    Menu.Applied += () =>
    {
        Config.Speed     = Speed.Value;
        Config.ShowHints = ShowHints.Value;
        Config.LinkKey   = LinkKey.Value.ToString();
        Config.Save();
    };

    Menu.Register();
}
```

- Setting `.Value` does not raise `Changed` and saves nothing. It is just "this is the current value".
- Values are normalized when assigned: a hand-edited `Speed = 500` in the XML becomes `100` because of `Range(0, 100)`.
- **Defaults** in the window come from the defaults you pass to `Number` / `Toggle` / ..., not from your config.
- Instead of `Applied`, you can save individual settings in their `Changed` handlers.

### Your own window

If the standard window is not enough:

```csharp
// 1. Replace the standard window: clicking your mod in the gear menu calls your action
Menu = new SettingsMenu("radar", "Radar Plus", "1.2");
Menu.CustomWindow(() => new RadarSettingsScreen().Open());   // your MyModScreen
Menu.Register();

// 2. Keep the standard window and add a button for complex parts
tab.Button("Route editor...", () => new RouteEditor().Open());
```

A mod window (`MyModScreen`) opens over the hub's window. **Esc** returns to the settings.
With Mod UI V0.10 a button can also open a vanilla-style window described with the same controls — see [Your own windows](#your-own-windows).

---

## Input window and toolbar Set actions

Mod UI draws one window that asks for values. Your mod uses it for toolbar *Set* actions,
or opens it itself, for example from a terminal button.

### The input window

```
┌──────────────────── Set light ────────────────────[×]┐
│ Radius                                               │
│ [−] ══════●═════════════════  [  5.0  ]  [+]  m      │
│ Mode                                                 │
│ [ Blink                                          ▼ ] │
│ Light                                                │
│ [ On                                             ▼ ] │
│ ──────────────────────────────────────────────────── │
│ Toolbar label                                        │
│ [ R5.0 On                                          ] │
│ ──────────────────────────────────────────────────── │
│                 [   OK   ]  [ Cancel ]               │
└──────────────────────────────────────────────────────┘
```

**What the player sees:** they drag the action onto a toolbar (G screen, timer, event controller…). One window asks
for all values and the label under the slot icon.
- The first text field has the focus with its value selected, so the player can type a number and press **Enter**.
- **Enter** moves to the next text field. In the last one, in the label field or outside the fields, it presses **OK**.
- The label follows the values until the player edits it by hand.
- **Cancel**, **Esc** and **×** close the window, and the action is not placed on the toolbar.

### Toolbar Set actions

Since update 1.211 an action can carry values. Each toolbar slot stores its own values and runs the action with them,
like the vanilla *Set Intensity* or *Set Height*. The game's own input dialog is not available to mods, so Mod UI draws one.

```csharp
using Fd32.ModUI;
using Sandbox.ModAPI;
using VRage.Game.Components;

[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
public class MyActions : MySessionComponentBase
{
    public override void BeforeStart()
    {
        var a = ModUI.SetAction<IMyLightingBlock>("MyMod_SetLight", "Set light");   // action id, name in the G list
        var radius = a.Number("Radius", 5).Range(0.5f, 20).Step(0.5f).Unit("m").Slider().Field().Buttons();
        var mode   = a.Choice("Mode", "blink").Option("steady", "Steady").Option("blink", "Blink");
        var on     = a.Toggle("Light", true).Texts("On", "Off");
        a.Label("R{0} {2}");                    // toolbar label: {0} = radius, {2} = light
        a.Enabled(b => ModUI.IsReady);          // no hub → no action in the G list (your choice)
        a.Check = v => v.Get(radius) > 15 && !v.Get(on)
            ? v.Error(radius, "Radius above 15 m only with the light on") : null;
        a.Run = (block, v) =>                   // the slot was pressed: its own values
        {
            block.Radius  = v.Get(radius);
            block.Enabled = v.Get(on);
        };
        a.Register();
    }
}
```

| Value | `v.Get(...)` | In the window |
|---|---|---|
| `Number(label, default)` | `float` | slider, field and −/+ buttons, the same options as [Number](#number): `Range`, `Step`, `Decimals`, `Unit`, `Slider` / `Field` / `Buttons` |
| `Choice(label, defaultKey)` + `.Option(key, label)` | `string` (option key) | combo box |
| `Toggle(label, default)` + `.Texts(on, off)` | `bool` | combo box with two options; without `Texts` — On / Off (Вкл / Выкл in Russian) |
| `Text(label, default = "")` + `.MaxLength`, `.Placeholder` | `string` | text field |
| `Color(label, default)` + `.Alpha()` | `Color` | swatch, Hex field and H, S, V sliders (always shown here); stored in the slot as `"#RRGGBBAA"` |

Values have no keys: they are identified by **their order**. `.Tooltip(text)` works as in settings.

**The toolbar label**
- `Label(template)`: `{0}`, `{1}`… are the values in order (`string.Format`). A number is shown with its decimals but **without** the unit;
  a choice or on/off shows the option text, a color shows `#RRGGBB`.
- Without a template the label is the values separated by spaces (a number with its unit).
- **9 characters at most**: the game cuts longer labels. The field accepts 9 characters, and a template that gives more is cut,
  with a line in the hub's log.
- `AskLabel(false)` removes the label field; the label is then made from the template.

**`SetAction<TBlock>` members**

| Member | Description |
|---|---|
| `ModUI.SetAction<TBlock>(id, name)` | `TBlock` is a block interface, such as `IMyLightingBlock`. `id` is permanent; `name` is shown in the G list and as the window title. |
| `Icon(path)` | Toolbar icon (`.dds`). Default: the vanilla `Textures\GUI\Icons\Actions\SetValue.dds`. |
| `Enabled(Func<IMyTerminalBlock, bool>)` | Whether the block offers the action. By default, always. |
| `Label(template)`, `AskLabel(bool)` | See above. |
| `Check = v => …` | Called on OK. Return `null` to accept, or a message to refuse: the player sees it and the window stays open. `v.Error(value, text)` also turns that value's label red. |
| `Run = (block, v) => …` | The slot was pressed (by the player, a timer, an event controller…). `v.Get(value)` gives the slot's values. |
| `Register()` | Creates the action and adds it to `TBlock`. Call it once. |
| `TerminalAction` | The created `IMyTerminalAction`, if you want to adjust it yourself. |

**Rules**
- Call `Register()` in `BeforeStart` (or wherever your mod adds terminal controls), **also on a dedicated server**:
  timers and event controllers run actions there.
- **Add new values only at the end** and never reorder them. Slots store values by position. Values missing from old
  slots, or of the wrong type, fall back to the defaults. A choice is stored as its key, so you can reorder its options.
- `ModUI.IsReady` asks the hub whether it can draw the window (at most once a second). Without the hub nothing is asked,
  and the action is not placed. Whether to hide the action is up to your mod.
- On a dedicated server `ModUI.IsReady` is always `true`: the game checks an action's `Enabled` before it runs it too,
  and timers there must run it. Client files before V0.9.2 returned `false` there - copy the new `ModUI.cs`.

### Your own action

If you build the `IMyTerminalAction` yourself, use a form with the same values (see the table in [Toolbar Set actions](#toolbar-set-actions)):

```csharp
var form = ModUI.Form("Go to station");
var station = form.Number("Station", 1).Range(1, 9).Step(1).Field().Buttons();
form.Label("St {0}");

form.AddParameterDefinitions(action);                                      // the defaults, in order
action.DoUserParameterRequestWithItem = (item, done) => form.Ask(item, done);
action.ActionWithParameters = (block, args) => GoTo(block, (int)form.Read(args).Get(station));
```

### Window without a toolbar

The same window without a toolbar slot (V0.9.3 client file), for example from a terminal button: `form.Open(ok, cancel)`.
There is no label field; `ok` gets the values, `cancel` (optional) runs on Cancel / Esc, or at once without the hub.

```csharp
var form = ModUI.Form("Rename station");
var name = form.Text("Name", current).MaxLength(40);
form.Open(v => Rename(block, v.Get(name)));
```

---

## Your own windows

A mod can open a whole window of its own (from a terminal button, a hotkey, a settings button…): the same vanilla controls
as in settings, plus multi-line text and pictures, with the buttons at the bottom that the mod describes.
Needs Mod UI V0.10.0 or newer.

```csharp
var w = ModUI.Window("Rename ship");                     // narrow, one column; .Wide() — like the settings window
w.TextBlock("# Rename\nThe new name is used by all copies.");
var name  = w.Text("Ship name", "Wanderer").MaxLength(32);
var count = w.Number("Copies", 1).Range(1, 10).Step(1).Field().Buttons();
var note  = w.Label("");
w.Editing += s => note.Text = count.Value > 5 ? "This takes a while." : "";   // values are live
w.Footer("Rename", () =>
{
    if (name.Value.Trim().Length == 0) { w.ShowMessage("The name cannot be empty."); return false; }
    Rename(name.Value, count.IntValue);
    return true;                                         // true — close the window
}).Default();                                            // Enter
w.Footer("Cancel").Cancel();                             // Esc and ×
w.Open();
```

### Layout

- **Narrow** (default): one column, like the input window; the height follows the content (up to 0.9 of the screen, then it scrolls).
  Tabs, if any, are shown one under another as groups.
- **Wide** (`.Wide()`): like the settings window. Tabs (`w.Tab(title)`) are listed on the left when there is more than one,
  `w.Info(text | Func<string>)` fills the right column.
- Groups (`Group(title).Collapsible()`), `Label`, `Separator` and `Button` work as in settings.

### Values are live

- The controls are the same as in settings (`Toggle`, `Number`, `Choice`, `Text`, `Hotkey`, `Color`), but without keys:
  `w.Number("Speed", 20)`.
- **`.Value` changes at once** when the player changes something, and the setting's `Changed` fires at once too.
  Then `w.Editing` is called with that setting: decide here what is `Enabled` and `Visible`.
- **`x.Value = …` from your mod** is shown by the window within a fraction of a second. So are new labels
  (`setting.Label`, `button.Label`, `label.Text`, `group.Title`, `textBlock.Text`).
- There is no draft: Cancel does not roll anything back. If you need that, keep the old values yourself.

### Multi-line text and pictures

| Method | What it shows |
|---|---|
| `TextBlock(text)`, `TextBlock(() => text)` | Read-only text at full width, wrapped; a line `# …` is a heading, as in `Info`. No scroll bar of its own: the window scrolls. A `Func` is read again twice a second. |
| `Image(material)` + `.Width(fraction)`, `.Aspect(w / h)`, `.Tooltip` | A picture at full width (or a part of it, centred); the height follows the texture's proportions. |

`material` is the name of a texture the game knows: a `TransparentMaterial` from your mod's SBC (that is how a mod's own
`.dds` gets a name, see [`Data/TransparentMaterials_ModUI.sbc`](Data/TransparentMaterials_ModUI.sbc)), an LCD texture
(`"Construction"`, `"Online_wide"`…) or a generated texture (`tex.MaterialId.String`). A plain file path does not work:
the game draws screen sprites only by material name. A missing material is shown hatched, with a line in the hub's log.

### Buttons at the bottom

| Member | Description |
|---|---|
| `w.Footer(label, () => …)` | A button. Return `true` to close the window. Without a handler the button just closes it. |
| `.Default()` | Enter (outside a text field) presses it. |
| `.Cancel()` | Esc and × press it; the window closes only if it returns `true`. Without a Cancel button Esc and × just close the window. |
| `.Left()` | Put it on the left (like Defaults in settings). The others go on the right; in a narrow window without left buttons they are centred. |
| `.Tooltip(text)`, `.Label`, `.Enabled`, `.Visible` | As for other controls. |

### ModWindow members

| Member | Description |
|---|---|
| `ModUI.Window(caption)` | Creates the window description (nothing is shown yet). Keep it in a field and open it again later. |
| `Open()` | Shows the window. `false` if the hub is missing or older than V0.10.0 (a line in the log). |
| `Close()`, `IsOpen` | Close the window from your code. |
| `Rebuild()` | You added or removed controls, or changed `Caption`: the window is rebuilt, the tab and scrolling are kept. |
| `Opened`, `Editing`, `Closed` | Events: opened; the player changed a value; closed (by any button, Esc, × or `Close()`). |
| `ShowMessage`, `ShowConfirm` | A message or a Yes / No question over the window. |

A window can open another one on top (for example, from a `Button`); Esc returns to the window below.
The game allows at most 4 mod windows open at once.

---

## Without the hub, load order

- **Load order does not matter.** If the hub loads before your mod, it accepts `Register()` at once. If it loads
  later, it broadcasts "ready" and the client file registers again by itself.
- **Values work without the hub.** `.Value`, `IsPressed()`, saving and loading all live in the client file.
  If the hub is missing, the player simply has no settings window. `Menu.IsConnected` stays `false`.
- If the hub is older than the client file (an older protocol), it refuses the menu. The client writes a line to the log
  and your mod keeps working with its current values.

---

## Upgrading from Mod Settings

Mod UI is the same Workshop item (ID `3842864536`) under a new name. Old copies of `ModSettings.cs` keep working.
To update a mod:
1. Delete `ModSettings.cs` from your mod and copy [`Client/ModUI.cs`](Client/ModUI.cs) instead.
2. Replace `using ModSettings;` with `using Fd32.ModUI;`.

The class names are the same, and the saved files (`ModSettings_<id>.txt`) keep their names, so players keep their settings.

## Rules and limitations

- **Do not edit `ModUI.cs`.** Copy it as is. To update, replace it with the newer version from this repository.
- **Never change `id` and setting `key`s** between versions: saved values and the hub depend on them.
  Labels can change freely.
- **Keys must be unique within a menu.** If a key is repeated, the hub does not accept the menu and writes a line to its log.
- **C# 6** and the mod whitelist: the client file complies with both. Keep your own code to C# 6 as well, because the game compiles mods itself.
- **Personal settings only.** Values live on each player's machine. Server-wide (admin) settings are not supported.
- **No Ctrl+V** in text fields (the whitelist blocks reading the clipboard). Ctrl+C / Ctrl+X work.
- The mod window limit of the game applies to custom windows (`MyModScreen`). The hub's key prompt is drawn inside its own window, so it does not count.

## Troubleshooting

Look at the game log: `%AppData%\SpaceEngineers\SpaceEngineers_*.log`.

| Log line | Meaning |
|---|---|
| `ModUI| Mod UI V…` | The hub has loaded. |
| `ModUI client: <id>: центр принял меню` | The hub accepted your menu ("hub accepted the menu"). |
| `ModUI client: <id>: центр Mod UI устарел…` | The hub is older than your client file ("hub is outdated"). |
| `ModUI client: <caption>: центра Mod UI нет — окна ввода не будет` | A Set action was placed, but the hub is missing ("no hub, no input window"). |
| `ModUI| окно ввода: …`, `ModUI| … длиннее 9 символов — обрезана` | The hub refused an input window, or a label template gives more than 9 characters ("cut"). |
| `ModUI client: <caption>: центр Mod UI старый (нужен V0.10)` | `w.Open()` of your own window, but the hub is older than V0.10 ("hub is old"). |
| `ModUI| картинка окна …: материала "…" нет` | An `Image` names a material the game does not know ("no such material"). |
| `ModUI client: <id>: ошибка в …` | Your event handler threw an exception (full stack trace follows). |
| `MOD_ERROR` / `is prohibited` | The game refused to compile a mod (whitelist). |

- **No gear in the Esc menu:** the hub is not loaded. Check the dependency or add it to the world.
- **The gear is there but your mod is not listed:** `Register()` was not called, or the menu has repeated keys.
- **Hotkey does nothing:** chat or a window is open (see `EvenInMenus`), the modifiers differ (`R` ≠ `Ctrl + R`),
  or the key is unbound (`IsNone`).

---

## Repository layout

| Path | Contents |
|---|---|
| `Client/ModUI.cs` | **The client file** that mod authors copy. The reference copy: the game does not compile it from here. |
| `Data/Scripts/ModUI/` | The hub: the gear in the Esc menu (`HubSession.cs`, `Menu.cs`), the window (`SettingsWindow.cs`), the toolbar input window (`Forms.cs`), mod windows (`Windows.cs`), the GUI helper (`Gx.cs`), hub strings (`Loc.cs`). |
| `Data/TransparentMaterials_ModUI.sbc` | Materials over vanilla GUI textures. |
| `ModUI.csproj` | Compile check only: `dotnet build ModUI.csproj` (set `SEBin64` to your `SpaceEngineers\Bin64`). It does not check the whitelist; only the game does. |
| `TESTS.md` | In-game test plans (in Russian). |

## License

[MIT](LICENSE). You may copy `ModUI.cs` into any mod, including commercial or closed ones.
