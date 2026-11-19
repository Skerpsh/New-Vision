# Graph Report - Unity-PassthroughCameraApiSamples  (2026-11-19)

## Corpus Check
- 81 files · ~68,773 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 320 file(s) not represented in the graph (top: .meta 240, .prefab 22, .asset 13)
- WARNING: 320 of 444 files seen (72%) have no supported extractor and are missing from this graph entirely. It may look complete while representing only part of the codebase.

## Summary
- 1291 nodes · 2836 edges · 77 communities (43 shown, 34 thin omitted)
- Extraction: 96% EXTRACTED · 4% INFERRED · 0% AMBIGUOUS · INFERRED: 127 edges (avg confidence: 0.85)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `9a6e953f`
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
- MonoBehaviour
- Knock
- LinkPreview.Fetch
- OneEuro
- android.webkit.WebView
- OnlineRecognizer
- DetectionUiMenuManager
- MonoBehaviour
- NvStyle
- NvBrackets
- ObjectInfoView
- NvGlitch
- Image
- ClassFilter
- SpeechRecognizer
- RxCallbacks
- export_model.py
- DebuglevelEnum
- settings.json
- SubtitleView
- CodeContent
- drawHeader
- drawImpact
- Action
- ContentKind
- StickController.Init
- Target
- Page
- Side
- Agent Instructions — Unity Passthrough Camera API Samples
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
1. `WristPanel` - 74 edges
2. `BrowserPanel` - 69 edges
3. `NewVisionWebView` - 47 edges
4. `WebViewBridge` - 40 edges
5. `CompanionController` - 40 edges
6. `ObjectInfoView` - 40 edges
7. `SentisInferenceUiManager` - 38 edges
8. `PassthroughCameraSamples.MultiObjectDetection` - 37 edges
9. `ObjectInfoLookup` - 37 edges
10. `NvStyle` - 35 edges

## Surprising Connections (you probably didn't know these)
- `DetectionManager` --references--> `DetectionUiMenuManager`  [EXTRACTED]
  Assets/PassthroughCameraApiSamples/MultiObjectDetection/DetectionManager/Scripts/DetectionManager.cs → Assets/PassthroughCameraApiSamples/MultiObjectDetection/DetectionManager/Scripts/DetectionUiMenuManager.cs
- `SentisInferenceRunManager` --references--> `DetectionUiMenuManager`  [EXTRACTED]
  Assets/PassthroughCameraApiSamples/MultiObjectDetection/SentisInference/Scripts/SentisInferenceRunManager.cs → Assets/PassthroughCameraApiSamples/MultiObjectDetection/DetectionManager/Scripts/DetectionUiMenuManager.cs
- `SentisModelEditorConverter` --references--> `SentisInferenceRunManager`  [EXTRACTED]
  Assets/PassthroughCameraApiSamples/MultiObjectDetection/SentisInference/Editor/SentisModelEditorConverter.cs → Assets/PassthroughCameraApiSamples/MultiObjectDetection/SentisInference/Scripts/SentisInferenceRunManager.cs
- `SubtitleView` --references--> `SpeechRecognizer`  [EXTRACTED]
  Assets/PassthroughCameraApiSamples/MultiObjectDetection/Speech/Scripts/SubtitleView.cs → Assets/PassthroughCameraApiSamples/MultiObjectDetection/Speech/Scripts/SpeechRecognizer.cs
- `NewVisionControls` --references--> `SpeechRecognizer`  [EXTRACTED]
  Assets/PassthroughCameraApiSamples/MultiObjectDetection/Stick/Scripts/NewVisionControls.cs → Assets/PassthroughCameraApiSamples/MultiObjectDetection/Speech/Scripts/SpeechRecognizer.cs

## Import Cycles
- None detected.

## Communities (77 total, 34 thin omitted)

### Community 0 - "unityengine"
Cohesion: 0.06
Nodes (11): NewVisionBuild, SherpaOnnx, PassthroughCameraSamples.BrightnessEstimation, PassthroughCameraSamples.MultiObjectDetection, PassthroughCameraSamples.CameraViewer, NewVision.Editor, PassthroughCameraSamples, PassthroughCameraSamples.ShaderSample (+3 more)

### Community 2 - "CompanionController"
Cohesion: 0.06
Nodes (13): CompanionController, Act, Acts, Available, Key, Pivot, Pose, Neutral (+5 more)

### Community 3 - "RectTransform"
Cohesion: 0.05
Nodes (12): BrightnessEstimationDebugger, BrightnessEstimationManager, DetectionUiBlinkText, DetectionUiTextWritter, LaptopGestures, Enabled, LookingAtLaptop, DebugUIBuilder (+4 more)

### Community 6 - "Unity-PassthroughCameraAPISamples"
Cohesion: 0.05
Nodes (35): Additional Components, AI coding agents, Documentation, Download the Project, Getting Started, Learn More, License, Project Content (+27 more)

### Community 8 - "GeminiClient"
Cohesion: 0.10
Nodes (11): GeminiClient, ApiError, ApiKey, Candidate, Chunk, Content, HasKey, IsBusy (+3 more)

### Community 10 - "WristPanel"
Cohesion: 0.09
Nodes (6): LaptopPaired, WristPanel, ClassPages, FindPages, IsVisible, TileWidth

### Community 11 - "SentisInferenceUiManager"
Cohesion: 0.22
Nodes (3): SentisInferenceUiManager, ContentParent, MaxVisibleBoxes

