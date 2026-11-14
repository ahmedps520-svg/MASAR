# MASAR: Phase 0 Technical Research

Research date: **2026-11-14**. Sources are listed at the end of each section.

Confidence labels:
- **[Official]**: from Apple, Epic, Unity, Godot or Logitech documentation, or kernel source.
- **[Community]**: forum posts and third-party repositories (lower reliability).
- **[Inference]**: our own reasoning, which still needs device testing.

The facts that matter most were checked more than once. For example, `GCRacingWheel` availability was confirmed by two independent research passes **and** by a direct read of Apple's documentation metadata.

---

## 1. Target hardware: iPad Pro 11-inch (M5, 256 GB)

| Fact | Value | Source |
|---|---|---|
| CPU | 9-core (3 performance "super" cores + 6 efficiency cores) on the 256/512 GB models | [Official] Apple specs |
| GPU | 10-core, **hardware-accelerated ray tracing** (3rd-generation RT engine), a Neural Accelerator in each GPU core | [Official] Apple specs, newsroom |
| Unified memory | **12 GB** (256 GB model) | [Official] Apple specs |
| Memory bandwidth | 153 GB/s | [Official] Apple specs |
| Display | 2420 × 1668 at 264 ppi, ProMotion 10–120 Hz | [Official] Apple specs |
| Metal 4 | Supported on A14 or later, so the M5 qualifies | [Official] Apple Metal page, [Inference] |
| App memory | `com.apple.developer.kernel.increased-memory-limit` entitlement (iPadOS 15+, "only available on some device models"). `os_proc_available_memory()` (iPadOS 13+) gives the real headroom at run time | [Official] |
| Thermal | `ProcessInfo.thermalState` (iPadOS 11+): nominal / fair / serious / critical. **No public API exposes GPU temperature or per-frame GPU load** beyond Metal's command-buffer GPU timestamps | [Official], [Inference] |
| Max app size | 4 GB uncompressed app binary and bundle. Larger content must be downloaded (Background Assets / on-demand) | [Official] App Store Connect |

**What this means for MASAR:**
- 12 GB is shared by the OS, the CPU and the GPU. A realistic budget is about **3–5 GB** for the whole game until `os_proc_available_memory()` is measured on the device.
- Sustained performance is lower than peak, because the iPad is passively cooled. Target a **stable 60 fps, with a 40/30 fps fallback**. Treat 120 Hz as optional.
- The native resolution is high, so render below native and upscale.

Sources: https://www.apple.com/ipad-pro/specs/ · https://www.apple.com/newsroom/2025/10/apple-introduces-the-powerful-new-ipad-pro-with-the-m5-chip/ · https://developer.apple.com/metal/ · https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.developer.kernel.increased-memory-limit · https://developer.apple.com/documentation/os/os_proc_available_memory · https://developer.apple.com/documentation/foundation/processinfo/thermalstate-swift.property · https://developer.apple.com/help/app-store-connect/reference/app-uploads/maximum-build-file-sizes

---

## 2. Logitech G923 (PlayStation version) on iPadOS

**This is the single highest-risk requirement in the project.**

### 2.1 Apple's GameController framework

- **[Official] `GCRacingWheel` is macOS-only.** Apple's documentation metadata lists only **macOS 13.0+ and Mac Catalyst 16.0+**; iOS and iPadOS are not listed. The article is titled "Add support for racing wheel devices in **macOS**". (Mac Catalyst means iPad-style apps running *on a Mac*.) We verified this directly on 2026-11-14.
- **[Official] No force-feedback API for wheels anywhere in GameController.**
  - `GCRacingWheel` exposes input only.
  - `GCSteeringWheelElement` only reports `maximumDegreesOfRotation`.
  - `GCDeviceHaptics` is rumble on game controllers, not wheel force feedback.
  - Apple's `ForceFeedback` framework is macOS-only.
