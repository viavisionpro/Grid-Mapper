namespace GridMapper.Core;

public enum ModelError
{
    InvalidId, InvalidName, InvalidPhysicalSize, InvalidRasterSize, DensityMismatch,
    InvalidPattern, EmptyScreen, TooManyItems, NegativeCabinetPosition,
    DuplicateId, MissingCabinetType, CabinetOverlap, UnsupportedProjectVersion,
    MissingScreen, DuplicatePlacement, MissingPlacement, InvalidNumbering, InvalidInformation, InvalidText, InvalidFont, InvalidLogo, InvalidSnap
}

public sealed class ModelValidationException(ModelError code, string message) : ArgumentException(message)
{
    public ModelError Code { get; } = code;
}

public static class ModelLimits
{
    public const int MaxDimension = 16_384;
    public const long MaxPixelCount = 33_554_432;
    public const int MaxCabinets = 10_000;
    public const int MaxScreens = 1_000;
    public const decimal MinMillimeters = 0.001m;
    public const decimal MaxMillimeters = 1_000_000m;
    public const decimal DensityRelativeTolerance = 0.000001m;

    internal static void Require(bool condition, ModelError code, string message)
    {
        if (!condition) throw new ModelValidationException(code, message);
    }

    internal static void ValidateId(Guid id) =>
        Require(id != Guid.Empty, ModelError.InvalidId, "Объект должен иметь непустой ID.");

    public static void ValidateName(string? name) =>
        Require(!string.IsNullOrWhiteSpace(name) && name.Length <= 128 && !name.Any(char.IsControl),
            ModelError.InvalidName, "Имя должно содержать от 1 до 128 символов без управляющих символов.");

    public static void ValidateRaster(long width, long height) =>
        Require(width > 0 && height > 0 && width <= MaxDimension && height <= MaxDimension
            && width * height <= MaxPixelCount,
            ModelError.InvalidRasterSize, "Недопустимый размер растра: до 16 384 px на ось и 33 554 432 px в сумме.");

    // Canvas is a virtual composition, not a request for one full-size bitmap.
    public static void ValidateCanvas(long width, long height) =>
        Require(width > 0 && height > 0 && width <= MaxDimension && height <= MaxDimension,
            ModelError.InvalidRasterSize, "Размер Canvas: от 1 до 16384 px по каждой оси.");

    internal static bool SameDensity(decimal left, decimal right) =>
        Math.Abs(left - right) <= Math.Max(left, right) * DensityRelativeTolerance;
}
