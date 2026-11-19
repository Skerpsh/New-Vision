# Graph Report - Unity-PassthroughCameraApiSamples  (2026-11-19)

## Corpus Check
- 77 files · ~64,811 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 308 file(s) not represented in the graph (top: .meta 230, .prefab 22, .asset 13)
- WARNING: 308 of 428 files seen (72%) have no supported extractor and are missing from this graph entirely. It may look complete while representing only part of the codebase.

## Summary
- 1192 nodes · 2607 edges · 78 communities (43 shown, 35 thin omitted)
- Extraction: 96% EXTRACTED · 4% INFERRED · 0% AMBIGUOUS · INFERRED: 115 edges (avg confidence: 0.85)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `2ee1d83f`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- unityengine
- CompanionController
- RectTransform
- NewVisionWebView
- NewVisionBleLink
- Unity-PassthroughCameraAPISamples
- BrowserPanel
- GeminiClient
- Vector3
- WristPanel
- SentisInferenceUiManager
- ObjectMemory
- SentisInferenceRunManager
- WebViewBridge
- ObjectInfoLookup
- StickClient
- BrowserPointer
- main.cpp
- handleLine
- attributes
- StartMenu
- CodeScanner
- Knock
- LinkPreview.Fetch
- WristPanel.Refresh
- android.webkit.WebView
- HandTracker
- DetectionUiMenuManager
- MonoBehaviour
- NvStyle
- NvBrackets
- ObjectInfoView
- NvGlitch
- Image
- ClassFilter
- LaptopGestures
- RxCallbacks
- export_model.py
- DebuglevelEnum
- settings.json
- LaserPointer
- DetectionManager
- CodeContent
- drawHeader
- drawImpact
- Action
- ContentKind
- AndroidJavaObject
- Target
- Source
- Page
- Side
- Agent Instructions — Unity Passthrough Camera API Samples
- HandOutline
- Style
- Command
- Theme
- hzdb
- ScreenMode
- hzdb
- .onDescendantInvalidated
- Channel
- .aider.conf.yml
- GEMINI.md
- setup-meta-quest-tools.md

## God Nodes (most connected - your core abstractions)
1. `WristPanel` - 73 edges
2. `BrowserPanel` - 69 edges
3. `NewVisionWebView` - 47 edges
4. `CompanionController` - 42 edges
5. `ObjectInfoView` - 40 edges
6. `SentisInferenceUiManager` - 40 edges
7. `WebViewBridge` - 40 edges
8. `ObjectInfoLookup` - 38 edges
9. `NvStyle` - 35 edges
10. `PassthroughCameraSamples.MultiObjectDetection` - 35 edges

## Surprising Connections (you probably didn't know these)
- `ObjectInfoLookup` --references--> `ObjectInfoView`  [EXTRACTED]
  Assets/PassthroughCameraApiSamples/MultiObjectDetection/ObjectInfo/Scripts/ObjectInfoLookup.cs → Assets/PassthroughCameraApiSamples/MultiObjectDetection/ObjectInfo/Scripts/ObjectInfoView.cs
- `ObjectInfoView` --references--> `NvBrackets`  [EXTRACTED]
  Assets/PassthroughCameraApiSamples/MultiObjectDetection/ObjectInfo/Scripts/ObjectInfoView.cs → Assets/PassthroughCameraApiSamples/MultiObjectDetection/Style/Scripts/NvBrackets.cs
- `ObjectInfoView` --references--> `NvGlitch`  [EXTRACTED]
  Assets/PassthroughCameraApiSamples/MultiObjectDetection/ObjectInfo/Scripts/ObjectInfoView.cs → Assets/PassthroughCameraApiSamples/MultiObjectDetection/Style/Scripts/NvGlitch.cs
- `WristPanel` --references--> `NvGlitch`  [EXTRACTED]
  Assets/PassthroughCameraApiSamples/MultiObjectDetection/Stick/Scripts/WristPanel.cs → Assets/PassthroughCameraApiSamples/MultiObjectDetection/Style/Scripts/NvGlitch.cs
- `WristPanel` --references--> `HandTracker`  [EXTRACTED]
  Assets/PassthroughCameraApiSamples/MultiObjectDetection/Stick/Scripts/WristPanel.cs → Assets/PassthroughCameraApiSamples/MultiObjectDetection/Stick/Scripts/HandTracker.cs

## Import Cycles
- None detected.

## Communities (78 total, 35 thin omitted)

### Community 0 - "unityengine"
Cohesion: 0.06
Nodes (10): NewVisionBuild, PassthroughCameraSamples.BrightnessEstimation, PassthroughCameraSamples.MultiObjectDetection, PassthroughCameraSamples.CameraViewer, NewVision.Editor, PassthroughCameraSamples, PassthroughCameraSamples.ShaderSample, PassthroughCameraSamples.StartScene (+2 more)

