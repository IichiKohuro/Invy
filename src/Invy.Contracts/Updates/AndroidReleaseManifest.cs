namespace Invy.Contracts.Updates;

/// <summary>
/// Манифест последнего Android-релиза: <c>/updates/android/latest.json</c>.
/// Создаётся при выпуске версии рядом с APK (<c>invy-0.2.0.apk</c>).
/// </summary>
/// <param name="VersionCode">versionCode APK: major*10000 + minor*100 + patch.</param>
/// <param name="VersionName">Версия для людей (SemVer).</param>
/// <param name="ApkFileName">Имя файла APK в каталоге <c>/updates/android/</c>.</param>
/// <param name="Sha256">SHA-256 APK в hex — проверяется после загрузки.</param>
/// <param name="Size">Размер APK в байтах.</param>
/// <param name="Notes">Что нового (Markdown), необязательно.</param>
public sealed record AndroidReleaseManifest(
    int VersionCode,
    string VersionName,
    string ApkFileName,
    string Sha256,
    long Size,
    string? Notes = null);
