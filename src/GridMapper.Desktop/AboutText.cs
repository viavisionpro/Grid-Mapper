namespace GridMapper.Desktop;

// Publisher information is deliberately outside editable community translations.
internal static class AboutText
{
    internal static bool Russian => L.Language.Equals("Ru-ru", StringComparison.OrdinalIgnoreCase);
    internal static string Title => Russian ? "О Grid Mapper" : "About Grid Mapper";
    internal static string Close => Russian ? "Закрыть" : "Close";
    internal static string Licenses => Russian ? "Лицензии компонентов" : "Component licenses";
    internal static string Description => Russian
        ? "Офлайн-приложение для генерации LED тест-паттернов.\n\nНе требует доступа к сети. Все параметры применяются при вводе.\n\nПоля ввода данных имеют встроенную арифметику: Enter/Tab вычисляет выражение.\n\nКолесо или жест увеличения меняет масштаб. Средняя кнопка перемещает поле в Canvas."
        : "An offline application for generating LED test patterns.\n\nNo network access is required. All parameters apply as you type.\n\nInput fields support arithmetic expressions; Enter/Tab evaluates the expression.\n\nUse the mouse wheel or a pinch gesture to zoom. Middle-drag pans the Canvas workspace.";
    internal static string Components => (Russian
        ? "Компоненты: Avalonia и .NET — MIT; SkiaSharp/Skia, HarfBuzz и ANGLE — лицензии и уведомления в Licenses; Noto Sans / Noto Sans Mono — SIL OFL 1.1.\n\nNDI Runtime устанавливается отдельно и не входит в поставку. Передача NDI требует локальной сети.\n"
        : "Components: Avalonia and .NET — MIT; SkiaSharp/Skia, HarfBuzz and ANGLE — licenses and notices in Licenses; Noto Sans / Noto Sans Mono — SIL OFL 1.1.\n\nNDI Runtime is installed separately and is not bundled. NDI output requires a local network.\n")
        + "NDI® is a registered trademark of Vizrt NDI AB.";
}
