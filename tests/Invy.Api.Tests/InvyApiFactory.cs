using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Invy.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Invy.Api.Tests;

/// <summary>Сервер Invy в памяти: своя БД во временном каталоге, режим авторизации задаётся тестом.</summary>
public class InvyApiFactory : WebApplicationFactory<Program>
{
    public string DataPath { get; } = Path.Combine(Path.GetTempPath(), "invy-api-tests", Guid.NewGuid().ToString("N"));

    protected virtual string AuthMode => "TrustedLan";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Invy:DataPath", DataPath);
        builder.UseSetting("Invy:Auth:Mode", AuthMode);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(DataPath))
        {
            Directory.Delete(DataPath, recursive: true);
        }
    }
}

/// <summary>Короткие вызовы API с source-generated JSON (как у настоящих клиентов).</summary>
internal static class HttpJson
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static JsonTypeInfo<T> Info<T>() => (JsonTypeInfo<T>)InvyJsonContext.Default.GetTypeInfo(typeof(T))!;

    public static async Task<T> GetJsonAsync<T>(this HttpClient http, string url) =>
        (await http.GetFromJsonAsync(url, Info<T>(), Ct))!;

    public static async Task<TResponse> PostJsonAsync<TRequest, TResponse>(this HttpClient http, string url, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(url, body, Info<TRequest>(), Ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync(Info<TResponse>(), Ct))!;
    }

    public static async Task<TResponse> PutJsonAsync<TRequest, TResponse>(this HttpClient http, string url, TRequest body)
    {
        using var response = await http.PutAsJsonAsync(url, body, Info<TRequest>(), Ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync(Info<TResponse>(), Ct))!;
    }

    /// <summary>Запрос, который должен завершиться ошибкой: код ответа и тело ProblemDetails.</summary>
    public static async Task<(int Status, JsonElement Problem)> SendExpectingErrorAsync(this HttpClient http, HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: InvyJsonContext.Default.Options);
        }

        using var response = await http.SendAsync(request, Ct);
        response.IsSuccessStatusCode.ShouldBeFalse($"{method} {url} должен был вернуть ошибку");
        var text = await response.Content.ReadAsStringAsync(Ct);
        return ((int)response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }
    }
}
