using Invy.Client.Core.Catalog;
using Invy.Client.Core.Session;
using Invy.Contracts.Catalog;
using Invy.Contracts.Items;

namespace Invy.Client.Tests.Support;

/// <summary>Пример из README: Дом → Чердак → Серая коробка → Прозрачный пакет → кабели; Сарай → ящик с инструментами.</summary>
public sealed record SampleData(Guid Home, Guid Attic, Guid Box, Guid Bag, Guid Cable, Guid Shed)
{
    public static async Task<SampleData> CreateAsync(ClientSession session)
    {
        var api = session.Api;
        var catalog = session.Catalog;

        async Task<Guid> Add(string type, Guid? parent, string? name, params (string Field, object Value)[] values)
        {
            var itemType = catalog.ActiveTypes.Single(t => t.Name == type);
            var fields = CatalogStore.EffectiveFields(itemType);
            var dtos = values.Select(v =>
            {
                var field = fields.Single(f => f.Name == v.Field);
                return v.Value switch
                {
                    int n => new FieldValueDto(field.Id, Number: n),
                    bool b => new FieldValueDto(field.Id, Bool: b),
                    string s when field.DataType is FieldDataType.Choice => new FieldValueDto(field.Id, OptionIds: [catalog.List(field.ValueListId)!.Options.Single(o => o.Value == s).Id]),
                    string s => new FieldValueDto(field.Id, Text: s),
                    _ => throw new ArgumentException(v.Field),
                };
            }).ToList();
            return (await api.CreateItemAsync(new SaveItemRequest(itemType.Id, parent, name, Values: dtos))).Id;
        }

        var home = await Add("Место", null, "Дом");
        var attic = await Add("Место", home, "Чердак");
        var box = await Add("Коробка", attic, "Серая коробка", ("Материал", "Картон"), ("Цвет", "серый"), ("Размер", "L"));
        var bag = await Add("Пакет", box, "Прозрачный пакет", ("Вид", "Zip-lock"), ("Прозрачный", true));
        var cable = await Add("Кабель", bag, null, ("Длина", 100), ("Разъём 1", "Type-A"), ("Разъём 2", "Type-C"), ("Версия USB", "3.0"), ("Цвет", "чёрный"));
        await Add("Кабель", bag, null, ("Длина", 200), ("Разъём 1", "HDMI"), ("Разъём 2", "HDMI"));
        await Add("Переходник", bag, null, ("Вход", "Type-C"), ("Выход", "Jack 3.5 мм"));
        var shed = await Add("Место", home, "Сарай");
        var toolbox = await Add("Коробка", shed, "Ящик с инструментами", ("Материал", "Металл"));
        await Add("Инструмент", toolbox, "Отвёртка крестовая PH2", ("Производитель", "Bosch"));
        await Add("Инструмент", null, "Рулетка 5 м");
        return new SampleData(home, attic, box, bag, cable, shed);
    }

    /// <summary>Картинка-«фото» для тестов: градиент и круг.</summary>
    public static byte[] Photo(SkiaSharp.SKColor from, SkiaSharp.SKColor to, int width = 640, int height = 480)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        using var gradient = new SkiaSharp.SKPaint
        {
            Shader = SkiaSharp.SKShader.CreateLinearGradient(new(0, 0), new(width, height), [from, to], SkiaSharp.SKShaderTileMode.Clamp),
        };
        canvas.DrawRect(0, 0, width, height, gradient);
        using var circle = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.White.WithAlpha(160), IsAntialias = true };
        canvas.DrawCircle(width * 0.6f, height * 0.45f, height * 0.25f, circle);
        using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}
