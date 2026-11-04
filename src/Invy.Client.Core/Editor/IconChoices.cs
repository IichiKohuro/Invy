namespace Invy.Client.Core.Editor;

/// <summary>Наборы для выбора иконки и цвета вещи или типа.</summary>
public static class IconChoices
{
    /// <summary>Эмодзи для домашних вещей: хранение, техника, инструменты, быт.</summary>
    public static IReadOnlyList<string> Emojis { get; } =
    [
        "📦", "🗃️", "🗄️", "🗂️", "📁", "🧳", "🎒", "👜", "💼", "🛍️", "🧺", "🪣",
        "🏠", "🏡", "🏚️", "🚪", "🛏️", "🛋️", "🪑", "🚗", "🚲", "⛺", "🧰", "🪜",
        "🔌", "🔋", "🪫", "💡", "🔦", "🕯️", "💻", "🖥️", "⌨️", "🖱️", "🖨️", "📱",
        "📷", "🎧", "🎮", "🕹️", "📺", "📻", "☎️", "⌚", "💾", "💿", "📀", "🔀",
        "🛠️", "🔧", "🔨", "🪛", "🪚", "⚙️", "🧲", "🔩", "🪝", "📏", "📐", "✂️",
        "🧪", "🧴", "🧻", "🧼", "🧽", "🧹", "🪥", "🪒", "💊", "🩹", "🩺", "🧷",
        "👕", "👖", "🧥", "👟", "🥾", "🧦", "🧤", "🧣", "🕶️", "👓", "💍", "🎩",
        "📚", "📖", "📒", "✏️", "🖊️", "📎", "📌", "🖍️", "🎨", "🧵", "🧶", "🪡",
        "🍽️", "🍴", "🥄", "🔪", "🫖", "☕", "🍷", "🧂", "🫙", "🥫", "🍯", "🧊",
        "🎁", "🎄", "🎈", "🧸", "🪀", "⚽", "🏀", "🎾", "🎣", "🛹", "🪴", "🌱",
        "🔑", "🗝️", "🔒", "🧯", "🧭", "🗺️", "💰", "💳", "📄", "🏷️", "⭐", "❤️",
    ];

    /// <summary>Символы Fluent (имена из набора иконок интерфейса).</summary>
    public static IReadOnlyList<string> Symbols { get; } =
    [
        "Folder", "OpenFolder", "ZipFolder", "Library", "Home", "MapPin", "Map", "Globe", "Earth", "Target",
        "Tag", "Bookmark", "Star", "Important", "Flag", "Pin", "Bookmarks", "Clipboard", "Document", "Page",
        "Camera", "Image", "Video", "Audio", "Microphone", "Phone", "CellPhone", "Keyboard", "Print", "Scan",
        "Wifi4", "Cloud", "Code", "Games", "Calculator", "Ruler", "Repair", "Setting", "Link", "Attach",
        "Mail", "Calendar", "Clock", "Alert", "Shop", "People", "Contact", "Emoji", "Highlight", "ColorFill",
    ];

    /// <summary>Цвета в духе акцентов Windows 11; <c>null</c> — без цвета.</summary>
    public static IReadOnlyList<string> Colors { get; } =
    [
        "#D13438", "#E3008C", "#B146C2", "#744DA9", "#0063B1", "#0078D4", "#0099BC", "#00B7C3",
        "#00B294", "#107C10", "#498205", "#8E8CD8", "#FFB900", "#F7630C", "#CA5010", "#7A7574",
    ];
}
