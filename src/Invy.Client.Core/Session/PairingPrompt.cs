using Invy.Contracts.Devices;

namespace Invy.Client.Core.Session;

/// <summary>Администратору: новое устройство просит доступ — разрешить (изменять или только смотреть) или отклонить.</summary>
public static class PairingPrompt
{
    public static async Task AskAsync(ClientSession session, PendingPairingDto request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        if (session.Connection.Me?.Role != DeviceRole.Admin)
        {
            return;
        }

        var platform = request.Platform switch
        {
            DevicePlatform.Windows => "Windows",
            DevicePlatform.Android => "Android",
            _ => "устройство",
        };
        var choice = await session.Dialogs.AskAsync(
            "Новое устройство просит доступ",
            $"«{request.DeviceName}» ({platform}) хочет подключиться к Invy. Код на его экране: {request.Code}.\nРазрешайте, только если код совпадает.",
            "Разрешить изменять",
            "Только просмотр",
            "Отклонить");

        await session.TryAsync(
            () => choice switch
            {
                DialogChoice.Primary => session.Api.ApprovePairingAsync(request.RequestId, new ApprovePairingRequest(DeviceRole.Editor)),
                DialogChoice.Secondary => session.Api.ApprovePairingAsync(request.RequestId, new ApprovePairingRequest(DeviceRole.Viewer)),
                _ => session.Api.DenyPairingAsync(request.RequestId),
            },
            "Запрос подключения");

        if (choice != DialogChoice.Cancel)
        {
            session.Dialogs.Notify($"«{request.DeviceName}» подключено", NotifyKind.Success);
        }
    }
}