- **[Official] No racing-wheel changes in 2024–2026.** The GameController release notes for WWDC24 and WWDC25 cover visionOS, spatial controllers and the stylus.
- **[Official] Generic USB HID devices are filtered out on iPadOS.**
  - `GCController.supportsHIDDevice(_:)` is macOS 11+ only.
  - CoreHID is macOS 15+ only.
  - An Apple DTS engineer (Jan 2025) said there is no list of supported controllers, and that unsupported devices need a bug report asking Apple to add them.
- **[Official] Apple's iPad support articles list only** Xbox, PlayStation (DualSense, DualShock 4, Access) and MFi controllers. They list no racing wheels.

### 2.2 The wheel itself

| Fact | Detail | Confidence |
|---|---|---|
| USB IDs | VID `0x046D`. PID `0xC267` (PS4 mode, the boot default) and `0xC266` (PC mode, switched by a vendor output report) | [Official] SDL source; [Community] driver wiki |
| FFB protocol | The **classic Logitech protocol** (the same family as the G29), sent as HID output reports. Logitech published technical documentation of it (Linux lg4ff patch, 2014). The Xbox/PC G923 uses HID++ instead | [Official] kernel list; [Community] new-lg4ff |
| TrueForce | Proprietary, on a separate USB interface. Not realistic for us | [Community] |
| Logitech OS support | Windows only (no macOS or iPadOS listed) | [Official] Logitech support |
| Public reports of G923/G29/G920 working on iPad | **None found** | — |

### 2.3 Safari / web

- **[Official]** On iOS-family platforms WebKit's Gamepad API is built on GameController, so the same limits apply. WebHID and WebUSB are not available. WebGPU does ship in Safari 26 on iPadOS.

### 2.4 DriverKit: the only public-API route to the raw wheel

- **[Official]** iPadOS 16+ on **M-series iPads** supports DriverKit families **USBDriverKit, PCIDriverKit and AudioDriverKit**. **HIDDriverKit is macOS-only.**
- **[Official]** The app talks to its driver extension (dext) through IOKit user clients. This needs `com.apple.developer.driverkit.communicates-with-drivers`, and the user has to enable the driver in Settings.
- **[Official, Apple DTS 2026]**
  - With a **paid** developer account, "Development Only" DriverKit entitlements (wildcard vendor ID) allow testing on our own iPad.
  - Distribution needs Apple to approve a `transport.usb` entitlement for Logitech's VID/PID. Apple says it does not limit driver development to the original hardware maker, but approval isn't guaranteed.
  - Xcode can't currently sign USB dexts automatically, so they must be signed manually.
- **[Community] Unresolved risk:** developers report that Apple's built-in HID driver (`AppleUserHIDDrivers`) claims HID-class devices first and **ignores the custom dext**, on both macOS and iPadOS. Nobody has publicly shown a USBDriverKit dext taking a HID wheel over from Apple's driver on iPad.
- **[Inference]** If it does work, we would implement the PS→PC mode switch, input-report parsing and classic Logitech force-feedback commands ourselves. All are documented by the Linux drivers. This is **high effort, high risk, and needs the paid Apple account**.

### 2.5 Conclusions

| Question | Answer |
|---|---|
| Can the G923 PS be read on iPadOS through GameController? | **No (per official docs). The on-device probe will confirm.** |
| Can genuine force feedback reach the G923 from iPadOS? | **No public game/FFB API.** Only a custom USBDriverKit driver could, and that route is unproven. |
| Best viable alternatives | (1) **Wheel bridge:** a PC or small Linux board reads the G923 with full force feedback and streams inputs to the iPad over the network, while the iPad streams physics forces back. (2) **Supported gamepads** (DualSense, Xbox), whose haptics are real. (3) **Touch or tilt.** (4) **Research track:** a DriverKit experiment once a paid account exists. |

Rule 7 applies here: no G923 or force-feedback option will appear in the iPad UI until real input or force reaches real hardware.

