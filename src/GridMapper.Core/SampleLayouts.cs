namespace GridMapper.Core;

// Technical fixtures, not a product cabinet-profile library.
public static class SampleLayouts
{
    public static ScreenModel Regular() => ScreenModel.Regular("Экран 1",
        new(Guid.NewGuid(), "Контрольный 500×500", 500, 500, 128, 128), 2, 2);

    public static ScreenModel Mixed()
    {
        var square = new CabinetType(Guid.NewGuid(), "Контрольный 500×500", 500, 500, 128, 128);
        var tall = new CabinetType(Guid.NewGuid(), "Контрольный 500×1000", 500, 1000, 128, 256);
        return new(Guid.NewGuid(), "Смешанный экран", [square, tall],
            [new(Guid.NewGuid(), square.Id, 0, 0), new(Guid.NewGuid(), square.Id, 0, 128),
                new(Guid.NewGuid(), tall.Id, 128, 0)], ScreenStyle.Default);
    }

    public static ProjectModel Project()
    {
        var regular = Regular();
        var mixed = Mixed();
        return new(ProjectModel.CurrentFormatVersion, [regular, mixed], new(512, 256,
            [new(Guid.NewGuid(), regular.Id, 0, 0, false, false),
                new(Guid.NewGuid(), mixed.Id, 256, 0, false, false)]));
    }
}
