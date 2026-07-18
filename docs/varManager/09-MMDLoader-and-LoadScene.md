# 09 — MMDLoader & LoadScene (MMD motion subsystem)

A secondary feature: convert **MikuMikuDance (MMD)** motions into VaM **Timeline** animations. It is two programs joined by one file-drop contract.

| Component | Project | Framework | Role |
|---|---|---|---|
| **MMDLoader** | WPF + WinForms interop | .NET 6.0-windows | Desktop GUI. Scans MMD folders, lets the user pick audio/camera/person VMD files, copies them into VaM, **writes** `loadscene.json`. A thin file-marshaller — no MMD parsing. |
| **LoadScene** | Unity plugin (`MVRScript`) | .NET Framework 3.5, ILMerged | In-game VaM plugin ("feelfar"). Polls `loadscene.json`, parses VMD/PMX, retargets MMD motion → VaM controllers/morphs, builds AcidBubbles **Timeline** presets. |

```
MMD folders (VMD/audio) ──scan──► MMDLoader GUI ──pick audio+camera+person VMDs, heel/leg/pos──►
   copies files → <VaM>\MMDForLoad\ ;  writes → <VaM>\Custom\PluginData\feelfar\loadscene.json
        ▼ (polled every frame)
   LoadScene plugin: read json → delete it → parse VMD via bundled LibMMD → drive a hidden g2f.pmx
   skeleton → sample bone transforms per frame → VaM FreeControllerV3 + finger + face-morph curves
        ▼
   write Timeline .vap presets → load onto Person / WindowCamera atoms → register triggers to auto-play
```

The user must already have a VaM scene open containing the target **Person** atom(s), an **AudioSource** atom, and a **WindowCamera** atom. LoadScene matches existing atoms; it does not create Person atoms.

## MMDLoader (WPF) workflow