Sources: https://developer.apple.com/documentation/gamecontroller/gcracingwheel · https://developer.apple.com/tutorials/data/documentation/gamecontroller/gcracingwheel.json · https://developer.apple.com/documentation/gamecontroller/racing-wheel-device-support · https://developer.apple.com/documentation/gamecontroller/gcsteeringwheelelement · https://developer.apple.com/documentation/gamecontroller/gcdevicehaptics · https://developer.apple.com/documentation/gamecontroller/gccontroller/supportshiddevice(_:) · https://developer.apple.com/documentation/updates/gamecontroller · https://developer.apple.com/documentation/forcefeedback · https://developer.apple.com/documentation/corehid · https://developer.apple.com/forums/thread/763679 · https://support.apple.com/en-us/111099 · https://support.apple.com/en-us/111100 · https://support.apple.com/en-us/111101 · https://discourse.libsdl.org/t/sdl-detect-logitech-g923-playstation-as-wheel-c8d1d/45862 · https://github.com/berarma/new-lg4ff · https://github.com/mescon/logitech-trueforce-linux-driver/wiki/G923 · https://raw.githubusercontent.com/torvalds/linux/master/drivers/hid/hid-ids.h · https://support.logi.com/hc/en-ch/articles/360051800793 · https://developer.apple.com/documentation/driverkit/creating-drivers-for-ipados · https://developer.apple.com/documentation/usbdriverkit · https://developer.apple.com/documentation/hiddriverkit · https://developer.apple.com/documentation/driverkit/requesting-entitlements-for-driverkit-development · https://developer.apple.com/videos/play/wwdc2022/110373/ · https://developer.apple.com/forums/thread/842748 · https://developer.apple.com/forums/thread/751490 · https://raw.githubusercontent.com/WebKit/WebKit/main/Source/WebKit/UIProcess/Gamepad/cocoa/UIGamepadProviderCocoa.mm · https://webkit.org/blog/17333/webkit-features-in-safari-26-0/

---

## 3. Engine evaluation

