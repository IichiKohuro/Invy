namespace Invy.Application.Errors;

/// <summary>Запись не найдена → 404.</summary>
public sealed class NotFoundException(string message) : Exception(message)
{
    public static NotFoundException For(string what, Guid id) => new($"{what} не найден(а): {id}");
}

/// <summary>
/// Запись изменена другим устройством после того, как клиент её загрузил → 409.
/// <see cref="Current"/> — актуальное состояние для диалога «Показать отличия / Перезаписать».
/// </summary>
public sealed class ConflictException(string message, object? current = null) : Exception(message)
{
    public object? Current { get; } = current;
}

/// <summary>Ошибки ввода по полям или нарушение правил (цикл в дереве, неверный родитель) → 422.</summary>
public sealed class ValidationException : Exception
{
    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("Проверьте введённые данные")
    {
        Errors = errors;
    }

    public ValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] })
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>Накопитель ошибок проверки: бросает <see cref="ValidationException"/>, если что-то найдено.</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = [];

    public bool HasErrors => _errors.Count > 0;

    public ValidationErrors Add(string field, string error)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            _errors[field] = list = [];
        }

        list.Add(error);
        return this;
    }

    public ValidationErrors AddRange(string field, IEnumerable<string> errors)
    {
        foreach (var error in errors)
        {
            Add(field, error);
        }

        return this;
    }

    public void ThrowIfAny()
    {
        if (HasErrors)
        {
            throw new ValidationException(_errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));
        }
    }
}