- **Settings:** only two user strings — `vampath` (validated by `VaM.exe` presence) and `mmdpath` (last-scanned MMD root). WindowSettings exposes only `vampath`.
- **Directory scan** (`buttonLoadMMD_Click`): recursively walk the MMD root; a directory is an "MMD project" if it has ≤ 5 subdirectories and each subdir holds *purely* motion XOR *purely* audio (those subdirs' files fold up into the parent). Keyed by directory leaf name; filtered live by a textbox.
  - ✎ Known legacy bugs to fix in a rebuild: line ~87 `subaudios.Concat(...)` discards its result (subdir `.mp3`s ignored in the XOR test); a subdir with *both* vmd and audio isn't folded in.
- **Project selection** → combos with collision-suffixed names:
  - Audio combo: `None` + audio files by **descending size**; auto-select the largest; WPF `MediaPlayer` preview with scrub slider.
  - Camera VMD combo: `None` + vmds by descending size; auto-select first matching `\bcam\b|camera|镜头|カメラ|카메라`.
  - Person VMD combos: all vmds by descending size; Motion1 auto-selects the first *non-camera* vmd.
- **Per-person control** (`UserCtlPersonVMD`, up to 6 persons): Motion1 (body), Motion2 (expression), PersonOrder (1-based), IgnoreFace, StraightLeg + work angle (default 140), PosX/Y/Z, high-heel toggle + foot/toe angles (default −45/35) + hold-rotation force, sample Rate. "Add Person" clones person1 with a PosX offset to avoid collisions. Heel enabled → `posY += -sin(footXangle°)*0.07`.
- **Load** (`LoadMMD`): reset `<vampath>\MMDForLoad\`; build the root JSON; copy the chosen audio/camera/person VMDs into `MMDForLoad\`; emit resources; delete stale `Preset_mmdloader*.vap` presets; write `loadscene.json` (tab-indented) to `Custom\PluginData\feelfar\`.
- **Test buttons** write a one-resource `loadscene.json` (heel-only, or a VMD with `isTest:true` for preview-without-saving).
- Files created: copies in `MMDForLoad\` (flat, collision-suffixed) + `loadscene.json`. (`Comm.CreateSymbolicLink` is declared but unused — files are copied, not linked.)

## The `loadscene.json` contract (⚠ load-bearing — the single integration point)

**Path:** `<VaM>/Custom/PluginData/feelfar/loadscene.json`. **Producer:** MMDLoader (and varManager). **Consumer:** LoadScene, which parses then **deletes** it (one-shot).

### Root
```jsonc
{ "rescan": "false",              // "true" → SuperController.RescanPackages()
  "resources": [ /* processed in array order */ ] }
```

### Resource — common fields
```jsonc
{ "type": "audio|cameravmd|personvmd|highheel|scenes|atom|atomSubscene|empytscene|
           hairstyle|morphs|skin|clothing|plugin|breast|glute|pose|looks|animation",
  "saveName": "<path>",           // '\' replaced with '/'; meaning depends on type
  "characterGender": "female",    // constant in MMDLoader
  "ignoreGender": "true",         // constant → match persons regardless of gender
  "personOrder": "1" }            // 1-based; picks the Nth matching Person atom
```
> `type`/`ignoreGender`/`rescan` are emitted as JSON **strings** but read tolerantly (`.AsBool`/`.AsInt`). Later booleans/numbers (`AudioSourceControl`, `isTest`, heel floats) are real JSON types.

### Type-specific fields
- **`audio`** (always once): `saveName:"MMDForLoad/<song>"` or `""` (empty ⇒ stop AudioSource + remove `AnimaStartAudio` trigger).
- **`cameravmd`** (always once): `saveName:"MMDForLoad/<camera.vmd>"` or `""`; optional `"AudioSourceControl": true` if audio loaded. Empty ⇒ reset WindowCamera.
- **`personvmd`** (0–6): `saveName` = **absolute** path to Motion1 vmd in `MMDForLoad`; `ignoreFace`, `enableHeel`, `footJointDriveXTargetAdjust`, `toeJointDriveXTargetAdjust`, `holdRotationMaxForceAdjust`, `straightLeg`, `straightLegWorkAngle`, `posY/posX/posZ` (floats); `isTest` (bool); optional `personvmd2` (absolute path to Motion2, merged), optional `AudioSourceControl`. (`sampleSpeed` is read by LoadScene if present but not written by this build → defaults to 1.)
- **`highheel`** (test/standalone): heel/leg/pos fields, no VMD — applies heel/pos to the Nth person without building a Timeline.
- Other types (`scenes/atom/atomSubscene/empytscene/hairstyle/morphs/skin/clothing/plugin/breast/glute/pose/looks/animation`) are handled by LoadScene but **produced by varManager**, not MMDLoader (shared loader vocabulary; `merge` bool honored on several). See [06](./06-Preview-Scene-Analysis-and-Loading.md).

> Note the misspellings are part of the wire contract: **`empytscene`** (empty scene) and **`atomSubscene`**.

## LoadScene plugin behavior

- **Polling/dispatch** (`loadscene.cs`): `Update()` runs a coroutine each frame; if `loadscene.json` exists → read, `Thread.Sleep(1000)`, **delete**, parse. `rescan` → `RescanPackages()`. Iterate `resources`: `empytscene`→`NewScene()`; `highheel`/most types queued; `scenes/atom/audio/cameravmd/atomSubscene` handled inline. Then batch-add atoms and process person-targeted presets.
- **Person matching**: collect `Person` atoms that are `on`; since `ignoreGender:true`, decrement `personOrder` per person until 0 → target atom.
- **`personvmd`** (core): reset physics; build a `Mmd2TimelinePersonAtom` wrapper; apply heel/leg/face settings; `InitAtom()` builds the hidden MMD skeleton; `ImportVmd(saveName)` (+ `personvmd2` merged, length = max); set position/sampleSpeed; `StartPlay` samples the whole motion into a `TimelineJson`; if not test → save `Preset_mmdloader<order>.vap`, load it, register a `person1MotionActive` trigger that plays the Timeline.
- **Skeleton & retargeting** (`Mmd2TimelinePersonAtom` + `DazBoneMapping`): loads a base **PMX** at `Custom/Scripts/g2f.pmx` (a Genesis2Female-topology MMD model shipped with the tool — ⚠ **critical external asset**), overwrites its bone positions to match the VaM DAZ rig (×10 scale), maps Japanese MMD bone names → DAZ bones (`センター`→hip, `頭`→head, `左腕`→lShldr, fingers, twist bones via `boneA|boneB|ratio`), synthesizes fake arm/finger hierarchy, applies MMD→Unity axis fix (constant quaternion `(0,1,0,0)` + ±36° arm/shoulder correction). Ignored bones: tongue, eyes, pectorals, jaw, carpals.
- **Sampling → Timeline**: for each sampled frame, pose the MMD skeleton, drive VaM controllers, and record each controller's local position/rotation as Timeline keyframes (curve type "3"). Fingers → 25 VaM finger params. Face morphs → Japanese-morph → VaM-morph table (`まばたき`→Eyes Closed, `あ`→AA+Mouth Open, …), skipped if `ignoreFace`. 30 fps assumed throughout.
- **`cameravmd`** (`Mmd2TimelineCameraAtom`): sample the WindowCamera path (position + rotation via `Quaternion.Euler(-180/π · rot)` + FOV FloatParam), build a preset embedding both **Timeline** and **Embody**; register a `cameraActive` trigger that activates Embody "passenger" mode and plays the camera Timeline.
- **`audio`**: queue the wav/mp3 on the AudioSource atom; register `AnimaStartAudio` trigger.
- **Auto-sync**: three `motionAnimationMaster` triggers fire at t=0 — `person1MotionActive`, `cameraActive`, `AnimaStartAudio` — so motion, camera, and audio start together.

## Timeline preset object model (the `.vap` content)
`TimelineJson → { AtomType, Clips:[ TimelineClipJson ] }`. A `TimelineClipJson` carries `AnimationName/Length`, blend/loop/segment/speed/weight strings, optional `AudioSourceControl`, `Controllers:[{Controller, ControlPosition/Rotation, X,Y,Z,RotX,RotY,RotZ,RotW keyframe arrays}]`, and `FloatParams:[{Storable(default geometry), Name, Min, Max, Value:[frames]}]`. Each frame: `{t, v, c(curve), i, o}`. This clip array is injected as `$$$clips$$$` into a hard-coded AcidBubbles Timeline preset skeleton (Person preset references `AcidBubbles.Timeline.latest:.../VamTimeline.AtomAnimation.cslist`).

## Constraints & dependencies (LoadScene)
- **.NET 3.5**, VaM 1.20.77.9 Mono runtime. References `Assembly-CSharp.dll` + `UnityEngine*.dll` from the VaM install.
- ⚠ **No custom enums** and **no `Awake()` override** (VaM's dynamic C# compiler crashes on them — use `Init()`).
- **ILMerged** (`ILMerge 3.0.29` + `MSBuild.ILMerge.Task 1.1.3`) to fold bundled **SimpleJSON** and **LibMMD** (a ~40-file from-scratch PMX/VMD parser with SJIS decoding) into one DLL. Also ships a `LoadScene.cslist` for VaM's cslist-based loading. Uses `MVR.FileManagementSecure` for sandboxed file I/O.
- **Required runtime assets** (not created by these tools): `Custom/Scripts/g2f.pmx`, `AcidBubbles.Timeline.latest`, `AcidBubbles.Embody.latest`, and pre-existing Person/AudioSource/WindowCamera atoms in the open scene.

## Rebuild notes
- The **only** integration contract is `loadscene.json`; the two halves can be reimplemented independently if the schema, the `MMDForLoad/`-relative-vs-absolute `saveName` conventions, and the optional-field booleans are preserved.
- All MMD intelligence lives in LoadScene/LibMMD; retargeting is **sample-based** (drive a posed skeleton frame-by-frame, record VaM controller transforms), so fidelity depends on `g2f.pmx` alignment and the `DazBoneMapping`/`FaceMorph`/finger translation tables.
- The MMD subsystem is bound to VaM's ancient Mono runtime; a rebuild of *varManager* can leave LoadScene largely as-is and only modernize the MMDLoader GUI and the `loadscene.json` writer.

Continue to [10 — UI Feature Catalog](./10-UI-Feature-Catalog.md).
