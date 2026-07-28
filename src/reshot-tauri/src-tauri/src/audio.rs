//! Microphone enumeration.
//!
//! The C# recorder stores `audio.micDevice` as an `MMDevice.ID` string (NAudio's
//! `MMDeviceEnumerator`). NAudio is a thin wrapper over WASAPI's
//! `IMMDeviceEnumerator`, so calling that same COM interface here yields byte-identical
//! IDs, anything else (e.g. matching on friendly names) would silently break the
//! device the user picked.

use serde::Serialize;

#[derive(Serialize)]
pub struct Microphone {
    pub id: String,
    pub name: String,
}

#[cfg(windows)]
pub fn list() -> Vec<Microphone> {
    use windows::Win32::Devices::FunctionDiscovery::PKEY_Device_FriendlyName;
    use windows::Win32::Media::Audio::{
        eCapture, IMMDeviceEnumerator, MMDeviceEnumerator, DEVICE_STATE_ACTIVE,
    };
    use windows::Win32::System::Com::{
        CoCreateInstance, CoInitializeEx, CoTaskMemFree, CLSCTX_ALL, COINIT_MULTITHREADED,
        STGM_READ,
    };

    let mut devices = Vec::new();

    unsafe {
        // The calling thread may already be in an apartment; a failed re-init is
        // benign, so the HRESULT is deliberately dropped rather than propagated.
        let _ = CoInitializeEx(None, COINIT_MULTITHREADED);

        let enumerator: IMMDeviceEnumerator =
            match CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL) {
                Ok(value) => value,
                Err(_) => return devices,
            };

        let collection = match enumerator.EnumAudioEndpoints(eCapture, DEVICE_STATE_ACTIVE) {
            Ok(value) => value,
            Err(_) => return devices,
        };

        let count = collection.GetCount().unwrap_or(0);
        for index in 0..count {
            let Ok(device) = collection.Item(index) else {
                continue;
            };
            let Ok(id) = device.GetId() else { continue };
            let id_string = id.to_string().unwrap_or_default();
            // GetId hands over a fresh COM allocation; the string above owns a copy.
            CoTaskMemFree(Some(id.0 as *const _));
            if id_string.is_empty() {
                continue;
            }

            // Friendly name is best-effort: a nameless endpoint is still selectable.
            // PROPVARIANT's Display goes through PropVariantToBSTR, which handles the
            // VT_LPWSTR this key returns, and the value frees itself on drop.
            let name = device
                .OpenPropertyStore(STGM_READ)
                .ok()
                .and_then(|store| store.GetValue(&PKEY_Device_FriendlyName).ok())
                .map(|value| value.to_string())
                .filter(|text| !text.is_empty())
                .unwrap_or_else(|| id_string.clone());

            devices.push(Microphone {
                id: id_string,
                name,
            });
        }
    }

    devices
}

#[cfg(not(windows))]
pub fn list() -> Vec<Microphone> {
    Vec::new()
}
