import SwiftUI

struct ContentView: View {
    @EnvironmentObject var model: ProbeModel

    var body: some View {
        NavigationStack {
            List {
                Section("How to use") {
                    Text("1. Plug in the Logitech G923 (USB-C adapter). 2. Turn the wheel lock-to-lock and press every pedal fully. 3. Press every button. 4. Unplug and re-plug once. 5. Tap Share report and send the text back.")
                        .font(.callout)
                }

                Section("Device") {
                    row("Model", model.system.modelIdentifier)
                    row("OS", model.system.systemVersion)
                    row("Thermal state", model.system.thermalState)
                    row("Low Power Mode", model.system.lowPowerMode ? "on" : "off")
                    row("Physical RAM", String(format: "%.1f GB", model.system.physicalMemoryGB))
                    row("Available to this app", String(format: "%.0f MB", model.system.availableMemoryMB))
                    row("App footprint", String(format: "%.0f MB", model.system.footprintMB))
                    row("CPU cores", "\(model.system.cpuCores) (active \(model.system.activeCpuCores))")
                    row("GPU", model.system.gpuName)
                    row("GPU families", model.system.gpuFamilies)
                    row("GPU ray tracing", model.system.gpuRaytracing)
                    row("GPU working set", "\(model.system.gpuWorkingSetMB) MB")
                }

                Section("Input APIs") {
                    row("GCRacingWheel class", model.racingWheelClassPresent ? "present" : "absent on this OS")
                    row("Keyboard", model.keyboardConnected ? "connected" : "none")
                    row("Mice / pointers", "\(model.mouseCount)")
                    row("Game controllers", "\(model.controllers.count)")
                    Button("Reset calibration ranges") { model.resetRanges() }
                }

                if model.controllers.isEmpty {
                    Section("Controllers") {
                        Text("No device is exposed through the GameController framework.")
                            .foregroundStyle(.secondary)
                    }
                }

                ForEach(model.controllers) { c in
                    Section("\(c.vendorName) · \(c.productCategory)") {
                        row("Profile", c.profileKind)
                        row("Attached to device", c.attachedToDevice ? "yes" : "no")
                        row("Haptics", c.haptics)
                        row("Battery", c.battery)
                        Button("Test haptics (only if exposed)") { model.testHaptics(c.id) }
                        ForEach(c.elements) { e in
                            VStack(alignment: .leading, spacing: 2) {
                                HStack {
                                    Text(e.id).font(.system(.body, design: .monospaced))
                                    Spacer()
                                    Text(e.value).font(.system(.body, design: .monospaced))
                                }
                                Text("\(e.kind) · seen \(e.range)")
                                    .font(.caption).foregroundStyle(.secondary)
                                if !e.aliases.isEmpty {
                                    Text("aliases: \(e.aliases)").font(.caption2).foregroundStyle(.secondary)
                                }
                            }
                        }
                    }
                }

                Section("Event log") {
                    ForEach(model.log.prefix(60)) { entry in
                        Text("\(entry.time.formatted(date: .omitted, time: .standard))  \(entry.text)")
                            .font(.system(.caption, design: .monospaced))
                    }
                }
            }
            .navigationTitle("MASAR Input Probe")
            .toolbar {
                ShareLink(item: model.reportText()) {
                    Label("Share report", systemImage: "square.and.arrow.up")
                }
            }
        }
    }

    private func row(_ title: String, _ value: String) -> some View {
        HStack {
            Text(title)
            Spacer()
            Text(value).foregroundStyle(.secondary).multilineTextAlignment(.trailing)
        }
    }
}
