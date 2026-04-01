# Mjolnir

A Schedule I mod that adds Mjolnir (Thor's Hammer) as a purchasable weapon with melee combat, throwable mechanics, lightning powers, and flight.

## Features

- **Melee Combat** — Left-click to swing. Hits NPCs with blunt impact and triggers a lightning strike on contact.
- **Wind-Up & Throw** — Hold right-click to spin the hammer. Release when fully charged to throw it. The hammer flies forward, hits the first target, then returns to you.
- **Flight** — While the hammer is fully charged (hold right-click until electrified), press and hold Space to fly in the direction you're looking. Release Space to stop.
- **Lightning Zap** — Press X (configurable) to shoot lightning from the hammer toward whatever you're aiming at. Damages and electrifies NPCs. Works on any surface.
- **NPC Panic** — Lightning strikes cause nearby NPCs to panic and flee (configurable radius).
- **Electrified Effect** — When the hammer is fully charged during wind-up, the player gets an electrified visual effect.
- **Stamina System** — All hammer actions consume stamina (melee, lightning, wind-up, flight). Fully configurable per-action costs with a toggle to disable.
- **Live Configuration** — All settings update in real-time via ModsApp or any config editor — no restart required.

## Controls

| Action | Input |
|--------|-------|
| Melee swing | Left-click |
| Wind-up spin | Hold right-click |
| Throw | Release right-click (when charged) |
| Fly | Space (while charged, hold right-click) |
| Lightning zap | X (configurable) |

## Installation

### IL2CPP vs. Mono

Schedule I runs on two different Unity backends. Most players are on **IL2CPP** (the Steam default). Mods built for one backend are incompatible with the other and will crash MelonLoader if loaded together. This mod ships both `ThorHammer.Il2Cpp.dll` and `ThorHammer.Mono.dll`.

Install [OTC Loader](https://www.nexusmods.com/schedule1/mods/1698) (optional, recommended) to automatically detect your game branch and load the correct DLL. It works for all mods, not just OverTheCounter. If installing manually without the loader, only copy the DLL that matches your game branch — see instructions below.

### Requirements

- [MelonLoader 0.7.2+](https://github.com/LavaGang/MelonLoader)
- [S1API Forked 3.0.0+](https://thunderstore.io/c/schedule-i/p/ifBars/S1API_Forked/) ([NexusMods](https://www.nexusmods.com/schedule1/mods/1194))
- [S1MAPI 1.0.0+](https://thunderstore.io/c/schedule-i/p/ifBars/S1MAPI/) ([NexusMods](https://www.nexusmods.com/schedule1/mods/1447))
- [OTC Loader](https://www.nexusmods.com/schedule1/mods/1698) (optional, recommended) — auto-detects your game branch and disables incompatible DLLs

### Using a mod manager (recommended)

1. Open **r2modman**, **Vortex**, or **Gale** and select **Schedule I**.
2. Search for **Mjolnir** in the online tab.
3. Click **Download** — when prompted to install dependencies, click Yes. The mod manager will download and configure everything for you.
4. Launch the game.

### Manual installation

1. Install **MelonLoader 0.7.2+** on your Schedule I game.
2. Install **S1API Forked** and **S1MAPI** into your `Mods` folder. Make sure to pick the versions matching your game branch (IL2CPP or Mono).
3. Download the latest Mjolnir release. It includes two files:
   - `ThorHammer.Il2Cpp.dll` — for the IL2CPP branch (Steam default)
   - `ThorHammer.Mono.dll` — for the Mono branch
4. Copy **only** the DLL that matches your game branch into your `Mods` folder. Do not install both — loading the wrong-branch DLL will crash MelonLoader.
5. (Optional, recommended) Install [OTC Loader](https://www.nexusmods.com/schedule1/mods/1698) — drop `OverTheCounter-Loader.dll` into your `Plugins` folder. It automatically disables wrong-branch DLLs for all mods.
6. Launch the game.

## Configuration

Settings are stored in MelonLoader's config file and organized into the following categories:

* **Controls** — Lightning key binding (any valid [Unity KeyCode](https://docs.unity3d.com/ScriptReference/KeyCode.html) name, e.g. `X`, `F`, `G`, `T`).
* **Shop** — Price (default $10,000) and a toggle to sell at hardware stores (Handy Hank's, Dan's) in addition to the Arms Dealer.
* **Combat** — Melee damage/force, throw damage/force, lightning damage/force, and lightning panic radius (NPCs within this radius flee when lightning strikes).
* **Mechanics** — Flight speed, throw speed, max throw range, and wind-up duration (minimum 0.1s).
* **Stamina** — Enable/disable toggle for stamina consumption, plus per-action costs: swing cost, lightning cost, wind-up drain rate, and flight drain rate.

Every setting includes a full description visible in ModsApp by k0mods [Thunderstore](https://thunderstore.io/c/schedule-i/p/k0Mods/ModsApp/)/[Nexus](https://www.nexusmods.com/schedule1/mods/1222) (recommended, open-source, recently updated). Also compatible with other config editors. You can always edit the config file directly if you prefer.

## In-Game

Mjolnir can be purchased from the **Arms Dealer** for $10,000. Optionally, it can also be sold at **Handy Hank's Hardware** and **Dan's Hardware** (disabled by default — enable in config).

## Credits

- **Author:** hdlmrell
- **3D Hammer Model:** Fannsonetti
