# Public Hyperscale

The headset version of "Hyperscale in the Public Interest," a geospatial talk built on Cesium. A Unity 6 project using Cesium for Unity and OpenXR, built as an APK for Meta Quest 3 (and runnable on PC VR through Quest Link).

## Build

1. Clone this repository into a Unity 6 project made from the VR template (see below for the first time).
2. Open it in Unity. Window > TextMeshPro > Import TMP Essential Resources (once).
3. Public Hyperscale > Set the Cesium ion token.
4. Public Hyperscale > Create the scene, then Configure for Meta Quest, then Check the setup.
5. Headset in developer mode on USB: Public Hyperscale > Build and run on the headset. The APK is written to `Builds/`; sideload it elsewhere with `adb install -r`.

## First time on a new machine

Create a Unity 6 project from the VR template, close Unity, then in that folder:

```
git init
git remote add origin https://github.com/powersimple/hyperscale_vr.git
git fetch origin
git checkout -f -B main origin/main
```

Before the first open, add Cesium for Unity to the project's packages:

```
powershell -ExecutionPolicy Bypass -File setup_cesium.ps1
```

Then open the project in Unity. Once it opens cleanly, commit `Packages/` and `ProjectSettings/` so every later clone matches.

## Data

`Assets/StreamingAssets/AtlasPackage` is generated from the web deck by an export script and replaced on each export. Data credits: PeeringDB (data centers), Epoch AI (AI compute), TeleGeography Submarine Cable Map (CC BY-NC-SA 3.0); imagery and terrain from Cesium ion, Bing Maps, and Google.

The MIT license covers the code. The data package keeps its sources' terms (TeleGeography's data is CC BY-NC-SA 3.0, non-commercial).

## Controls (Quest)

| Control | Action |
|---|---|
| Right stick | Fly forward, back, slide left and right (off in orbit) |
| Right stick click | Take a photo (saved to the app's Photos folder: Android/data/<app id>/files/Photos) |
| Left stick | Forward zooms in, back zooms out; left and right turn, or spin the globe from orbit (about 15 s a turn) |
| Right trigger | Laser (tipped up so a relaxed arm aims ahead): point to label, pull to select; pull on empty space to clear |
| A | Display on or off; from orbit, with the display off, the controls guide shows |
| B | Fly to the selection, or to wherever the laser points |
| Y / X | Next / previous slide |
| Menu (flat button, left) | The intro: the whole Earth, centered, the equator level with your eyes, display off; title over the pole and the controls guide beside the Earth. The app starts here; Y plays the current slide |
| Right grip / left grip (hold) | Turbo 4x / precision 1/4 |
| Left stick click | Recenter the display (and turn it on) |
| Meta button | The app holds still while the Quest menu is open |

## Heads-up display

- Look up: the deck title and byline, the slide's title and subtitle.
- Left: the story text and its Explore links, with the stat boxes beneath.
- Upper right: filters, a checkbox list with full labels; each slide resets them to what it shows.
- Lower right: data on the selected marker (hidden when nothing is selected).
- Bottom of the view: Back, the progress bar and slider, Next.
- Look down: what the laser points at; then one box with the filter state, the location line (region, country, coordinates, heading, altitude) with the compass, the stories slider and the slides slider, previous and next arrows at its sides; then the sources, the credits, and the silver Academy logo flat on the floor. The 3D emblem sits at the lower right.
- From orbit, the deck title and subtitle (Raleway, in Assets/AtlasVR/Resources/Fonts) float above the North Pole.
- Data points are small gem spheres (sapphire data centers, ruby AI compute, topaz under construction, aquamarine cable landings); named sites keep the story icons. The filter list is the legend.
- Upper right, above the filters: the state or province and country under you, with latitude and longitude.
- Lower right, flying low and moving: a small 3D compass; the needle points the way you face, N stays on north.
- Nothing sits in the straight-ahead view. The HUD turns with you only once your head turns past 80 degrees.
- The laser is tipped up 32 degrees from the controller (`laserPitchUp`), so a relaxed arm aims straight ahead.

On the globe, named data centers and power plants use the story's icons (Tabler glyphs, rendered from the web deck into `Assets/AtlasVR/Resources/AtlasIcons.png`), and the deck's power and data connections draw as glowing arcs with pulses (`layers/flows.json`, written by `_vr/export/flows.mjs`).

Country and state borders come from Natural Earth's 1:50m boundary lines (public domain, no coastlines), written by `_vr/export/borders.mjs` in the atlas app; countries show below about 9,000 km, states below about 2,500 km. Day and night are two terrain tilesets kept loaded side by side, so a night slide never re-images the globe. Night needs Cesium ion's Earth at Night (asset 3812) in your ion account (Asset Depot, Add to my assets); without it the app stays on the day Earth and says so. The emblem mesh is baked from the Academy's glTF model by `_vr/export/bake_emblem.py` into `Assets/AtlasVR/Resources/AcademyEmblem.bytes`, so the project needs no glTF importer. Keyboard in the editor: WASD fly, Q/E zoom, J/L turn, H HUD, N north, arrows next/previous, T fly to.
