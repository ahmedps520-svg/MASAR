// MASAR native metrics for iOS / iPadOS.
// Exposes OS values Unity does not provide: thermal state, available memory,
// process memory footprint and Low Power Mode. Public APIs only.

#import <Foundation/Foundation.h>
#import <GameController/GameController.h>
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

// Everything the GameController framework exposes right now: the on-device equivalent of
// the input-probe app. Returned as a malloc'd UTF-8 JSON string (the runtime frees it).
const char* masar_controllers_json(void)
{
    @autoreleasepool {
        NSMutableArray* list = [NSMutableArray array];
        for (GCController* c in [GCController controllers]) {
            NSMutableDictionary* d = [NSMutableDictionary dictionary];
            d[@"vendorName"] = c.vendorName ?: @"";
            d[@"productCategory"] = c.productCategory ?: @"";
            d[@"attachedToDevice"] = @(c.isAttachedToDevice);
            d[@"extendedGamepad"] = @(c.extendedGamepad != nil);
            d[@"microGamepad"] = @(c.microGamepad != nil);
            d[@"haptics"] = @(c.haptics != nil);
            NSMutableArray* elements = [NSMutableArray array];
            NSDictionary* map = c.physicalInputProfile.elements;
            for (NSString* key in [map.allKeys sortedArrayUsingSelector:@selector(compare:)]) {
                GCControllerElement* el = map[key];
                NSString* kind = [el isKindOfClass:[GCControllerDirectionPad class]] ? @"dpad"
                               : [el isKindOfClass:[GCControllerAxisInput class]] ? @"axis"
                               : [el isKindOfClass:[GCControllerButtonInput class]] ? @"button" : @"other";
                [elements addObject:@{ @"name": key, @"kind": kind, @"analog": @(el.isAnalog) }];
            }
            d[@"elements"] = elements;
            [list addObject:d];
        }
        NSDictionary* root = @{
            @"controllers": list,
            @"racingWheelClassPresent": @(NSClassFromString(@"GCRacingWheel") != nil),
            @"keyboardConnected": @([GCKeyboard coalescedKeyboard] != nil),
            @"mouseCount": @([GCMouse mice].count)
        };
        NSData* data = [NSJSONSerialization dataWithJSONObject:root options:0 error:nil];
        NSString* s = data ? [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding] : @"{}";
        return strdup([s UTF8String]);
    }
}

}