### Community 2 - "CompanionController"
Cohesion: 0.07
Nodes (14): CompanionController, Act, Acts, Available, Key, Pivot, Pose, Neutral (+6 more)

### Community 3 - "RectTransform"
Cohesion: 0.06
Nodes (11): BrightnessEstimationDebugger, BrightnessEstimationManager, CameraViewerManager, DetectionUiBlinkText, DetectionUiTextWritter, BoundingBoxData, DebugUIBuilder, ActiveUpdate (+3 more)

### Community 6 - "Unity-PassthroughCameraAPISamples"
Cohesion: 0.06
Nodes (34): Additional Components, AI coding agents, Documentation, Download the Project, Getting Started, Learn More, License, Project Content (+26 more)

### Community 8 - "GeminiClient"
Cohesion: 0.10
Nodes (11): GeminiClient, ApiError, ApiKey, Candidate, Chunk, Content, HasKey, IsBusy (+3 more)

### Community 10 - "WristPanel"
Cohesion: 0.15
Nodes (5): WristPanel, ClassPages, FindPages, IsVisible, TileWidth

### Community 11 - "SentisInferenceUiManager"
Cohesion: 0.20
Nodes (3): SentisInferenceUiManager, ContentParent, MaxVisibleBoxes

### Community 12 - "ObjectMemory"
Cohesion: 0.14
Nodes (6): ObjectMemory, Count, InView, Spot, Target, TargetFound

### Community 13 - "SentisInferenceRunManager"
Cohesion: 0.12
Nodes (3): SentisInferenceRunManager, LastInferenceMs, LastLayersPerFrame

### Community 14 - "WebViewBridge"
Cohesion: 0.08
Nodes (15): WebViewBridge, Available, CanGoBack, CanGoForward, FocusVersion, Height, InputFocused, InputType (+7 more)

### Community 15 - "ObjectInfoLookup"
Cohesion: 0.15
Nodes (5): ObjectInfoLookup, DwellEnabled, LastAnswerSeconds, QrEnabled, Theme

### Community 16 - "StickClient"
Cohesion: 0.13
Nodes (7): ObjectSizes, StickClient, Connected, LaptopPaired, PingMs, PortState, Status

### Community 18 - "BrowserPointer"
Cohesion: 0.14
Nodes (10): BrowserPointer, IsHand, PressAmount, Pressed, PressEnded, PressRay, PressStarted, Ray (+2 more)

### Community 19 - "main.cpp"
Cohesion: 0.17
Nodes (13): applyTheme(), bootAnimation(), brackets(), crtOpen(), decode(), easeOut(), loadFonts(), loadSettings() (+5 more)

### Community 20 - "handleLine"
Cohesion: 0.19
Nodes (18): applyScreen(), clockKnown(), clockText(), findCommand(), glitch(), handleBleInbox(), handleLine(), handleSerial() (+10 more)

### Community 21 - "attributes"
Cohesion: 0.11
Nodes (17): description, label, options, placeholder, render, value, body, attributes (+9 more)

### Community 24 - "Knock"
Cohesion: 0.12
Nodes (16): fxLevelNow(), Knock, K_DROP, K_NONE, K_STRIKE, K_SWING, KnockSense, fallSince (+8 more)

### Community 25 - "LinkPreview.Fetch"
Cohesion: 0.20
Nodes (5): LinkPreview, Data, LimitedDownloadHandler, Length, Text

### Community 29 - "DetectionUiMenuManager"
Cohesion: 0.07
Nodes (9): CameraToWorldCameraCanvas, CameraToWorldManager, CameraToWorldRayRenderer, DetectionUiMenuManager, IsInputActive, IsPaused, InputManager, Instance (+1 more)

### Community 30 - "MonoBehaviour"
Cohesion: 0.16
Nodes (4): StickController, HandedInputSelector, ReturnToStartScene, StartMenu

### Community 31 - "NvStyle"
Cohesion: 0.14
Nodes (11): NvStyle, Accent, Dim, Font, FxLevel, Muted, Panel, Scan (+3 more)

### Community 32 - "NvBrackets"
Cohesion: 0.26
Nodes (6): SharpFrame, Thickness, NvBrackets, Length, OutlineAlpha, Thickness

### Community 35 - "NvGlitch"
Cohesion: 0.12
Nodes (7): NvGlitch, Knock, None, Strike, Swing, Spring, Moving

### Community 37 - "ClassFilter"
Cohesion: 0.27
Nodes (4): ClassFilter, EnabledClasses, EnabledCount, Labels

### Community 38 - "LaptopGestures"
Cohesion: 0.24
Nodes (3): LaptopGestures, Enabled, LookingAtLaptop

### Community 39 - "RxCallbacks"
Cohesion: 0.22
Nodes (3): RxCallbacks, partial, ServerCallbacks