### Community 12 - "ObjectMemory"
Cohesion: 0.13
Nodes (6): ObjectMemory, Count, InView, Spot, Target, TargetFound

### Community 13 - "SentisInferenceRunManager"
Cohesion: 0.13
Nodes (3): SentisInferenceRunManager, LastInferenceMs, LastLayersPerFrame

### Community 14 - "WebViewBridge"
Cohesion: 0.09
Nodes (15): WebViewBridge, Available, CanGoBack, CanGoForward, FocusVersion, Height, InputFocused, InputType (+7 more)

### Community 15 - "ObjectInfoLookup"
Cohesion: 0.14
Nodes (5): ObjectInfoLookup, DwellEnabled, LastAnswerSeconds, QrEnabled, Theme

### Community 16 - "StickClient"
Cohesion: 0.13
Nodes (7): WifiConnector, ObjectSizes, StickClient, Connected, PingMs, PortState, Status

### Community 18 - "BrowserPointer"
Cohesion: 0.05
Nodes (19): BrowserPointer, IsHand, PressAmount, Pressed, PressEnded, PressRay, PressStarted, Ray (+11 more)

### Community 19 - "main.cpp"
Cohesion: 0.17
Nodes (13): applyTheme(), bootAnimation(), brackets(), crtOpen(), decode(), easeOut(), loadFonts(), loadSettings() (+5 more)

### Community 20 - "handleLine"
Cohesion: 0.19
Nodes (18): applyScreen(), clockKnown(), clockText(), findCommand(), glitch(), handleBleInbox(), handleLine(), handleSerial() (+10 more)

### Community 21 - "attributes"
Cohesion: 0.11
Nodes (17): description, label, options, placeholder, render, value, body, attributes (+9 more)

### Community 23 - "MonoBehaviour"
Cohesion: 0.12
Nodes (6): CameraViewerManager, EnvironmentRayCastSampleManager, CodeScanner, Current, ShaderSampleManager, ReturnToStartScene

### Community 24 - "Knock"
Cohesion: 0.12
Nodes (16): fxLevelNow(), Knock, K_DROP, K_NONE, K_STRIKE, K_SWING, KnockSense, fallSince (+8 more)

### Community 25 - "LinkPreview.Fetch"
Cohesion: 0.21
Nodes (5): LinkPreview, Data, LimitedDownloadHandler, Length, Text

### Community 28 - "OnlineRecognizer"
Cohesion: 0.13
Nodes (12): Dll, FeatureConfig, HomophoneReplacerConfig, OnlineCtcFstDecoderConfig, OnlineModelConfig, OnlineParaformerModelConfig, OnlineRecognizer, OnlineRecognizerConfig (+4 more)

### Community 29 - "DetectionUiMenuManager"
Cohesion: 0.07
Nodes (9): CameraToWorldCameraCanvas, CameraToWorldManager, CameraToWorldRayRenderer, DetectionUiMenuManager, IsInputActive, IsPaused, InputManager, Instance (+1 more)

### Community 31 - "NvStyle"
Cohesion: 0.13
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

### Community 38 - "SpeechRecognizer"
Cohesion: 0.10
Nodes (12): SpeechRecognizer, Current, Enabled, Level, State, Error, Installing, Listening (+4 more)

### Community 39 - "RxCallbacks"
Cohesion: 0.22
Nodes (3): RxCallbacks, partial, ServerCallbacks

### Community 42 - "DebuglevelEnum"
Cohesion: 0.14
Nodes (8): DetectionManager, PassthroughCameraDebugger, DebuglevelEnum, ALL, NONE, ONLY_ERROR, ONLY_LOG, ONLY_WARNING

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
- **149 isolated node(s):** `IsInputActive`, `IsPaused`, `LastInferenceMs`, `LastLayersPerFrame`, `Off` (+144 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 425 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **34 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `WristPanel` connect `WristPanel` to `unityengine`, `WristPanel.Build`, `NvGlitch`, `Image`, `RectTransform`, `Vector3`, `ObjectMemory`, `SentisInferenceUiManager.DrawUIBoxes`, `Action`, `BrowserPointer`, `StickController.Init`, `Page`, `MonoBehaviour`, `OneEuro`, `Style`?**
  _High betweenness centrality (0.104) - this node is a cross-community bridge._
- **What connects `IsInputActive`, `IsPaused`, `LastInferenceMs` to the rest of the system?**
  _149 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `unityengine` be split into smaller, more focused modules?**
  _Cohesion score 0.060073260073260075 - nodes in this community are weakly interconnected._
- **Why does `BrowserPanel` connect `BrowserPanel` to `unityengine`, `WristPanel.Build`, `RectTransform`, `Image`, `Vector3`, `SentisInferenceUiManager.DrawUIBoxes`, `WebViewBridge`, `ObjectInfoLookup`, `BrowserPointer`, `Target`, `MonoBehaviour`?**
  _High betweenness centrality (0.092) - this node is a cross-community bridge._
- **Should `CompanionController` be split into smaller, more focused modules?**
  _Cohesion score 0.06019871420222092 - nodes in this community are weakly interconnected._
- **Why does `SpeechRecognizer` connect `SpeechRecognizer` to `unityengine`, `SubtitleView`, `StickController.Init`, `MonoBehaviour`, `OnlineRecognizer`, `NvStyle`?**
  _High betweenness centrality (0.065) - this node is a cross-community bridge._
- **Should `RectTransform` be split into smaller, more focused modules?**
  _Cohesion score 0.054563492063492064 - nodes in this community are weakly interconnected._