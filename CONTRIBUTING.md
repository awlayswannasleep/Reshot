# Участие в разработке

## Что понадобится

- [.NET 8 SDK](https://dotnet.microsoft.com/download), Windows 10 2004+ x64
- [Node.js 18+](https://nodejs.org/) и [Rust](https://rustup.rs/) — только если трогаешь
  окно настроек (`src/reshot-tauri`)

```powershell
dotnet build reshot.sln -c Debug
dotnet test
dotnet run --project src/Reshot.App
```

## Устройство проекта

| Проект | Назначение |
|---|---|
| `src/Reshot.Core` | Ядро **без UI**: модель документа, история, настройки, хоткеи |
| `src/Reshot.Capture` | Единственное место, где живёт Windows.Graphics.Capture |
| `src/Reshot.Recording` | MP4 и M4A через Media Foundation и NAudio |
| `src/Reshot.App` | WPF-оболочка: трей, оверлей, OCR, экспорт |
| `src/reshot-tauri` | Окно настроек: Tauri 2, Rust, TypeScript |

Границы, которые стоит держать:

1. **`Reshot.Core` не знает про WPF.** Благодаря этому модель документа, история и
   настройки покрыты юнит-тестами без окон.
2. **Windows.Graphics.Capture живёт только в `Reshot.Capture`.** Остальной код видит
   интерфейс `IScreenCaptureService`.
3. **`settings.json` общий** для C# и окна настроек. Запись только слиянием, иначе одна
   сторона затрёт ключи другой.

## Стиль

- Комментарии объясняют **почему**, а не пересказывают код
- Документация и комментарии в репозитории — по-русски, интерфейс приложения — по-английски
- Новая логика в `Reshot.Core` — с тестами (`tests/Reshot.Core.Tests`)

## Проверка изменений

Запущенный `reshot.exe` держит свой файл, поэтому перед пересборкой его нужно закрыть:

```powershell
taskkill /IM reshot.exe /F
```

Оверлей — полноэкранное окно, и запускать его ради каждой проверки неудобно. То, что
можно проверить без него, лучше проверять без него: логика ядра закрыта юнит-тестами, а
разметку HUD можно отрисовать за пределами экрана, собрав кадр с
`VirtualLeft`/`VirtualTop = -30000`.

## Сборка релиза

См. [build/README.md](build/README.md).
