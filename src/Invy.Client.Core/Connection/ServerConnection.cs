using System.Net;
using System.Net.Http.Headers;
using CommunityToolkit.Mvvm.ComponentModel;
using Invy.Client.Core.Api;
using Invy.Contracts;
using Invy.Contracts.Devices;
using Invy.Contracts.Server;

namespace Invy.Client.Core.Connection;

public enum ConnectionStatus
{
    Disconnected,
    Connecting,

    /// <summary>Ждём, пока администратор подтвердит запрос с кодом <see cref="ServerConnection.PairingCode"/>.</summary>
    WaitingForApproval,
    Connected,
    Failed,
}

/// <summary>
/// Подключение к серверу Invy одним действием: проверка сервера → сохранённый токен → режим «Доверенная сеть»
/// → запрос сопряжения с кодом и ожидание подтверждения. Токен сохраняется в защищённом виде.
/// </summary>
public sealed partial class ServerConnection(
    ISettingsStore settings,
    ITokenProtector protector,
    Func<HttpMessageHandler>? handlerFactory = null,
    string? deviceName = null,
    DevicePlatform platform = DevicePlatform.Windows,
    string appVersion = "0.0.0") : ObservableObject
{
    [ObservableProperty]
    public partial ConnectionStatus Status { get; private set; }

    [ObservableProperty]
    public partial string? PairingCode { get; private set; }

    [ObservableProperty]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    public partial ServerInfoDto? Server { get; private set; }

    [ObservableProperty]
    public partial CurrentDeviceDto? Me { get; private set; }

    public InvyApiClient? Api { get; private set; }

    public Uri? ServerUrl { get; private set; }

    public string? Token { get; private set; }

    /// <summary>Интервал опроса решения администратора.</summary>
    public TimeSpan PairingPollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public bool CanEdit => Me?.Role is DeviceRole.Editor or DeviceRole.Admin;

    /// <summary>«192.168.1.10» → <c>http://192.168.1.10:5470/</c>; HTTPS без порта — стандартный 443.</summary>
    public static Uri? ParseServerAddress(string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return null;
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "http://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps) || url.Host.Length == 0)
        {
            return null;
        }

        var authority = value[(value.IndexOf("://", StringComparison.Ordinal) + 3)..].Split('/')[0];
        var builder = new UriBuilder(url) { Path = "/" };
        if (url.Scheme == Uri.UriSchemeHttp && !authority.Contains(':', StringComparison.Ordinal))
        {
            builder.Port = ApiRoutes.DefaultHttpPort;
        }

        return builder.Uri;
    }

    public async Task<bool> ConnectAsync(Uri serverUrl, CancellationToken cancellationToken = default)
    {
        Status = ConnectionStatus.Connecting;
        Error = null;
        PairingCode = null;
        try
        {
            var anonymous = CreateApi(serverUrl, null);
            Server = await anonymous.GetServerInfoAsync(cancellationToken);

            // 1. Сохранённый токен этого же сервера.
            var saved = settings.Current;
            if (saved.ServerId == Server.ServerId && saved.ProtectedToken is { } protectedToken && protector.Unprotect(protectedToken) is { } token)
            {
                var api = CreateApi(serverUrl, token);
                try
                {
                    Me = await api.GetMeAsync(cancellationToken);
                    return Complete(api, serverUrl, token);
                }
                catch (ApiException e) when (e.Status == HttpStatusCode.Unauthorized)
                {
                    // доступ отозван — подключаемся заново
                }
            }

            // 2. «Доверенная сеть»: сервер пускает без токена.
            try
            {
                Me = await anonymous.GetMeAsync(cancellationToken);
                return Complete(anonymous, serverUrl, null);
            }
            catch (ApiException e) when (e.Status == HttpStatusCode.Unauthorized)
            {
                // нужно сопряжение
            }

            // 3. Сопряжение: показываем код и ждём администратора.
            var request = await anonymous.RequestPairingAsync(new PairingRequestCreate(deviceName ?? Environment.MachineName, platform, appVersion), cancellationToken);
            while (request.Status == PairingStatus.Pending)
            {
                PairingCode = request.Code;
                Status = ConnectionStatus.WaitingForApproval;
                await Task.Delay(PairingPollInterval, cancellationToken);
                request = await anonymous.GetPairingStatusAsync(request.RequestId, cancellationToken);
            }

            if (request.Status != PairingStatus.Approved || request.Token is null)
            {
                return Fail(request.Status == PairingStatus.Denied ? "Администратор отклонил подключение" : "Время ожидания подтверждения истекло — попробуйте снова");
            }

            var approved = CreateApi(serverUrl, request.Token);
            Me = await approved.GetMeAsync(cancellationToken);
            return Complete(approved, serverUrl, request.Token);
        }
        catch (OperationCanceledException)
        {
            Status = ConnectionStatus.Disconnected;
            PairingCode = null;
            return false;
        }
        catch (HttpRequestException ex)
        {
            return Fail($"Сервер {serverUrl.Authority} недоступен: {ex.Message}");
        }
        catch (ApiException ex)
        {
            return Fail(ex.UserMessage);
        }
    }

    /// <summary>HttpClient к серверу с токеном устройства (для API, фото, SignalR).</summary>
    public HttpClient CreateHttpClient(Uri serverUrl, string? token)
    {
        var http = handlerFactory is null ? new HttpClient() : new HttpClient(handlerFactory(), disposeHandler: true);
        http.BaseAddress = serverUrl;
        http.Timeout = TimeSpan.FromSeconds(100);
        if (token is not null)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return http;
    }

    public Func<HttpMessageHandler>? HandlerFactory => handlerFactory;

    private InvyApiClient CreateApi(Uri serverUrl, string? token) => new(CreateHttpClient(serverUrl, token));

    private bool Complete(InvyApiClient api, Uri serverUrl, string? token)
    {
        Api = api;
        ServerUrl = serverUrl;
        Token = token;
        PairingCode = null;
        settings.Save(settings.Current with
        {
            ServerUrl = serverUrl.ToString(),
            ServerId = Server?.ServerId,
            ProtectedToken = token is null ? null : protector.Protect(token),
        });
        Status = ConnectionStatus.Connected;
        OnPropertyChanged(nameof(CanEdit));
        return true;
    }

    private bool Fail(string message)
    {
        Error = message;
        PairingCode = null;
        Status = ConnectionStatus.Failed;
        return false;
    }
}
