## Table of Contents
1. [Plugin Initialization & Globals](#1-plugin-initialization--globals)
2. [User Interface & Windows](#2-user-interface--windows)
3. [Presentation & Canvas Engine](#3-presentation--canvas-engine)
4. [Game Data & Capture Hooks](#4-game-data--capture-hooks)
5. [Archival & File Management](#5-archival--file-management)
6. [Mechanics & Presets](#6-mechanics--presets)
7. [Collaboration & Networking](#7-collaboration--networking)

---

## 1. Plugin Initialization & Globals

| Responsibility | Files |
| :--- | :--- |
| **Plugin Lifecycle / Bootstrapping** | `Plugin.cs` |
| **Global Service Locator** | `Service.cs` |
| **User Settings / Config** | `Configuration.cs` |
| **Combat Condition Checks** | `ConditionEvaluator.cs` |

## 2. User Interface & Windows

| Responsibility | Files |
| :--- | :--- |
| **Main Application Window** | `Windows/MainWindow.cs` |
| **Timeline Scrubber** | `Windows/MainWindowTimeline.cs` |
| **Combat Log** | `Windows/MainWindowInteractiveLog.cs` |
| **Party / Alliance Overlays** | `Windows/PartyOverlay.cs`, `Windows/AllianceOverlay.cs` |
| **Drawing Tool Selection** | `Windows/ToolbarWindow.cs` |
| **Shape Properties Editing** | `Windows/PropertiesWindow.cs`, `Windows/Properties/*` |
| **Popups / Chat Notifications** | `UI/NotificationHandler.cs` |
| **Settings UI** | `UI/RecapConfigWindow.cs`, `Windows/CanvasConfigWindow.cs` |

## 3. Presentation & Canvas Engine

| Responsibility | Files |
| :--- | :--- |
| **Canvas Rendering Pipeline** | `DrawingLogic/ReplayRenderer.cs`, `Windows/MainWindowCanvas.cs` |
| **Shape Coordinate Translation** | `DrawingLogic/CanvasProjector.cs` |
| **Texture & Image Loading** | `DrawingLogic/TextureManager.cs` |
| **User Interaction (Drag/Rotate/Resize)** | `DrawingLogic/ShapeInteractionHandler.cs`, `DrawingLogic/InteractionHandlerHelpers.cs` |
| **Drawing Object State (Undo/Redo/Pages)** | `Core/CanvasController.cs`, `Core/PageManager.cs`, `Core/UndoManager.cs` |
| **Hit Detection Math** | `DrawingLogic/HitDetection.cs` |
| **Adding New Shapes / Tool Registry** | `DrawingLogic/BaseDrawable.cs`, `DrawingLogic/DrawMode.cs`, `Core/ToolRegistry.cs` |

## 4. Game Data & Capture Hooks

| Responsibility | Files |
| :--- | :--- |
| **Packet Interception (ActionEffect/ActorControl)** | `Events/CombatEventCapture.cs` |
| **Continuous Position / State Sampling** | `Core/PositionRecorder.cs` |
| **Event Data Models** | `Events/CombatEvent.cs`, `Events/Death.cs` |
| **Actor/Job Tracking** | `Events/MetadataRecorder.cs` |
| **Game Memory Structs** | `Game/*` |

## 5. Archival & File Management

| Responsibility | Files |
| :--- | :--- |
| **Encounter Lifecycle (Start/End Session)** | `Core/PullManager.cs` |
| **Replay Data Structures** | `Core/ReplayData.cs`, `Core/PullSession.cs` |
| **Disk Save/Load & Compression** | `Core/ReplayFileManager.cs` |
| **Vector Shape Serialization** | `Serialization/DrawableSerializer.cs` |
| **Plan Exporting** | `Core/PlanExportManager.cs` |

## 6. Mechanics & Presets

| Responsibility | Files |
| :--- | :--- |
| **Automated AoE Processing** | `DrawingLogic/AoeAutomater.cs`, `DrawingLogic/AoeBridge.cs` |
| **Arena Backgrounds / Phases** | `Core/Mechanics/ArenaDatabase.cs` |
| **Hardcoded Encounter Triggers** | `Core/Mechanics/UMAD.cs`, `Core/Mechanics/MechanicsRegistry.cs` |
| **Preset Storage / Custom Mechanics** | `Core/Mechanics/PresetManager.cs`, `Core/Mechanics/PresetStorageService.cs` |
| **JSON Mechanics Files** | `Core/Mechanics/Presets/*` |

## 7. Collaboration & Networking

| Responsibility | Files |
| :--- | :--- |
| **WebSocket Connection / Broadcasting** | `Networking/NetworkManager.cs` |
| **Live Session Join UI** | `Windows/LiveSessionWindow.cs` |
| **Network Messages** | `Networking/NetworkPayload.cs` |
| **Message Encoding** | `Serialization/PayloadSerializer.cs` |
