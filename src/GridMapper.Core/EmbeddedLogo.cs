using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace GridMapper.Core;

// Immutable project-owned source. Style edits share this object instead of
// copying or revalidating a potentially large base64 image on every keystroke.
public sealed record EmbeddedLogo
{
    public const int MaxBytes = 8 * 1024 * 1024;
    public string DataBase64 { get; }
    public string FileName { get; }
    [JsonIgnore] public string Key { get; }
    public EmbeddedLogo(string dataBase64, string fileName)
    {
        ModelLimits.Require(dataBase64 is not null && dataBase64.Length is > 0 and <= 11_184_812
            && fileName is not null && fileName.Length <= 255 && !fileName.Any(char.IsControl), ModelError.InvalidLogo, "Недопустимый встроенный логотип.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(dataBase64!); }
        catch (FormatException) { throw new ModelValidationException(ModelError.InvalidLogo, "Повреждены данные встроенного логотипа."); }
        ModelLimits.Require(bytes.Length is > 0 and <= MaxBytes, ModelError.InvalidLogo, "Логотип должен быть файлом до 8 MiB.");
        DataBase64 = dataBase64!; FileName = fileName!; Key = "embedded:" + Convert.ToHexString(SHA256.HashData(bytes));
    }
}
