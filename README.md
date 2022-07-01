# Neon White TAS Kit

## Installation

This mod uses [MelonLoader](https://github.com/LavaGang/MelonLoader) as its modloader. Install that on your Neon White install first, and then run the game once so it generates some folders, and to verify it's installed (you should see a MelonLoader splash screen).

After that, grab the latest .dll from the [Releases page](https://github.com/PandorasFox/Neon-TAS/releases) and drop it in the "Mods" folder in your Neon White folder, e.g. `SteamLibrary\steamapps\common\Neon White\Mods`.

## Configuration

Configuration for this mod is provided by the **Mono Varient** of [Melon Preferences Manager](https://github.com/sinai-dev/MelonPreferencesManager/releases/). Its default in-game bind is F5.

The IL2CPP version of Melon Preferences Manager will **NOT** work. You **must** use the Mono variant.

## Usage

[Quick 'n' Shitty Demo Vid](https://www.youtube.com/watch?v=HbLU1PFZU2Q)

The mod needs to be enabled within the Melon Preferences (default keybind `F5`) category for it. Replaying and recording can both be turned on independelty; recording inputs only happens when there are no active replays happening.

Upon level completion or death (not restart, tho I should hook that eventually), the current replay is saved to disk, in the default 'ghost' replay folder (`%APPDATA%\..\LocalLow\Little Flag Software, LLC\Neon White\{steam user id}\Ghosts\`, then stored in the folder for that level name). There should be `0.phant` files in those folders - those are your PB ghosts! The `[input count]_[timestamp].TAS` files are the input files.

When replays are enabled, you need to provide a filename for the TAS kit to automatically load from the appropriate level folder at the start of the level. This is handled by the mod config; by default it is 'active.TAS'.

There is optionally a config to copy the timestamped filename to your clipboard upon level completion. This lets you easily paste in the filename of the last completed run into the TAS kit for easy replaying and double checking :)

You can edit the .TAS files in any text editor. Their format is as follows:

```
[frame]>[X movement],[Y movement],[facing direction],facing angle]|JFDSRP
```

These components are as follows:

* frame: 5-digit (optionally padded to 5 digits by the recorder), followed by a `>` to delineate between it and the inputs for that frame.
* X movement, Y movement, facing direction, facing angle
  * these are all floats and can be omitted from a frame.
  * these inputs all 'stick' if omitted (as Neon White has a fair amount of holding W, and you tend to only occasionally adjust camera angles)
  * facing direction is the direction your camera is facing on a 2-d plane (e.g. 0 = north, 90 = east, 180 = south, 270 = west) and angle is the up/down angle between -90 and +90.
* The 'actions' on the right side of the `|` separator.
  * J means a Jump input on that frame
  * F is Fire
  * D is Discard
  * S is Swap Cards
  * R is Restart (can technically fake this input, but idk why you'd want to. Isn't implemented to actually do anything yet as menus are not TASed.)
  * P will disable the TAS playback and pause the game, and will resume recording upon being resumed.
    * **You can add a `P` action on a specific frame in an input file to effectively re-record from that point on**. Upon finishing the level, a new input file will be saved and copied to your clipboard.

## Caveats and Notes

Inputs are replayed by hooking on Unity's fixed-rate `fixedUpdate` routine that ticks every .0167 of game-time and updating what input polls will receive until the next fixedUpdate tick. However, a fair amount of movement/velocity/input processing happens in `.Update` methods that tick with every frame, rather than at a fixed rate.

What this means is that inputs should be updated at a constant-interval 60hz rate, but input polling _might not_ fall evenly on those if your framerate varies too much.

Inputs are recorded by reading input states on every fixedUpdate tick, so recording may miss inputs if you input them for 

It is recommended to use specific frame limits when using these tools. I haven't done much testing on how much this matters, but if the VOD of the TAS will be at 60fps anyways, may as well do all the TASing at 60fps :)

Desyncs can still occasionally happen even with frame limiting etc. I've done some work to try and prevent this from happening; you may need to re-record to try and get a more consistent string of inputs.

### Things that should probably be added

* Better partial replay support (e.g. config for saving upon restart, config menu button / keybind? for saving the current input stream so far).
* "If a tas ends just hand back control and record" would probably be good instead of just sticking the last input
* Maybe a more robust tick input system, but I really do not want to mess with the string i/o unless SUPER necessary.