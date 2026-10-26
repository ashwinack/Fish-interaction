# Fish Interaction: Immersive Three-Screen Aquarium

An interactive aquarium tunnel for Unity 6 (URP): a left wall, a right wall and a floor screen, each on its own
physical display, form one continuous underwater tunnel. An operator monitor shows a control panel.
Fish school, flee from touches, come over to look at a resting finger and eat food that drifts down.

- Multi-species boids on the GPU, with per-display touch reactions
- Multi-touch and mouse hover on every screen
- GPU stable-fluids and wave simulation drawn as a semi-transparent water layer on the glass
- Sunlight through the surface: caustics, god rays, soft shadows, light scattering in the fog
- Plants that bend away from touches
- Control panel on Display 1: live previews, display routing, feeding and startle actions, live settings

## Requirements
- Unity **6000.6** (Unity 6.6) with URP 17.6 and the Input System package
- 4 displays for the full setup (1 operator monitor and 3 screens). It also runs in the Editor with Game views.

## Getting started
1. Clone the repo and open the folder in Unity Hub.
2. Open `Assets/Aquarium/Scenes/Aquarium.unity` and press Play.

The aquarium works without any extra assets: species whose pack model is missing use the built-in procedural
fish, and the pack's rock columns are skipped.

### Optional fish packs
The project was set up with two third-party packs. They are **not included** in this repo because of their
licenses. To use them, import them into `Assets/` yourself:
- **Sea fish – underwater world**, imported to `Assets/fish`, then run **Aquarium > Add Sea Fish Pack (underwater world)**
- **Cartoon Fish Pack 2**, then run **Aquarium > Add Cartoon Fish Pack 2**

Full documentation: [Assets/Aquarium/README.md](Assets/Aquarium/README.md)
