using System.Text.Json;

namespace StickyDesk.Core;

public sealed class ConfigurationService(AppPaths paths)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public List<string> Warnings { get; } = [];

    public AppConfiguration Load()
    {
        foreach (var candidate in new[] { paths.Config, paths.Config + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var configuration = JsonSerializer.Deserialize<AppConfiguration>(File.ReadAllText(candidate), Options)
                    ?? throw new JsonException("Пустая конфигурация.");
                if (configuration.Version != 1) throw new JsonException("Неподдерживаемая версия конфигурации.");
                configuration.Settings ??= new();
                configuration.Notes ??= [];
                configuration.Settings.AutosaveDelayMs = Math.Clamp(configuration.Settings.AutosaveDelayMs, 500, 1000);
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                configuration.Notes = configuration.Notes.Where(note =>
                {
                    if (note is null || !Guid.TryParseExact(note.Id, "N", out _) || string.IsNullOrWhiteSpace(note.FilePath)) return false;
                    try
                    {
                        if (!files.Add(paths.Resolve(note.FilePath)) || !ids.Add(note.Id)) return false;
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
                    note.Width = Math.Clamp(note.Width, 180, 3000);
                    note.Height = Math.Clamp(note.Height, 120, 3000);
                    if (string.IsNullOrWhiteSpace(note.Color)) note.Color = "#FFF2B3";
                    return true;
                }).ToList();
                if (candidate != paths.Config) Warnings.Add("Настройки восстановлены из резервной копии.");
                return configuration;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                paths.Log($"Configuration: {ex}");
                Warnings.Add($"Не удалось прочитать {Path.GetFileName(candidate)}: {ex.Message}");
                // Preserve the damaged input before a later save replaces it.
                try { File.Copy(candidate, candidate + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmssfff}", false); }
                catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException) { paths.Log(copyError.ToString()); }
            }
        }
        return new();
    }

    public void Save(AppConfiguration configuration) => AtomicFile.Write(paths.Config, System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(configuration, Options)));

    public void SaveCheckpoint(AppConfiguration configuration)
    {
        // Renamed files must remain reachable even if the main configuration is later corrupted.
        Save(configuration);
        Save(configuration);
    }
}
