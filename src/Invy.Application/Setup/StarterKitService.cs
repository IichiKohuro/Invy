using Invy.Application.Abstractions;
using Invy.Contracts.Items;
using Invy.Domain.Common;
using Invy.Domain.Items;
using Invy.Domain.Lists;
using Invy.Domain.Text;
using Invy.Domain.Types;
using Microsoft.EntityFrameworkCore;

namespace Invy.Application.Setup;

/// <summary>
/// Стартовый набор типов и справочников (PLAN.md, 5.5): Место, Шкаф, Коробка, Пакет, Кабель, Переходник,
/// Зарядное устройство, Инструмент, Электроника, Расходники. Повторный вызов добавляет только недостающее.
/// </summary>
public sealed class StarterKitService(IInventoryDbContext db, IUnitOfWork unitOfWork)
{
    public async Task<StarterKitResult> ApplyAsync(CancellationToken cancellationToken)
    {
        var lists = await db.ValueLists.Include(l => l.Options).ToListAsync(cancellationToken);
        var typeNames = (await db.ItemTypes.Select(t => t.NameNorm).ToListAsync(cancellationToken)).ToHashSet();
        int listsCreated = 0, typesCreated = 0;

        ValueList List(string name, params string[] options)
        {
            var existing = lists.FirstOrDefault(l => l.NameNorm == TextNormalizer.Normalize(name));
            if (existing is not null)
            {
                return existing;
            }

            var list = new ValueList();
            list.Rename(name);
            for (var i = 0; i < options.Length; i++)
            {
                var option = new ValueListOption { ListId = list.Id, SortOrder = i };
                option.SetValue(options[i]);
                list.Options.Add(option);
            }

            db.ValueLists.Add(list);
            lists.Add(list);
            listsCreated++;
            return list;
        }

        void Type(string name, TypeRole role, string emoji, string? template, string? unit, params FieldDefinition[] fields)
        {
            if (!typeNames.Add(TextNormalizer.Normalize(name)))
            {
                return;
            }

            var type = new ItemType { Role = role, IconKind = IconKind.Emoji, IconValue = emoji, TitleTemplate = template, DefaultUnit = unit, SortOrder = typesCreated };
            type.Rename(name);
            for (var i = 0; i < fields.Length; i++)
            {
                fields[i].TypeId = type.Id;
                fields[i].SortOrder = i;
                type.Fields.Add(fields[i]);
            }

            db.ItemTypes.Add(type);
            typesCreated++;
        }

        static FieldDefinition F(string name, FieldDataType type, string? unit = null, ValueList? list = null, bool showInList = false, bool remember = false) =>
            new() { Name = name, DataType = type, Unit = unit, ValueListId = list?.Id, ShowInList = showInList, RememberLast = remember };

        var connectors = List(
            "Разъёмы", "Type-A", "Type-C", "Micro-B", "Mini-B", "Lightning", "HDMI", "Mini HDMI", "Micro HDMI", "DisplayPort", "Mini DisplayPort",
            "VGA", "DVI", "RJ45", "Jack 3.5 мм", "Jack 6.3 мм", "RCA", "Toslink", "SATA", "Molex", "DC 5.5×2.1", "IEC C13", "IEC C7", "Евровилка");
        var usb = List("Версия USB", "1.1", "2.0", "3.0", "3.1", "3.2", "USB4", "Thunderbolt 3", "Thunderbolt 4");
        var material = List("Материал", "Картон", "Пластик", "Дерево", "Металл", "Ткань");
        var size = List("Размер", "S", "M", "L", "XL");
        var bagKind = List("Вид пакета", "Zip-lock", "Обычный", "Вакуумный", "Мешочек");

        Type("Место", TypeRole.Place, "📍", null, null, F("Помещение", FieldDataType.Text), F("Этаж", FieldDataType.Integer));
        Type("Шкаф", TypeRole.Container, "🗄️", null, null, F("Полок", FieldDataType.Integer));
        Type(
            "Коробка", TypeRole.Container, "📦", null, null,
            F("Материал", FieldDataType.Choice, list: material, showInList: true),
            F("Цвет", FieldDataType.Text, showInList: true, remember: true),
            F("Размер", FieldDataType.Choice, list: size),
            F("Прозрачная", FieldDataType.Boolean),
            F("Надпись", FieldDataType.Text));
        Type(
            "Пакет", TypeRole.Container, "🛍️", null, null,
            F("Вид", FieldDataType.Choice, list: bagKind, showInList: true),
            F("Прозрачный", FieldDataType.Boolean),
            F("Цвет", FieldDataType.Text, remember: true));
        Type(
            "Кабель", TypeRole.Item, "🔌", "{Разъём 1} → {Разъём 2}, {Длина} см", "шт",
            F("Длина", FieldDataType.Integer, "см", showInList: true),
            F("Разъём 1", FieldDataType.Choice, list: connectors, showInList: true),
            F("Разъём 2", FieldDataType.Choice, list: connectors, showInList: true),
            F("Версия USB", FieldDataType.Choice, list: usb),
            F("Цвет", FieldDataType.Text, remember: true));
        Type(
            "Переходник", TypeRole.Item, "🔀", "{Вход} → {Выход}", "шт",
            F("Вход", FieldDataType.Choice, list: connectors, showInList: true),
            F("Выход", FieldDataType.Choice, list: connectors, showInList: true));
        Type(
            "Зарядное устройство", TypeRole.Item, "🔋", null, "шт",
            F("Мощность", FieldDataType.Integer, "Вт", showInList: true),
            F("Разъём", FieldDataType.Choice, list: connectors));
        Type("Инструмент", TypeRole.Item, "🛠️", null, "шт", F("Производитель", FieldDataType.Text, remember: true), F("Модель", FieldDataType.Text));
        Type(
            "Электроника", TypeRole.Item, "💻", null, "шт",
            F("Производитель", FieldDataType.Text, remember: true), F("Модель", FieldDataType.Text), F("Серийный номер", FieldDataType.Text));
        Type("Расходники", TypeRole.Item, "🧷", null, "шт");

        if (listsCreated + typesCreated > 0)
        {
            await unitOfWork.SaveAsync(Guid.CreateVersion7(), cancellationToken);
        }

        return new StarterKitResult(typesCreated, listsCreated);
    }
}