### Community 42 - "DebuglevelEnum"
Cohesion: 0.20
Nodes (7): PassthroughCameraDebugger, DebuglevelEnum, ALL, NONE, ONLY_ERROR, ONLY_LOG, ONLY_WARNING

### Community 43 - "settings.json"
Cohesion: 0.20
Nodes (9): enabledPlugins, meta-vr@meta-quest, extraKnownMarketplaces, meta-quest, autoUpdate, source, $schema, repo (+1 more)

### Community 47 - "drawHeader"
Cohesion: 0.42
Nodes (9): battery(), chamfer(), drawBattery(), drawDebug(), drawHeader(), drawHome(), drawPill(), drawRaw() (+1 more)

### Community 48 - "drawImpact"
Cohesion: 0.28
Nodes (8): drawGlitch(), drawImpact(), drawMotion(), due(), fxMoving(), fxRand(), render(), scrollArea()

### Community 50 - "ContentKind"
Cohesion: 0.25
Nodes (8): ContentKind, Contact, Email, Phone, Product, Text, Url, Wifi

### Community 52 - "Target"
Cohesion: 0.29
Nodes (7): Target, Button, Grab, None, Page, ResizeLeft, ResizeRight

### Community 53 - "Source"
Cohesion: 0.33
Nodes (6): Source, LeftController, LeftHand, None, RightController, RightHand

### Community 54 - "Page"
Cohesion: 0.33
Nodes (6): Page, Classes, Find, General, Stick, Sys

### Community 55 - "Side"
Cohesion: 0.33
Nodes (6): hitSide(), Side, SIDE_BOTTOM, SIDE_LEFT, SIDE_RIGHT, SIDE_TOP

### Community 56 - "Agent Instructions — Unity Passthrough Camera API Samples"
Cohesion: 0.40
Nodes (4): Agent Instructions — Unity Passthrough Camera API Samples, Meta Quest tooling, Quest / Horizon-specific notes, Source-of-truth files (read these first, do not duplicate their contents in this file)

### Community 59 - "Style"
Cohesion: 0.40
Nodes (5): Style, Row, Small, Tab, Tile

### Community 60 - "Command"
Cohesion: 0.40
Nodes (5): Command, key, label, modifier, name

### Community 61 - "Theme"
Cohesion: 0.40
Nodes (5): Theme, b, g, name, r

### Community 62 - "hzdb"
Cohesion: 0.50
Nodes (3): npx, @meta-quest/hzdb, hzdb

### Community 63 - "ScreenMode"
Cohesion: 0.50
Nodes (4): ScreenMode, SCREEN_DEBUG, SCREEN_HOME, SCREEN_OFF

### Community 64 - "hzdb"
Cohesion: 0.50
Nodes (3): npx, @meta-quest/hzdb, hzdb

### Community 66 - "Channel"
Cohesion: 0.67
Nodes (3): Channel, FROM_BLE, FROM_SERIAL

## Knowledge Gaps
- **139 isolated node(s):** `name`, `label`, `modifier`, `key`, `partial` (+134 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 405 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **35 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `BrowserPanel` connect `BrowserPanel` to `unityengine`, `RectTransform`, `Image`, `LaptopGestures`, `Vector3`, `SentisInferenceUiManager`, `WebViewBridge`, `ObjectInfoLookup`, `BrowserPointer`, `Target`, `CodeScanner`, `MonoBehaviour`?**
  _High betweenness centrality (0.112) - this node is a cross-community bridge._
- **What connects `name`, `label`, `modifier` to the rest of the system?**
  _139 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `unityengine` be split into smaller, more focused modules?**
  _Cohesion score 0.059096459096459095 - nodes in this community are weakly interconnected._
- **Why does `WristPanel` connect `WristPanel` to `unityengine`, `WristPanel.Build`, `NvGlitch`, `Image`, `RectTransform`, `LaptopGestures`, `BrowserPanel`, `Vector3`, `SentisInferenceUiManager`, `ObjectMemory`, `StickClient`, `Action`, `Page`, `WristPanel.Refresh`, `Style`, `HandTracker`, `MonoBehaviour`?**
  _High betweenness centrality (0.097) - this node is a cross-community bridge._
- **Should `WristPanel.Build` be split into smaller, more focused modules?**
  _Cohesion score 0.14482758620689656 - nodes in this community are weakly interconnected._
- **Why does `ObjectInfoLookup` connect `ObjectInfoLookup` to `unityengine`, `ObjectInfoView`, `CompanionController`, `LaptopGestures`, `BrowserPanel`, `GeminiClient`, `ObjectInfoLookup.ShowLink`, `Vector3`, `SentisInferenceUiManager`, `SentisInferenceRunManager`, `Action`, `CodeScanner`, `MonoBehaviour`, `NvStyle`?**
  _High betweenness centrality (0.066) - this node is a cross-community bridge._
- **Should `CompanionController` be split into smaller, more focused modules?**
  _Cohesion score 0.06734006734006734 - nodes in this community are weakly interconnected._