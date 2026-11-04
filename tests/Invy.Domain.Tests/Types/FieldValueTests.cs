using Invy.Domain.Tags;
using Invy.Domain.Types;

namespace Invy.Domain.Tests.Types;

public class FieldValueTests
{
    private static readonly Guid TypeC = Guid.NewGuid();
    private static readonly Guid TypeA = Guid.NewGuid();
    private static readonly HashSet<Guid> Connectors = [TypeA, TypeC];

    [Fact]
    public void Required_field_must_have_value()
    {
        var field = Field(FieldDataType.Text, required: true);

        FieldValueValidator.Validate(field, null, Connectors).ShouldBe(["«Поле»: обязательное поле"]);
        FieldValueValidator.Validate(field, new FieldValue(field.Id, Text: "   "), Connectors).ShouldNotBeEmpty();
        FieldValueValidator.Validate(Field(FieldDataType.Text), null, Connectors).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(100.0, true)]
    [InlineData(100.5, false)]    // целое
    [InlineData(0.0, false)]      // меньше min
    [InlineData(10001.0, false)]  // больше max
    public void Integer_respects_whole_numbers_and_range(double number, bool valid)
    {
        var field = Field(FieldDataType.Integer, settings: new FieldSettings(Min: 1, Max: 10000));

        FieldValueValidator.Validate(field, new FieldValue(field.Id, Number: number), Connectors).Count.ShouldBe(valid ? 0 : 1);
    }

    [Fact]
    public void Value_of_wrong_kind_is_rejected()
    {
        var field = Field(FieldDataType.Integer);

        FieldValueValidator.Validate(field, new FieldValue(field.Id, Text: "сто"), Connectors).ShouldBe(["«Поле»: ожидается число"]);
    }

    [Fact]
    public void Choice_accepts_one_option_from_its_list()
    {
        var field = Field(FieldDataType.Choice);

        FieldValueValidator.Validate(field, new FieldValue(field.Id, OptionIds: [TypeC]), Connectors).ShouldBeEmpty();
        FieldValueValidator.Validate(field, new FieldValue(field.Id, OptionIds: [TypeC, TypeA]), Connectors).ShouldBe(["«Поле»: можно выбрать только один вариант"]);
        FieldValueValidator.Validate(field, new FieldValue(field.Id, OptionIds: [Guid.NewGuid()]), Connectors).ShouldBe(["«Поле»: вариант не из справочника поля"]);
        FieldValueValidator.Validate(Field(FieldDataType.MultiChoice), new FieldValue(field.Id, OptionIds: [TypeC, TypeA]), Connectors).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("https://example.com/manual.pdf", true)]
    [InlineData("example.com", false)]
    [InlineData("ftp://example.com", false)]
    public void Url_must_be_http_or_https(string text, bool valid)
    {
        var field = Field(FieldDataType.Url);

        FieldValueValidator.Validate(field, new FieldValue(field.Id, Text: text), Connectors).Count.ShouldBe(valid ? 0 : 1);
    }

    [Fact]
    public void Text_respects_length_and_pattern()
    {
        var field = Field(FieldDataType.Text, settings: new FieldSettings(MaxLength: 5, Pattern: "^[A-Z]+$"));

        FieldValueValidator.Validate(field, new FieldValue(field.Id, Text: "ABC"), Connectors).ShouldBeEmpty();
        FieldValueValidator.Validate(field, new FieldValue(field.Id, Text: "abc"), Connectors).ShouldBe(["«Поле»: значение не соответствует шаблону"]);
        FieldValueValidator.Validate(field, new FieldValue(field.Id, Text: "ABCDEF"), Connectors).ShouldBe(["«Поле»: не длиннее 5 символов"]);
        FieldValueValidator.IsValidPattern("[unclosed").ShouldBeFalse();
    }

