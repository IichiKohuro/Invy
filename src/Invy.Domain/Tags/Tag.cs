using Invy.Domain.Common;
using Invy.Domain.Text;

namespace Invy.Domain.Tags;

public sealed class Tag : AggregateRoot
{
    public string Name { get; private set; } = string.Empty;

    public string NameNorm { get; private set; } = string.Empty;

    public string? Color { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Имя тега без «#» и лишних пробелов: «#Зарядка » → «Зарядка».</summary>
    public void Rename(string name)
    {
        Name = name.Trim().TrimStart('#').Trim();
        NameNorm = TextNormalizer.Normalize(Name);
    }
}
