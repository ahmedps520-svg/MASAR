import Foundation
import GameController
import CoreHaptics
import SwiftUI

struct LogEntry: Identifiable {
    let id = UUID()
    let time: Date
    let text: String
}

struct ElementSnapshot: Identifiable {
    let id: String          // element key in physicalInputProfile
    let kind: String        // button / axis / dpad / touchpad / other
    let value: String       // live value
    let range: String       // min..max seen since connect (calibration aid)
    let aliases: String
}

struct ControllerSnapshot: Identifiable {
    let id: ObjectIdentifier
    let vendorName: String
    let productCategory: String
    let profileKind: String
    let attachedToDevice: Bool
    let haptics: String
    let battery: String
    let elements: [ElementSnapshot]
}

/// Polls GameController-framework devices and reports exactly what is exposed.
@MainActor
final class ProbeModel: ObservableObject {
    @Published var controllers: [ControllerSnapshot] = []
    @Published var log: [LogEntry] = []
    @Published var system = SystemSnapshot.current()
    @Published var keyboardConnected = false
    @Published var mouseCount = 0
    @Published var racingWheelClassPresent = false

    private var timer: Timer?
    private var systemTimer: Timer?
    private var observers: [NSObjectProtocol] = []
    private var ranges: [String: (Float, Float)] = [:]
    private var hapticEngines: [ObjectIdentifier: CHHapticEngine] = [:]
    private var started = false