    [Fact]
    public void Rows_round_trip_for_every_data_type()
    {
        var itemId = Guid.NewGuid();
        var cases = new (FieldDefinition Field, Func<Guid, FieldValue> Value)[]
        {
            (Field(FieldDataType.Text), id => new FieldValue(id, Text: "чёрный")),
            (Field(FieldDataType.Decimal, settings: new FieldSettings(Decimals: 1)), id => new FieldValue(id, Number: 1.5)),
            (Field(FieldDataType.Boolean), id => new FieldValue(id, Bool: true)),
            (Field(FieldDataType.Date), id => new FieldValue(id, Date: new DateOnly(2026, 11, 4))),
            (Field(FieldDataType.MultiChoice), id => new FieldValue(id, OptionIds: [TypeA, TypeC])),
            (Field(FieldDataType.ItemRef), id => new FieldValue(id, ItemId: itemId)),
        };

        foreach (var (field, make) in cases)
        {
            var value = make(field.Id);
            var restored = FieldValue.FromRows(field, value.ToRows(itemId, field));

            (restored with { OptionIds = null }).ShouldBe(value with { OptionIds = null }, field.DataType.ToString());
            (restored.OptionIds ?? []).ShouldBe(value.OptionIds ?? [], field.DataType.ToString());
        }
    }

    [Fact]
    public void Formats_values_for_people()
    {
        var length = Field(FieldDataType.Integer, unit: "см");
        var version = Field(FieldDataType.Choice);
        Func<Guid, string?> options = id => id == TypeC ? "Type-C" : "Type-A";

        FieldValueFormatter.Format(length, new FieldValue(length.Id, Number: 100), options, _ => null).ShouldBe("100 см");
        FieldValueFormatter.Format(length, new FieldValue(length.Id, Number: 100), options, _ => null, includeUnit: false).ShouldBe("100");
        FieldValueFormatter.Format(Field(FieldDataType.Decimal), new FieldValue(Guid.Empty, Number: 1.5), options, _ => null).ShouldBe("1,5");
        FieldValueFormatter.Format(version, new FieldValue(version.Id, OptionIds: [TypeA, TypeC]), options, _ => null).ShouldBe("Type-A, Type-C");
        FieldValueFormatter.Format(Field(FieldDataType.Boolean), new FieldValue(Guid.Empty, Bool: false), options, _ => null).ShouldBe("Нет");
        FieldValueFormatter.Format(Field(FieldDataType.Date), new FieldValue(Guid.Empty, Date: new DateOnly(2026, 1, 5)), options, _ => null).ShouldBe("05.01.2026");
        FieldValueFormatter.Format(length, null, options, _ => null).ShouldBeNull();
    }

    [Fact]
    public void Title_template_renders_cable_name()
    {
        var values = new Dictionary<string, string?> { ["Разъём 1"] = "Type-A", ["Разъём 2"] = "Type-C", ["Длина"] = "100" };

        TitleTemplate.Render("{Разъём 1} → {Разъём 2}, {длина} см", values).ShouldBe("Type-A → Type-C, 100 см");
        TitleTemplate.Render("{Разъём 1} → {Разъём 2}", new Dictionary<string, string?> { ["Разъём 1"] = "Type-A" }).ShouldBe("Type-A");
        TitleTemplate.Render("{Разъём 1} → {Разъём 2}", new Dictionary<string, string?>()).ShouldBeNull();
        TitleTemplate.FieldNames("{Разъём 1} → { Длина }").ShouldBe(["Разъём 1", "Длина"]);
    }

    [Fact]
    public void Tag_name_drops_hash_sign()
    {
        var tag = new Tag();

        tag.Rename(" #Зарядка ");

        tag.Name.ShouldBe("Зарядка");
        tag.NameNorm.ShouldBe("зарядка");
    }

    private static FieldDefinition Field(FieldDataType type, bool required = false, FieldSettings? settings = null, string? unit = null) =>
        new() { Name = "Поле", DataType = type, IsRequired = required, Settings = settings ?? FieldSettings.Empty, Unit = unit };
}
