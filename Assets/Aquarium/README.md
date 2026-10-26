# Three-Screen Aquarium (Unity 6.6 / URP 17.6)

Open `Assets/Aquarium/Scenes/Aquarium.unity` and press Play. The `Aquarium` GameObject holds the
`AquariumDirector` (all settings are in its Inspector) and the three screen cameras.
To rebuild the scene from scratch: **Aquarium > Create Aquarium Scene**.

## Displays (4 outputs)
| Display | Shows |
|---|---|
| **Display 1**: operator monitor | **Control panel** (live previews, display routing, actions, live settings) |
| **Display 2** | Left wall (`Left Wall Camera (Display 2)`) |
| **Display 3** | Right wall (`Right Wall Camera (Display 3)`) |
| **Display 4** | Floor (`Floor Camera (Display 4)`) |

**Windows setup:** in *Settings > System > Display*, make the operator monitor the **main display**
("Make this my main display") and arrange the three projectors/screens. Unity's Display 1 is always the main
display; Displays 2-4 follow Windows' order. If a screen shows the wrong view, fix it live in the control panel
(◀ ▶ next to each screen, or **Swap left / right**). **Identify displays** shows each display's number on it
for 5 s. The choice is saved and used on every start.

**Control panel (Display 1):**
- Screens → displays: route each screen to a display at runtime; Identify; Swap left/right.
- Actions: **Feed all screens**, **Startle all fish**, **Restart aquarium**, **Quit** (with confirmation).
- Live status: fish visible / curious per screen, active touches, last touched screen, food flakes.
- Live previews of all three screens (toggle off to save GPU time).
- Look (live): sunlight, water haze, god rays, touch water effect, ripple strength.
- Interaction (live): scare radius, viewer eye height, mouse hover on/off, feeding on/off.
- Fish (on restart): size multiplier and which species are enabled.
- Settings save automatically to `%USERPROFILE%\AppData\LocalLow\<Company>\<Product>\AquariumSettings.json`;
  **Reset to scene defaults** deletes that file. **F1** hides/shows the panel.
- The mouse on the operator monitor never disturbs the fish (Display 1 has no aquarium camera).
- Turn the panel off with `Display > Control Panel` on the director.

- Each camera renders independently to its own display. To re-route a screen, change that camera's
  **Target Display** in its Inspector. **Aquarium > Setup Three Display Cameras** recreates them (Displays 2-4).
- A player build activates Displays 2 and 3 automatically (`Display.Activate`). Connect 3 monitors or projectors.
- In the Editor: **Aquarium > Open Game Views For Displays 1-4**, dock the four Game views, then press Play.
- **F2** switches to a single-window preview (left | floor | right side by side). **F1** shows a stats overlay:
  fish visible per display and the last touched display.
- All three cameras share one eye point and are posed together every frame in `DisplayRig.LateUpdate`
  (off-axis projection), so the displays form one continuous tunnel with seamless corners.
- Set `Tunnel > Width / Wall Height / Length` to the real screen sizes (metres) and use display resolutions
  with the same aspect ratios. The top edge of the floor image points to +Z.

## Touch and mouse
- **Multi-touch on every touchscreen**: all connected touchscreens (one per display) and pens are read,
  with every finger tracked separately.
- **Mouse hover**: moving the mouse over a screen is interactive with no click needed (lighter wake, fish
  move aside). Clicking gives the full splash and scare. A parked cursor stops interacting after
  `Interaction > Hover Idle Time`. Turn it off with `Interaction > Mouse Hover`.
- **Plants**: every kelp, seagrass and anemone blade bends away from nearby touches with a springy ease-in
  and ease-out, and sways with the tank current at its own root (`Interaction > Plant Push / Radius`).
- Each touch or click is mapped to the display it happened on: the Input System `displayIndex` in a player,
  or the Game view under the cursor in the Editor.
- Only fish visible on that display react. Left wall touches scare left wall fish only, and likewise for the
  right wall and the floor. School members pass the escape along only to fish on the same display.
