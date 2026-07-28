mod audio;

use std::fs;
use std::path::PathBuf;

use serde_json::{Map, Value};

/// `%AppData%\reshot\settings.json`, the same file the C# app owns
/// (`Reshot.Core.ReshotPaths.SettingsFile`). Both write pretty camelCase JSON.
fn settings_file() -> PathBuf {
    let appdata = std::env::var("APPDATA").unwrap_or_default();
    PathBuf::from(appdata).join("reshot").join("settings.json")
}

fn user_dir(leaf: &str) -> String {
    let profile = std::env::var("USERPROFILE").unwrap_or_default();
    PathBuf::from(profile)
        .join(leaf)
        .join("reshot")
        .to_string_lossy()
        .into_owned()
}

/// Recursive object merge: values from `patch` win, keys only present in `base`
/// survive. This is what keeps the settings window forward-compatible, the C#
/// side may add keys this UI has never heard of, and saving must not drop them.
fn merge(base: &mut Value, patch: &Value) {
    match (base, patch) {
        (Value::Object(base_map), Value::Object(patch_map)) => {
            for (key, patch_value) in patch_map {
                match base_map.get_mut(key) {
                    Some(base_value) => merge(base_value, patch_value),
                    None => {
                        base_map.insert(key.clone(), patch_value.clone());
                    }
                }
            }
        }
        (base_slot, patch_value) => *base_slot = patch_value.clone(),
    }
}

#[tauri::command]
fn load_settings() -> Result<Value, String> {
    let path = settings_file();
    if !path.exists() {
        // First run, or the C# app has never started. Hand back an empty object
        // and let the frontend fall back to its documented defaults (SPEC §13).
        return Ok(Value::Object(Map::new()));
    }
    let text = fs::read_to_string(&path).map_err(|e| format!("read {}: {e}", path.display()))?;
    serde_json::from_str(&text).map_err(|e| format!("parse {}: {e}", path.display()))
}

/// Path-explicit body of [`save_settings`], kept separate so the merge-on-write
/// behaviour can be tested without touching the real `%AppData%` file.
fn save_into(path: &std::path::Path, patch: &Value) -> Result<(), String> {
    if let Some(dir) = path.parent() {
        fs::create_dir_all(dir).map_err(|e| format!("create {}: {e}", dir.display()))?;
    }

    let mut current: Value = match fs::read_to_string(path) {
        Ok(text) => serde_json::from_str(&text).unwrap_or_else(|_| Value::Object(Map::new())),
        Err(_) => Value::Object(Map::new()),
    };
    merge(&mut current, patch);

    let text = serde_json::to_string_pretty(&current).map_err(|e| e.to_string())?;
    fs::write(path, text).map_err(|e| format!("write {}: {e}", path.display()))
}

#[tauri::command]
fn save_settings(patch: Value) -> Result<(), String> {
    save_into(&settings_file(), &patch)
}

#[tauri::command]
fn settings_path() -> String {
    settings_file().to_string_lossy().into_owned()
}

/// Per-user output folders, used only to prefill empty path fields.
#[tauri::command]
fn default_dirs() -> Value {
    serde_json::json!({
        "screenshots": user_dir("Pictures"),
        "videos": user_dir("Videos"),
        "records": user_dir("Music"),
    })
}

/// Active capture endpoints, as `{ id, name }`. `id` is the value written to
/// `audio.micDevice`; the sentinel `"default"` is added by the frontend.
#[tauri::command]
fn list_microphones() -> Vec<audio::Microphone> {
    audio::list()
}

/// Rounds the window frame itself.
///
/// The acrylic backdrop is painted by DWM over the whole window rectangle, so a
/// CSS `border-radius` alone leaves square blurred wedges poking out past the
/// panel's rounded corners. Asking DWM to round the window clips the backdrop to
/// the same shape.
#[cfg(windows)]
fn round_corners(window: &tauri::WebviewWindow) {
    use windows::Win32::Foundation::HWND;
    use windows::Win32::Graphics::Dwm::{
        DwmSetWindowAttribute, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND,
    };

    let Ok(handle) = window.hwnd() else {
        return;
    };

    unsafe {
        let preference = DWMWCP_ROUND;
        let _ = DwmSetWindowAttribute(
            HWND(handle.0 as *mut core::ffi::c_void),
            DWMWA_WINDOW_CORNER_PREFERENCE,
            &preference as *const _ as *const core::ffi::c_void,
            std::mem::size_of_val(&preference) as u32,
        );
    }
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .setup(|app| {
            #[cfg(windows)]
            {
                use tauri::Manager;
                if let Some(window) = app.get_webview_window("main") {
                    round_corners(&window);
                }
            }
            Ok(())
        })
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![
            load_settings,
            save_settings,
            settings_path,
            default_dirs,
            list_microphones
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    /// The whole point of merging rather than overwriting: settings.json is co-owned
    /// with the C# app, which may hold keys this UI never renders.
    #[test]
    fn save_preserves_keys_the_ui_does_not_know_about() {
        let dir = std::env::temp_dir().join("reshot-merge-test-unknown");
        let _ = fs::remove_dir_all(&dir);
        let path = dir.join("settings.json");

        let on_disk = json!({
            "hotkey": "Home",
            "update": { "auto": true },
            "futureUnknownKey": "must-survive",
            "video": { "fps": 60, "corners": { "enabled": true, "color": "#FF0000" } }
        });
        fs::create_dir_all(&dir).unwrap();
        fs::write(&path, serde_json::to_string_pretty(&on_disk).unwrap()).unwrap();

        let patch = json!({
            "hotkey": "Ctrl+Shift+F9",
            "update": { "auto": false },
            "video": { "fps": 30, "corners": { "enabled": false, "color": "#FF0000" } }
        });
        save_into(&path, &patch).unwrap();

        let result: Value = serde_json::from_str(&fs::read_to_string(&path).unwrap()).unwrap();
        assert_eq!(result["futureUnknownKey"], "must-survive");
        assert_eq!(result["hotkey"], "Ctrl+Shift+F9");
        assert_eq!(result["update"]["auto"], false);
        assert_eq!(result["video"]["fps"], 30);
        assert_eq!(result["video"]["corners"]["enabled"], false);
        // A sibling the patch did not touch must keep its value.
        assert_eq!(result["video"]["corners"]["color"], "#FF0000");

        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn save_creates_the_file_when_missing() {
        let dir = std::env::temp_dir().join("reshot-merge-test-fresh");
        let _ = fs::remove_dir_all(&dir);
        let path = dir.join("settings.json");

        save_into(&path, &json!({ "hotkey": "PrtScn" })).unwrap();

        let result: Value = serde_json::from_str(&fs::read_to_string(&path).unwrap()).unwrap();
        assert_eq!(result["hotkey"], "PrtScn");

        let _ = fs::remove_dir_all(&dir);
    }

    /// A corrupt file must not block saving, the C# side has the same policy.
    #[test]
    fn save_recovers_from_corrupt_json() {
        let dir = std::env::temp_dir().join("reshot-merge-test-corrupt");
        let _ = fs::remove_dir_all(&dir);
        let path = dir.join("settings.json");
        fs::create_dir_all(&dir).unwrap();
        fs::write(&path, "{ this is not json").unwrap();

        save_into(&path, &json!({ "hotkey": "Home" })).unwrap();

        let result: Value = serde_json::from_str(&fs::read_to_string(&path).unwrap()).unwrap();
        assert_eq!(result["hotkey"], "Home");

        let _ = fs::remove_dir_all(&dir);
    }
}
