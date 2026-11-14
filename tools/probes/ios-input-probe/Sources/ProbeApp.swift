import SwiftUI

/// MASAR Input Probe: a diagnostic app for the target iPad.
///
/// It answers one question with evidence instead of assumptions:
/// "What does iPadOS actually expose to a third-party app when the
/// Logitech G923 (or any controller) is plugged in?"
///
/// It reports only what public APIs return. It does not fake devices,
/// values or force feedback.
@main
struct MasarInputProbeApp: App {
    @StateObject private var model = ProbeModel()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(model)
                .onAppear { model.start() }
        }
    }
}