- The escape response:
  - fish react after a short delay that grows with distance, so the escape spreads out from the finger
  - a fast turn with a curled body and wider tail beats (C-start)
  - a burst at full speed, then a gradual glide back to cruising
  - holding or dragging a finger keeps the fish fleeing, and they turn smoothly toward the new way out
- Tune these under `Interaction`: flee radius, reaction wave speed, jitter, escape duration.

## Fish size and viewing
- `Aquarium > Fish Size Multiplier` (default 2) scales every species. Spacing and speed scale with it, so bigger
  fish still school naturally.
- Each species has **Glass Affinity / Preferred Glass Distance**, so fish cruise close to the wall glass where
  visitors see them up close.

## Using Asset Store (or any) fish models
1. Import a fish pack (URP-compatible packs are easiest; Built-in "Standard" materials are converted to URP Lit automatically).
2. Select the `Aquarium` object, open `Species`, pick a species (or add a new one) and drag the fish prefab
   into **Custom Model**.
3. Set **Model Forward** to the axis the model's head points along (use **Model Rotation Offset** for
   anything else). The model is scaled to the species `Length` automatically.
4. Rigged or animated models keep their own swim animation, and it plays faster when the fish swims faster.
   Set **Model Animation State** if the Animator needs a specific state such as "Swim". Static meshes get the
   aquarium swim wave, bending and escape animation, using the model's texture.

Everything else (schooling, escapes, curiosity, feeding, per-display input) works the same for imported fish.

**Several looks per species:** fill **Model Variants** instead of Custom Model. Each variant has its own
model and forward axis, and each school uses one variant, so similar-looking fish swim together.

### Sea fish – underwater world (Assets/fish), currently active
Run **Aquarium > Add Sea Fish Pack (underwater world)**. It:
- converts the pack's 54 Built-in "Standard" materials to URP Lit (fins stay translucent),
- makes the swim and idle clips loop and creates a **swim / idle / eat** Animator Controller per fish
  (in `Assets/Aquarium/Generated/SeaFish`), assigned to the pack's prefabs,
- adds 19 rigged fish as 6 groups (135 fish):

| Group | Pack fish | Behaviour |
|---|---|---|
| Reef Angelfish | french angelfish, emperor angelfish, moorish idol, sweetlips, clown triggerfish | slow groups of 3, love the glass |
| Reef Tangs | regal tang, yellow tang, purple tang | schools of 10 cruising along the tunnel |
| Butterflyfish | collare & copperband butterflyfish, longnose, coral beauty, flame angel, firemouth, goldfish | curious pairs and small groups |
| Anthias & Grammas | lyretail anthias, royal gramma | bright schools low over the reef (seen through the floor) |
| Mandarin Fish | mandarin fish | hover slowly just above the sand |
| Perch | perch | tight school in open water |

- places the pack's rock **column** as reef pillars (6 behind the walls, 2 under the floor; `Environment Props` on the director),
- disables the cartoon and procedural reef species (sardines, mantas and sharks stay).

Imported rigged fish play **eat** when they snap up a food flake, **idle** while hovering at a finger, and
**swim** otherwise (played faster when they swim faster).

### Cartoon Fish Pack 2
With the aquarium scene open, run **Aquarium > Add Cartoon Fish Pack 2**. It adds all 28 fish as four
species and disables the four procedural reef species they replace (re-enable them in `Species` if you want both):

| Species | Pack models | Behaviour |
|---|---|---|
| Cartoon Reef Fish | 1–7, 10, 12, 14 | small schools of 6, very curious, stay near the glass |
| Cartoon Angelfish | 13, 16, 17, 21, 22, 24, 26–28 | slow, graceful groups of 3, love coming to the glass |
| Cartoon Racer | 8, 9, 11, 15, 18–20, 25 | fast schools of 6 cruising along the tunnel |
| Cartoon Squid | 23 | jet-like kick-and-glide pulses |

The models are static meshes, so they use the aquarium swim shader (body wave, turn bending, escape
tail beats) with the pack's texture. Forward axes are already set per model (7 face +X, the rest −X).

