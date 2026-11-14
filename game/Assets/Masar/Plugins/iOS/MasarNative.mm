// MASAR native metrics for iOS / iPadOS.
// Exposes OS values Unity does not provide: thermal state, available memory,
// process memory footprint and Low Power Mode. Public APIs only.

#import <Foundation/Foundation.h>
#include <os/proc.h>
#include <mach/mach.h>

extern "C" {

int masar_thermal_state(void)
{
    // 0 nominal, 1 fair, 2 serious, 3 critical
    return (int)[[NSProcessInfo processInfo] thermalState];
}

long long masar_available_memory(void)
{
    // Bytes this process can still allocate before hitting its limit (iOS 13+).
    return (long long)os_proc_available_memory();
}

long long masar_phys_footprint(void)
{
    task_vm_info_data_t info;
    mach_msg_type_number_t count = TASK_VM_INFO_COUNT;
    kern_return_t kr = task_info(mach_task_self(), TASK_VM_INFO, (task_info_t)&info, &count);
    return kr == KERN_SUCCESS ? (long long)info.phys_footprint : -1;
}

int masar_low_power_mode(void)
{
    return [[NSProcessInfo processInfo] isLowPowerModeEnabled] ? 1 : 0;
}

}
