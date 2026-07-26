---

# ![ABB icon](icon.png) Aether Blackbox  

See what actually caused your wipe.

Aether Blackbox records your pulls and lets you replay them in a 2D timeline. You can scrub through positioning, damage, and mechanics, or sync it with your own footage at [https://blackbox.aetherdraw.me](https://blackbox.aetherdraw.me) to see what happened from your POV.

---

<br>

<img width="1007" height="642" alt="abb main window" src="https://github.com/user-attachments/assets/fab0596c-f07f-4844-bfb7-d10eb01e8bc1" />

<br>
 <img src="https://github.com/rail2025/AetherBlackbox/blob/main/abbaoeloaderwip.gif" />


---

## How it works

1. Do a pull like normal (it records automatically)
2. Open the replay and scrub back through the fight
3. Check positioning, damage, buffs, etc leading up to the wipe

If you want more detail or want to share things with your console playing friends, you can export it to the web app.

---

## What you can do

### Replay fights

* Scrub through a timeline of the encounter
* See where everyone was standing at any moment
* Track damage taken, healing, buffs, and barriers

---

### Sync with your footage

![VOD sync](docs/vod-sync.gif) <img src="https://github.com/rail2025/AetherBlackbox/blob/main/blackboxvodteaser.gif" />

* Link your OBS recordings to the replay
* Watch your POV alongside the 2D timeline

---

### Draw on the fight

<img width="1007" alt="abb demo" src="https://github.com/user-attachments/assets/e2679e01-9274-454a-b884-0230e857395d" />

* Add annotations directly on the arena
* Draw markers that stick to players or bosses over time

---

### Save and reuse stuff

* Turn mechanics into simple slides
* Build a plan with pixel perfect in-game positions and drawings and export directly into AetherDraw

---

## Plugin vs web differences

### In-game plugin

* Records encounters automatically
* Lets you immediately review a wipe
* Basic replay and drawing tools

### Web app

* adds VOD sync
* Easier sharing for console
* Requires just 1 person in live sync mode to have the plugin running (so you can have 7 console players with the page open on a phone or tablet or whatever)
---

## Getting started

* Walkthrough/Website version: [https://blackbox.aetherdraw.me](https://blackbox.aetherdraw.me)
* Download / install: Installed through the main Dalamud plugin UI. search for AetherBlackbox and install

---

## Notes

* Works best if you already record your gameplay (for VOD sync) Use [OBStoABB](https://github.com/rail2025/PulltoOBS) for vod file naming automation and stitching
* You can still use the replay without any video

---



<br>
<img width="1007" alt="abb demo" src="https://github.com/user-attachments/assets/fd3aae03-a96e-4ca6-b2d7-cef4e8121a25" />
<br>

<br>

---

## Architecture

AetherBlackbox is designed as a high-performance combat replay and visualization system with real-time multiplayer synchronization.

design details: **[architecture.md](./architecture.md)**

### Highlights

* **Zero-touch logging:** Automatically records encounters
* **Replay system:** Timeline scrubbing with historical state reconstruction
* **Real-time collaboration:** WebSocket-based telestration (binary + JSON protocols)
* **Vector canvas:** Lossless zoom/pan with minimal network overhead
* **Performance-focused:** Aggressive caching and controlled concurrency

---

## Contributing

Feature requests, bug fixes are welcome. For those that wish to add to this project, please see the above section.

For the project map, (what files to edit to do a specific thing see this document:
Project map: **[project_map.md](./project_map.md)**

---

## Credits & Attribution

This project is a combined and modified work based on the following AGPL-licensed projects:

* **Combat Log Parser**
  https://github.com/Kouzukii/ffxiv-deathrecap
  Used for parsing Dalamud combat logs and extracting the 30-second pre-death event timeline.

* **Drawing App**
  https://github.com/rail2025/AetherDraw
  Used as the basis for the ImGui-based interactive replay and visualization UI.

This repository contains significant modifications and additional functionality, including replacing tabular output with a scrubbable replay window rendered via ImGui.

---

## Installation

Install through Dalamud.