Current versions on 2026-11-14:
- Unreal Engine 5.8.1 (installed; Epic's docs are on 5.9)
- Unity 6.3 LTS (6.7 LTS in beta)
- Godot 4.7 stable (4.8 in development)

### 3.1 Unreal Engine 5.8

- **[Official]** "A Mac computer is required to make signed builds for C++ projects." Remote Build connects Windows to a Mac over SSH.
- **[Official]** Blueprint-only projects can still be packaged from Windows, but they rely on Apple's Windows Metal shader compiler. Epic's compatibility table stops at UE 5.7, so **5.8 is unverified**.
- **[Official]** Blueprint-only also rules out a custom C++ vehicle model, and Blueprints are binary assets, which suits our text-based agent workflow poorly.
- **[Official]** On the mobile renderers, **Nanite, Lumen (software and hardware), Virtual Shadow Maps and hardware ray-tracing features are all "No"**. "Lumen currently does not work on iOS/tvOS/iPadOS devices." The desktop renderer on iOS is Beta (SM5).
- **[Community]** An experimental "Metal Mobile SM6" renderer exists in 5.8. One report says it crashes at startup on an M5 iPad.
- **[Official]** 5.8 needs Xcode 26.1.1 and iPadOS 17+. iOS Simulator support was removed in 5.8.1.
- **Verdict:** the best PC visuals, but the features that make Unreal look like Unreal don't run on iPad. Without a Mac it **fails the "proven path to the iPad" gate**.

### 3.2 Unity 6.3 LTS (URP)

- **[Official]** The Windows editor produces an **Xcode project**. Building the `.ipa` needs macOS and Xcode, either locally or in the cloud: Unity Build Automation (free tier of 100 Mac minutes a month) or any macOS CI.
- **[Official]** URP supports Metal, with these features:
  - Forward+ ("low performance impact" on mobile)
  - Deferred / Deferred+ (6.1+)
  - **GPU Resident Drawer** and **GPU occlusion culling** (need Forward+ and compute, which Metal has)
  - **STP upscaler** (SM5.0 or above, so fine on Metal)
  - SSAO, Adaptive Probe Volumes, Render Graph
- **[Official]** HDRP doesn't support iOS, so URP is the pipeline.
- **[Official]** Input System on iOS: MFi, Xbox and PlayStation controllers through GameController. **No raw HID or joystick devices on iOS.** HID is supported on Windows and macOS, which helps the PC build and the wheel bridge.
- **[Official]** Native plugins in `Assets/Plugins/iOS` (Objective-C, or Swift via `@_cdecl`) are integrated automatically. This is how we read thermal state, memory and other iOS-only APIs.
- **[Official]** The Profiler streams from an iOS development build over Wi-Fi (ports 54998–55511). We expect that to work from the Windows editor, but it is **[unverified]** until tested.
- **[Official]** Licensing:
  - Unity Personal is free up to $200k revenue/funding.
  - The splash screen is optional in Unity 6.
  - The Runtime Fee is cancelled.
- **Verdict:** the only option with a documented path from a Windows editor to an iPad build. It has a mature Metal pipeline and is fully scriptable in C# for custom physics.

### 3.3 Godot 4.7

- **[Official]**
  - Native Metal since 4.4 (Apple Silicon and iOS).
  - The Mobile renderer lacks SDFGI, VoxelGI and volumetric fog.
  - "You must export for iOS from a computer running macOS."
  - C# on iOS is **experimental**.
  - Jolt is the default physics engine from 4.6.
  - Texture streaming exists only in the 4.8 development builds.
  - Occlusion culling is CPU-based (Embree).
- **Verdict:** best openness and text workflow, but the weakest photorealism and open-world tooling, and the export step still needs macOS.

### 3.4 Native Metal

- **[Official]** Apple's Windows Metal tools compile shaders, but the app binary, signing, the Metal debugger and Instruments all need macOS.
- **Verdict:** a solo custom photorealistic engine is not feasible.

### 3.5 Scorecard (1 = poor, 5 = excellent, for this project)

| Criterion | Unity 6.3 URP | Unreal 5.8 | Godot 4.7 | Native Metal |
|---|---|---|---|---|
| Realistic vehicle physics (custom model) | 5 (C#/Burst) | 4 (needs C++ → Mac) | 3 (GDScript slow; C# iOS experimental) | 5 (but all from scratch) |
| Photorealism *on iPad* | 4 | 3 (mobile renderer) | 3 | 3 (engine-limited) |
| iPadOS / Apple Silicon support | 5 | 3 (desktop renderer Beta) | 4 | 5 |
| Performance tooling on M5 | 4 | 4 | 3 | 5 |
| USB HID / wheel | 2 (same OS limits for all) | 2 | 2 | 2 |
| Asset workflow | 4 | 5 | 3 | 1 |
| Memory / streaming | 4 (Addressables, mip streaming) | 4 (World Partition) | 2 | 2 |
| Dev complexity (solo + agent) | 4 | 2 | 4 | 1 |
| Build from Windows → TestFlight | 4 (Xcode project + cloud Mac) | 1 (needs a Mac) | 3 (export needs macOS) | 2 |
| Free tools/assets | 4 | 5 | 4 | 2 |
| Long-term maintainability | 4 | 4 | 4 | 2 |
| **Total** | **44** | **37** | **35** | **30** |

Sources: https://dev.epicgames.com/documentation/unreal-engine/ios-ipados-and-tvos-in-unreal-engine · https://dev.epicgames.com/documentation/unreal-engine/creating-remote-builds-of-unreal-engine-projects-for-ios · https://dev.epicgames.com/documentation/unreal-engine/using-the-windows-metal-shader-compiler-for-ios-in-unreal-engine · https://dev.epicgames.com/documentation/unreal-engine/ios-ipados-and-tvos-development-requirements-for-unreal-engine · https://dev.epicgames.com/documentation/unreal-engine/rendering-features-reference · https://dev.epicgames.com/documentation/en-us/unreal-engine/using-lumen-global-illumination-on-mobile-in-unreal-engine · https://dev.epicgames.com/documentation/unreal-engine/using-the-desktop-renderer-on-mobile-in-unreal-engine · https://forums.unrealengine.com/t/ue-5-8-enabling-metal-mobile-sm6-renderer-experimental-fails-because-libmetalirconverter-embeddedframework-zip-cannot-be-found/2735797 · https://docs.unity.com/en-us/engine/6000.3/manual/platform-specific/iphone/getting-started/ios-environment-setup · https://docs.unity.com/en-us/engine/6000.3/manual/render-pipelines/universal-render-pipeline/requirements · https://docs.unity.com/en-us/engine/6000.3/manual/analysis/graphics-performance-profiling/in-urp/reduce-draw-calls-urp/reduce-rendering-work-on-cpu/gpu-resident-drawer · https://docs.unity.com/en-us/engine/6000.3/manual/render-pipelines/universal-render-pipeline/introduction/urp-concepts/rendering-paths/comparison · https://docs.unity3d.com/Manual/urp/stp/stp-upscaler.html · https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.3/manual/System-Requirements.html · https://docs.unity3d.com/Packages/com.unity.inputsystem@1.19/manual/HID.html · https://docs.unity.com/en-us/engine/current/manual/platform-specific/iphone/ios-developing/ios-testing-and-debugging/ios-profile-device · https://unity.com/products/pricing-updates · https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html · https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_ios.html · https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/index.html · https://godotengine.org/releases/4.6/ · https://godotengine.org/article/dev-snapshot-godot-4-8-dev-5/ · https://developer.apple.com/metal/tools/

---

## 4. Build, signing and distribution from Windows

### 4.1 Apple accounts

- **[Official] Apple Developer Program:** US$99 per membership year. **The user is already enrolled** (confirmed on 2026-11-14).
- **[Official] Free Personal Team:** up to 3 devices, 3 apps per device and 10 App IDs, with profiles that **expire after 7 days**. It has no TestFlight, no App Store Connect and no certificates portal. On-device testing goes *through Xcode*, which requires a Mac. *We don't need this path, because the membership is paid.*
- **[Official] TestFlight:**
  - Up to 100 internal testers. Internal builds need no beta review.
  - Each build can be tested for up to 90 days.
  - Uploads must be built with **Xcode 26 or later** (since 2026-04-28).
  - Builds must follow the App Review Guidelines.
- **[Official] Developer Mode** on the iPad is only needed for Xcode or `.ipa` installs. It is **not needed for TestFlight**.

### 4.2 Cloud macOS (we have no Mac)

| Service | Free allowance | Notes |
|---|---|---|
| **GitHub Actions** | **Free for public repositories** (standard runners). Private repos: roughly 190–200 macOS minutes a month | arm64 runner: 3 CPUs (M1), 7 GB RAM, 14 GB documented disk. Labels include `macos-latest`, `macos-15`, `macos-26` (Xcode 26.0.1–26.6, default 26.6). Jobs run up to 6 hours. **Chosen: the repo is public.** |
| Codemagic | 500 macOS M2 min/month | 12 GB RAM, about 156 GB free disk. **Fallback** if the GitHub runner disk is too small for the Unity Xcode project |
| Unity Build Automation | 100 Mac min/month | Builds the Unity project directly |
| Xcode Cloud | 25 h/month with membership | Requires first-time setup *in Xcode on a Mac*, so it's ruled out |

### 4.3 Signing and upload without a Mac [Official, plus marked inferences]

- **App Store Connect API key (Team key, Admin role).** The `.p8` file can be downloaded only once. It enables 2FA-free automation of signing and upload. The user creates it in a browser on Windows.
- **Signing options in CI:**
  1. `xcodebuild` automatic signing with `-allowProvisioningUpdates` + the API key flags.
  2. fastlane `match`, with certificates in a separate private store, plus `upload_to_testflight`.
  - We start with option 1 and switch to option 2 if certificate churn becomes a problem.
- **Upload.** `xcodebuild -exportArchive` with `destination=upload`, or `xcrun altool --upload-package` (`--upload-app` is deprecated).
- **Certificates.** Apple documents generating the certificate request only on a Mac (Keychain Access). An OpenSSL request from Windows is a common but undocumented workaround. Letting the CI tooling create certificates through the API key avoids this.

### 4.4 Installing on the iPad from Windows

- **[Official]** Apple's Windows "Apple Devices" app doesn't install developer builds. Apple's ad hoc install tools are Mac-only.
- **→ TestFlight is the install route.**
- Third-party re-signers (Sideloadly, AltServer) exist, but they are not Apple-endorsed and broke after Apple server changes in 2026. **We won't depend on them.**

### 4.5 Unreal on cloud Macs

- **[Official]** UE needs at least 16 GB RAM on a Mac build host. GitHub's standard Mac runner has 7 GB RAM and 14 GB disk.
- **[Inference]** Combined with a full engine install on every run, this rules out UE iOS builds on hosted runners.

### 4.6 Licensing

| Item | Finding |
|---|---|
| OpenStreetMap (ODbL) | A game built from OSM data is generally a **Produced Work**, which we may license as we choose. **Attribution** must appear, for example "© OpenStreetMap contributors" linked to openstreetmap.org/copyright, in the credits or on the splash screen. If we publicly distribute a **modified OSM database** (for example our road graph), it must be offered under the ODbL. Plan: ship the road graph inside the game, and keep the extraction tools and derived data documented |
| Poly Haven | CC0, no credit required (we credit anyway) |
| ambientCG | CC0 1.0, commercial use allowed |
| Fab Standard License | Usable in any engine, but standalone redistribution isn't allowed. Legacy Quixel/Bridge "UE-only" licenses are a trap. **We don't use Megascans without a per-asset license check** |
| Real car brands | App Review guideline 5.2.1 forbids unlicensed third-party trademarks, and it applies to TestFlight too. **Use fictional brands and unbadged, altered designs** |

Sources: https://developer.apple.com/programs/whats-included/ · https://developer.apple.com/support/compare-memberships/ · https://developer.apple.com/help/app-store-connect/test-a-beta-version/testflight-overview · https://developer.apple.com/news/upcoming-requirements/ · https://developer.apple.com/documentation/xcode/enabling-developer-mode-on-a-device · https://docs.github.com/en/billing/concepts/product-billing/github-actions · https://docs.github.com/en/actions/reference/runners/github-hosted-runners · https://github.com/actions/runner-images · https://codemagic.io/pricing/ · https://unity.com/products/pricing-updates · https://developer.apple.com/xcode-cloud/get-started/ · https://developer.apple.com/documentation/appstoreconnectapi/creating-api-keys-for-app-store-connect-api · https://docs.fastlane.tools/app-store-connect-api/ · https://help.apple.com/asc/appsaltool/en.lproj/static.html · https://developer.apple.com/help/account/certificates/create-a-certificate-signing-request · https://support.apple.com/guide/devices-windows/welcome/windows · https://developer.apple.com/documentation/xcode/distributing-your-app-to-registered-devices · https://dev.epicgames.com/documentation/en-us/unreal-engine/macos-development-requirements-for-unreal-engine · https://osmfoundation.org/wiki/Licence/Licence_and_Legal_FAQ · https://opendatacommons.org/licenses/odbl/1-0/ · https://osmfoundation.org/wiki/Licence/Attribution_Guidelines · https://polyhaven.com/license · https://docs.ambientcg.com/license/ · https://www.fab.com/eula · https://developer.apple.com/app-store/review/guidelines/

---

## 5. Decisions taken (2026-11-14)

| Decision | Choice | Decided by |
|---|---|---|
| Engine | **Unity 6.3 LTS (6000.3.28f1), URP**. The user asked for "the best one for graphics that looks realistic but works on iPad, with optimization" | User delegated, recommendation applied |
| Project location | `E:\MASAR` (off OneDrive) | User |
| Apple Developer Program | Already enrolled (paid) | User |
| Source hosting / CI | **Public** GitHub repository + GitHub Actions macOS runners | User |
| Mac access | None. All Apple builds run in the cloud | User |
