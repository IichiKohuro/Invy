using Avalonia.Data.Converters;
using FluentAvalonia.UI.Controls;
using Invy.Client.Core.Session;

namespace Invy.Client.UI.Converters;

public static class NoticeConverters
{
    /// <summary>Вид уведомления → цвет полосы <see cref="FAInfoBar"/>.</summary>
    public static FuncValueConverter<NotifyKind, FAInfoBarSeverity> Severity { get; } = new(kind => kind switch
    {
        NotifyKind.Success => FAInfoBarSeverity.Success,
        NotifyKind.Warning => FAInfoBarSeverity.Warning,
        NotifyKind.Error => FAInfoBarSeverity.Error,
        _ => FAInfoBarSeverity.Informational,
    });
}
