# 0005. Android: хост Avalonia и самообновление APK

**Статус:** Принято с оговорками (спайк фазы 0, 2026-11-04) — сборка APK и работа на устройстве проверяются вручную и в CI, см. [чек-лист](../spikes/phase-0/checklist.md)

## Контекст

Android-клиент использует тот же UI-код, что и Desktop. Google Play не используется: новые версии раздаёт сервер Invy в домашней сети, приложение должно обновляться с минимумом действий.

## Решение

1. **Хост:** `MainApplication : AvaloniaAndroidApplication<App>` создаёт `App` с `AndroidHost` (`IAppHost`), `MainActivity : AvaloniaMainActivity`. Тот же `ShellViewModel` и те же View, что на Desktop.
2. **Платформа:** `net10.0-android`, minSdk 26 (Android 8.0), RID `android-arm64` (телефоны) и `android-x64` (эмулятор), APK (не AAB). `versionCode = major*10000 + minor*100 + patch` вычисляется из `VersionPrefix`.
3. **HTTP в домашней сети:** `network_security_config` разрешает незашифрованный трафик (Android 9+ по умолчанию запрещает). HTTPS с пиннингом — фаза 9.
4. **Общая абстракция обновлений** `IAppUpdater` (Client.Core): Desktop — Velopack, Android — APK. В настройках — «Сервер Invy» (адрес; порт 5470 подставляется сам) и «Обновления» (Проверить / Установить).
5. **Обновление Android** (`AndroidUpdateService`, `ApkInstaller`):
   - `{сервер}/updates/android/latest.json` (`AndroidReleaseManifest`: versionCode, versionName, apkFileName, sha256, size, notes) → сравнение с установленным versionCode;
   - загрузка APK в кэш приложения и проверка SHA-256 (подпись проверяет сам Android при установке);
   - установка через `PackageInstaller`-сессию (без FileProvider): Android 12+ — `SetRequireUserAction(NotRequired)`, Android 14+ — `SetRequestUpdateOwnership(true)`; при `PendingUserAction` показывается системное подтверждение;
   - первый раз Android просит разрешить Invy устанавливать приложения — открывается нужный экран настроек.
6. **Выпуск:** `tools/pack-android.ps1 -Version X` — сборка, `invy-X.apk` и `latest.json` (запись через временный файл) в каталог релизов сервера. Все версии подписываются одним ключом.
7. **`Invy.NoAndroid.slnf`** — фильтр решения для машин без Android SDK; `Invy.slnx` содержит все проекты.

## Проверка

| Что | Как | Результат |
|-----|-----|-----------|
| Компиляция C# под `net10.0-android` (API 36) со всеми анализаторами | Workload `android` 36.1.118 + заглушка каталога SDK, цель `Compile` | ✅ после исправления 3 расхождений с привязками (см. ниже) |
| Формат `latest.json` | Тест сериализации `AndroidReleaseManifest`; скрипт в PowerShell 7.6 | ✅ |
| Раздача `latest.json` (no-cache) и APK (immutable) сервером | `UpdateFeedTests` | ✅ |
| Сборка и подпись APK, тримминг в Release | Нужен Android SDK (`dl.google.com` недоступен из этой среды) | ⏳ CI и вручную |
| Запуск на телефоне, обновление, обновление без подтверждения на Android 12+ | — | ⏳ вручную на устройстве |

Найденные компиляцией особенности привязок .NET for Android 36:
- `PackageInstaller.SessionParams.SetRequireUserAction` принимает `int`, а константы объявлены перечислением `PackageInstallUserAction` (старые поля помечены `[Obsolete(error)]`);
- `PackageManager.PackageInfoFlags.Of(0)` неоднозначен — нужен `Of(0L)`;
- устаревшие API (`GetPackageInfo(string, int)`, `VersionCode`, `GetParcelableExtra(string)`) вызываются только в ветках для старых версий Android.

## Последствия

- Обновление без подтверждения возможно, только когда Invy — installer of record, то есть начиная со второго обновления, установленного самим приложением. Первая установка APK и первое обновление требуют подтверждения.
- Потеря ключа подписи = переустановка приложения на всех телефонах: keystore хранить в менеджере паролей и офлайн.
- Для сборки Android-проекта нужен Android SDK (ставится вместе с Visual Studio или Rider, либо `dotnet build -t:InstallAndroidDependencies`).
