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
| Right stick | Fly forward, back, slide left and right (off in orbit, where the Earth stays north-up) |
| Left stick | Forward zooms in, back zooms out; left and right turn (turning is off in orbit) |
| Right trigger | Laser: select a marker (its data and sources show); pull on empty space to clear; press HUD buttons |
| A | Show or hide the HUD (selection and filters are kept) |
| B | Fly in to the selected marker, or to wherever the laser points |
| Y | Turn to face north |
| X | Previous slide (Next is on the HUD) |
| Right grip (hold) | Turbo, 4x speed |
| Left grip (hold) | Precision, 1/4 speed |
| Left stick click or Menu | Recenter the HUD in front of you |

## Heads-up display

- Look up: the deck title and byline, the slide's title and subtitle.
- Top of the view: story navigation (the Stories menu, where you are, altitude).
- Left: the story text and its Explore links, with the stat boxes beneath.
- Upper right: filters, a checkbox list with full labels; each slide resets them to what it shows.
- Lower right: data on the selected marker (hidden when nothing is selected).
- Bottom of the view: Back, the progress bar and slider, Next.
- Look down: the sources, then the imagery credits; the Academy wordmark (lower left) and 3D emblem (lower right).
- Upper right, above the filters: the state or province and country under you, with latitude and longitude.
- Lower right, flying low and moving: a small 3D compass; the needle points the way you face, N stays on north.
- The HUD turns with you only once your head turns past 60 degrees.

On the globe, named data centers and power plants use the story's icons (Tabler glyphs, rendered from the web deck into `Assets/AtlasVR/Resources/AtlasIcons.png`), and the deck's power and data connections draw as glowing arcs with pulses (`layers/flows.json`, written by `_vr/export/flows.mjs`).

Country and state borders come from Natural Earth (public domain), written by `_vr/export/borders.mjs` in the atlas app. The emblem mesh is baked from the Academy's glTF model by `_vr/export/bake_emblem.py` into `Assets/AtlasVR/Resources/AcademyEmblem.bytes`, so the project needs no glTF importer. Keyboard in the editor: WASD fly, Q/E zoom, J/L turn, H HUD, N north, arrows next/previous, T fly to.
