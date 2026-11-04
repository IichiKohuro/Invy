namespace Invy.Client.Core.Shell;

/// <summary>Пункт боковой навигации. <paramref name="Icon"/> — имя символа Fluent (например, <c>Home</c>).</summary>
public sealed record NavigationItem(string Key, string Title, string Icon);