## Interacting with the fish
- **Tap** or **fast swipe**: fish near the finger dart away (on that display only).
- **Hold still** (about 0.5 s): curious fish notice, swim over and circle the finger.
- **Move slowly**: they follow the finger along the glass.
- **Keep holding** (about 1.2 s): food flakes drift down and the fish chase and eat them.
- The mouse does the same just by hovering (lighter); click for the full effect.
- Tune it under `Interaction > Curious fish and feeding` and per species with **Curiosity**
  (sharks barely care, angelfish and tangs love it).

## Fish look and motion
- Smooth, higher-resolution bodies (snout, gill bulge, lateral line, gill line) and **translucent fins** with
  fin rays that ripple with the body wave. Pectoral fins row. Eyes are glossy.
- Motion: smoothed steering, per-species turn-rate limits, gentle pitch and banking, and
  **burst-and-coast** swimming for reef fish (tail kicks, then glide). Tune these per species under `Natural motion`.

## Sunlight
- The fog scatters sunlight: water glows when looking toward the sun and brightens toward the surface
  (`Water > Sun Scatter / Surface Glow`). There are 30 soft, swaying god rays (`God Ray Intensity / Count`),
  stronger caustics, and a brighter Snell's window on the surface.

## Water layer
- Every display has two GPU simulations:
  - a wave simulation (`Ripple.compute`: rings and wakes)
  - a **stable-fluids liquid** simulation (`ScreenFluid.compute`: velocity + dye), which gives smooth,
    swirling liquid that follows the finger and curls into vortices (`Water Layer > Liquid ...`)
- A finger presses into the surface, drags leave a continuous wake, and holding still sends pulses.
- Normals use a smooth wide-kernel filter, so the simulation grid never shows as blocks or checkers.
- `Glass.shader` draws it on the screen plane as a **semi-transparent** layer: refraction of the aquarium
  behind it (with slight chromatic dispersion), light focusing, glints and soft foam, capped at
  `Water Layer > Max Opacity` (default 0.6). Corals, rocks and fish always show through, and calm water is fully transparent.
- The URP assets' opaque texture is set to full resolution, so the aquarium seen through the water stays sharp.

## Systems
| Script | What it does |
|---|---|
| `FishSchool` | Multi-species boids (schooling, migration, habitat, glass/rock avoidance, predators, per-display escapes, currents) |
| `FishSpecies` / `FishMeshBuilder` | 7 procedural species; edit or add species in the Inspector |
| `FluidSimulation` + `FluidSim.compute` | Tank-wide GPU current field: touches push water; kelp, fish and particles follow it |
| `RippleSurface` + `Ripple.compute` + `Glass.shader` | Per-display touch-reactive water layer |
| `DisplayRig` / `ProjectionScreen` | Screen cameras, display routing, synced off-axis projection, pointer-to-display mapping |
| `UnderwaterLighting` | Sun with soft shadows, caustics, fog, floodlights, bioluminescence, post-processing |
| `AquariumEffects` | Bubble vents, marine snow, touch bubbles, sand clouds |
| `AquariumInput` | Multi-touch + mouse per display. `InjectTouch(screen, uv, id)` accepts external sensors |

## Debug tools (Play mode)
- **Aquarium > Capture Three-Screen Screenshot** saves a stitched PNG to `Screenshots/`.
- **Aquarium > Debug > Simulate Tap On ...** then **Report Tap Reaction**: how many fish fled, and escapes
  counted per touched display (confirms the other displays didn't react).
- **Aquarium > Debug > Simulate Drag Across Floor**.

## Notes
- Place the Director at the world origin (the environment is built in world space).
- Changing defaults in code doesn't update an existing scene: re-add the component or edit the Inspector.
- Windows touchscreens: map each touchscreen to its monitor (Control Panel > Tablet PC Settings > Setup),
  otherwise Windows reports every touch on Display 1.
- The tank-wide current is shared water, so a strong push can drift nearby fish slightly even across a corner.
  Escapes (the actual reaction) are strictly per display.
