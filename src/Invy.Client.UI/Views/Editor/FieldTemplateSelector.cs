using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;
using Invy.Client.Core.Editor;

namespace Invy.Client.UI.Views.Editor;

/// <summary>
/// Шаблон поля формы по типу данных характеристики (<see cref="FieldEditorViewModel.TemplateKey"/>):
/// «Text», «Integer», «Choice»… Неизвестный тип — шаблон «Text».
/// </summary>
public sealed class FieldTemplateSelector : IDataTemplate
{
    [Content]
    public Dictionary<string, IDataTemplate> Templates { get; } = [];

    public Control? Build(object? param)
    {
        var key = (param as FieldEditorViewModel)?.TemplateKey ?? "Text";
        var template = Templates.GetValueOrDefault(key) ?? Templates.GetValueOrDefault("Text");
        return template?.Build(param);
    }

    public bool Match(object? data) => data is FieldEditorViewModel;
}
