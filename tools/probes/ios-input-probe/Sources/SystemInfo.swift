import Foundation
import UIKit
import Metal
import os

/// Device facts read from public APIs. Anything the OS does not expose is
/// reported as "unavailable"; nothing is estimated.
struct SystemSnapshot {
    var modelIdentifier: String
    var systemVersion: String
    var thermalState: String
    var lowPowerMode: Bool
    var physicalMemoryGB: Double
    var availableMemoryMB: Double      // os_proc_available_memory()
    var footprintMB: Double            // task_vm_info.phys_footprint
    var cpuCores: Int
    var activeCpuCores: Int
    var gpuName: String
    var gpuFamilies: String
    var gpuRaytracing: String
    var gpuWorkingSetMB: String
    var gpuUnifiedMemory: String

    static func current() -> SystemSnapshot {
        let pi = ProcessInfo.processInfo
        let gpu = MTLCreateSystemDefaultDevice()
        return SystemSnapshot(
            modelIdentifier: Self.machineIdentifier(),
            systemVersion: "\(UIDevice.current.systemName) \(UIDevice.current.systemVersion)",
            thermalState: Self.describe(pi.thermalState),
            lowPowerMode: pi.isLowPowerModeEnabled,
            physicalMemoryGB: Double(pi.physicalMemory) / 1_073_741_824.0,
            availableMemoryMB: Double(os_proc_available_memory()) / 1_048_576.0,
            footprintMB: Double(Self.physFootprint()) / 1_048_576.0,
            cpuCores: pi.processorCount,
            activeCpuCores: pi.activeProcessorCount,
            gpuName: gpu?.name ?? "unavailable",
            gpuFamilies: Self.gpuFamilies(gpu),
            gpuRaytracing: gpu.map { $0.supportsRaytracing ? "yes" : "no" } ?? "unavailable",
            gpuWorkingSetMB: gpu.map { String(format: "%.0f", Double($0.recommendedMaxWorkingSetSize) / 1_048_576.0) } ?? "unavailable",
            gpuUnifiedMemory: gpu.map { $0.hasUnifiedMemory ? "yes" : "no" } ?? "unavailable"
        )
    }

    static func describe(_ s: ProcessInfo.ThermalState) -> String {
        switch s {
        case .nominal: return "nominal"
        case .fair: return "fair"
        case .serious: return "serious"
        case .critical: return "critical"
        @unknown default: return "unknown(\(s.rawValue))"
        }
    }

    static func machineIdentifier() -> String {
        var sys = utsname()
        uname(&sys)
        let mirror = Mirror(reflecting: sys.machine)
        return mirror.children.reduce(into: "") { acc, el in
            if let v = el.value as? Int8, v != 0 { acc.append(Character(UnicodeScalar(UInt8(bitPattern: v)))) }
        }
    }

    static func physFootprint() -> UInt64 {
        var info = task_vm_info_data_t()
        var count = mach_msg_type_number_t(MemoryLayout<task_vm_info_data_t>.size / MemoryLayout<integer_t>.size)
        let kr = withUnsafeMutablePointer(to: &info) { ptr in
            ptr.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                task_info(mach_task_self_, task_flavor_t(TASK_VM_INFO), $0, &count)
            }
        }
        return kr == KERN_SUCCESS ? info.phys_footprint : 0
    }

    static func gpuFamilies(_ gpu: MTLDevice?) -> String {
        guard let gpu else { return "unavailable" }
        var out: [String] = []
        if gpu.supportsFamily(.apple7) { out.append("apple7") }
        if gpu.supportsFamily(.apple8) { out.append("apple8") }
        if gpu.supportsFamily(.apple9) { out.append("apple9") }
        if gpu.supportsFamily(.metal3) { out.append("metal3") }
        return out.isEmpty ? "none reported" : out.joined(separator: ", ")
    }
}
