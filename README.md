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
| Right stick | Fly forward, back, slide left and right |
| Left stick | Forward zooms in, back zooms out; left and right turn |
| Right trigger | Laser: select a marker (its data and sources show); pull on empty space to clear; press HUD buttons |
| A | Show or hide the HUD (selection and filters are kept) |
| B | Fly in to the selected marker, or to wherever the laser points |
| X / Y | Previous / next slide |
| Right grip (hold) | Turbo, 4x speed |
| Left grip (hold) | Precision, 1/4 speed |
| Left stick click or Menu | Recenter the HUD in front of you |

The HUD rings the edges of the view: filters with sub-menus across the top, the story (headline, subtitle, text, Explore links) on the left, stats and pointer info on the right, sources and story navigation (Back, progress, Next, story list) along the bottom. Below about 350 km, labels pop up over the places ahead of you. Keyboard in the editor: WASD fly, Q/E zoom, J/L turn, H HUD, arrows next/previous, T fly to.