    func start() {
        guard !started else { return }
        started = true

        addLog("Probe started on \(system.modelIdentifier), \(system.systemVersion)")

        // Runtime class lookup instead of a typed reference, so the probe compiles
        // on every SDK. It only tells us whether the class exists in this OS build.
        racingWheelClassPresent = NSClassFromString("GCRacingWheel") != nil
        addLog("GCRacingWheel class present at runtime: \(racingWheelClassPresent)")

        GCController.shouldMonitorBackgroundEvents = true

        let nc = NotificationCenter.default
        observers.append(nc.addObserver(forName: .GCControllerDidConnect, object: nil, queue: .main) { [weak self] note in
            let c = note.object as? GCController
            let text = "CONNECT controller: \(c?.vendorName ?? "nil") | category: \(c?.productCategory ?? "nil")"
            Task { @MainActor in self?.addLog(text); self?.refresh() }
        })
        observers.append(nc.addObserver(forName: .GCControllerDidDisconnect, object: nil, queue: .main) { [weak self] note in
            let c = note.object as? GCController
            let text = "DISCONNECT controller: \(c?.vendorName ?? "nil")"
            Task { @MainActor in self?.addLog(text); self?.refresh() }
        })
        observers.append(nc.addObserver(forName: .GCKeyboardDidConnect, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in self?.addLog("CONNECT keyboard"); self?.refresh() }
        })
        observers.append(nc.addObserver(forName: .GCKeyboardDidDisconnect, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in self?.addLog("DISCONNECT keyboard"); self?.refresh() }
        })
        observers.append(nc.addObserver(forName: .GCMouseDidConnect, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in self?.addLog("CONNECT mouse/pointer device"); self?.refresh() }
        })
        observers.append(nc.addObserver(forName: .GCMouseDidDisconnect, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in self?.addLog("DISCONNECT mouse/pointer device"); self?.refresh() }
        })
        observers.append(nc.addObserver(forName: ProcessInfo.thermalStateDidChangeNotification, object: nil, queue: .main) { [weak self] _ in
            let s = SystemSnapshot.describe(ProcessInfo.processInfo.thermalState)
            Task { @MainActor in self?.addLog("THERMAL state -> \(s)") }
        })

        // Also covers Bluetooth controllers that are in pairing mode.
        GCController.startWirelessControllerDiscovery(completionHandler: nil)

        refresh()
        timer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30.0, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.refresh() }
        }
        systemTimer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.system = SystemSnapshot.current() }
        }
    }

    func addLog(_ text: String) {
        log.insert(LogEntry(time: Date(), text: text), at: 0)
        if log.count > 500 { log.removeLast(log.count - 500) }
    }

    func resetRanges() {
        ranges.removeAll()
        addLog("Calibration ranges reset")
    }

    func refresh() {
        keyboardConnected = GCKeyboard.coalesced != nil
        mouseCount = GCMouse.mice().count
        controllers = GCController.controllers().map(snapshot)
    }

    private func snapshot(_ c: GCController) -> ControllerSnapshot {
        let profile = c.physicalInputProfile
        let profileKind: String
        if c.extendedGamepad != nil { profileKind = "extendedGamepad" }
        else if c.microGamepad != nil { profileKind = "microGamepad" }
        else { profileKind = "physicalInputProfile only" }

        let cid = ObjectIdentifier(c)
        var elements: [ElementSnapshot] = []
        for key in profile.elements.keys.sorted() {
            guard let el = profile.elements[key] else { continue }
            let aliases = el.aliases.sorted().joined(separator: ", ")
            let rangeKey = "\(cid.hashValue)|\(key)"
            if let dpad = el as? GCControllerDirectionPad {
                track(rangeKey + ".x", dpad.xAxis.value)
                track(rangeKey + ".y", dpad.yAxis.value)
                elements.append(ElementSnapshot(
                    id: key, kind: "dpad",
                    value: String(format: "x %+.3f  y %+.3f", dpad.xAxis.value, dpad.yAxis.value),
                    range: "x \(rangeText(rangeKey + ".x"))  y \(rangeText(rangeKey + ".y"))",
                    aliases: aliases))
            } else if let axis = el as? GCControllerAxisInput {
                track(rangeKey, axis.value)
                elements.append(ElementSnapshot(
                    id: key, kind: "axis",
                    value: String(format: "%+.4f", axis.value),
                    range: rangeText(rangeKey), aliases: aliases))
            } else if let button = el as? GCControllerButtonInput {
                track(rangeKey, button.value)
                elements.append(ElementSnapshot(
                    id: key, kind: button.isAnalog ? "button (analog)" : "button",
                    value: String(format: "%.4f %@", button.value, button.isPressed ? "PRESSED" : ""),
                    range: rangeText(rangeKey), aliases: aliases))
            } else {
                elements.append(ElementSnapshot(
                    id: key, kind: String(describing: type(of: el)),
                    value: "-", range: "-", aliases: aliases))
            }
        }

        var hapticsText = "none (controller.haptics is nil)"
        if let h = c.haptics {
            let locs = h.supportedLocalities.map { $0.rawValue }.sorted().joined(separator: ", ")
            hapticsText = "localities: \(locs.isEmpty ? "(empty)" : locs)"
        }

        var batteryText = "not reported"
        if let b = c.battery {
            batteryText = String(format: "%.0f%% state=%ld", b.batteryLevel * 100, b.batteryState.rawValue)
        }

        return ControllerSnapshot(
            id: cid,
            vendorName: c.vendorName ?? "(nil)",
            productCategory: c.productCategory,
            profileKind: profileKind,
            attachedToDevice: c.isAttachedToDevice,
            haptics: hapticsText,
            battery: batteryText,
            elements: elements)
    }

    private func track(_ key: String, _ v: Float) {
        if let r = ranges[key] { ranges[key] = (min(r.0, v), max(r.1, v)) } else { ranges[key] = (v, v) }
    }

    private func rangeText(_ key: String) -> String {
        guard let r = ranges[key] else { return "-" }
        return String(format: "[%+.3f … %+.3f]", r.0, r.1)
    }

    /// Plays a short pulse through the controller's own haptics engine, only if
    /// the OS exposes one. The result is logged truthfully, including failure.
    func testHaptics(_ id: ObjectIdentifier) {
        guard let c = GCController.controllers().first(where: { ObjectIdentifier($0) == id }) else { return }
        guard let haptics = c.haptics else {
            addLog("HAPTICS: \(c.vendorName ?? "?") exposes no haptics API")
            return
        }
        do {
            let engine: CHHapticEngine
            if let existing = hapticEngines[id] {
                engine = existing
            } else {
                guard let created = haptics.createEngine(withLocality: .default) else {
                    addLog("HAPTICS: createEngine(.default) returned nil")
                    return
                }
                engine = created
                hapticEngines[id] = created
            }
            try engine.start()
            let event = CHHapticEvent(
                eventType: .hapticContinuous,
                parameters: [CHHapticEventParameter(parameterID: .hapticIntensity, value: 1.0),
                             CHHapticEventParameter(parameterID: .hapticSharpness, value: 0.5)],
                relativeTime: 0, duration: 0.6)
            let pattern = try CHHapticPattern(events: [event], parameters: [])
            let player = try engine.makePlayer(with: pattern)
            try player.start(atTime: CHHapticTimeImmediate)
            addLog("HAPTICS: pattern sent to \(c.vendorName ?? "?") (feel it to confirm it played)")
        } catch {
            addLog("HAPTICS error: \(error.localizedDescription)")
        }
    }

    /// Plain-text report for sharing back to the development team.
    func reportText() -> String {
        var s = "MASAR Input Probe report, \(ISO8601DateFormatter().string(from: Date()))\n\n"
        s += "Device: \(system.modelIdentifier) | \(system.systemVersion)\n"
        s += String(format: "RAM: %.1f GB | available to app: %.0f MB | footprint: %.0f MB\n",
                    system.physicalMemoryGB, system.availableMemoryMB, system.footprintMB)
        s += "Thermal: \(system.thermalState) | Low Power Mode: \(system.lowPowerMode)\n"
        s += "CPU cores: \(system.cpuCores) (active \(system.activeCpuCores))\n"
        s += "GPU: \(system.gpuName) | families: \(system.gpuFamilies) | raytracing: \(system.gpuRaytracing) | "
        s += "recommendedMaxWorkingSet: \(system.gpuWorkingSetMB) MB | unified: \(system.gpuUnifiedMemory)\n"
        s += "GCRacingWheel class present: \(racingWheelClassPresent)\n"
        s += "Keyboard connected: \(keyboardConnected) | mice: \(mouseCount)\n\n"
        s += "Controllers (\(controllers.count)):\n"
        for c in controllers {
            s += "- \(c.vendorName) | category: \(c.productCategory) | profile: \(c.profileKind) | "
            s += "attached: \(c.attachedToDevice) | haptics: \(c.haptics) | battery: \(c.battery)\n"
            for e in c.elements {
                s += "    \(e.id) [\(e.kind)] value=\(e.value) range=\(e.range) aliases=\(e.aliases)\n"
            }
        }
        s += "\nEvent log (newest first):\n"
        let f = DateFormatter()
        f.dateFormat = "HH:mm:ss.SSS"
        for entry in log { s += "\(f.string(from: entry.time))  \(entry.text)\n" }
        return s
    }
}
