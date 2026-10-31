namespace Namerre.Application.Renaming.Presets;

/// <summary>
/// Durable preset storage. Names are unique ignoring case: <see cref="Add"/> and <see cref="Replace"/> throw a
/// <see cref="RenameException"/> with code <c>PresetNameTaken</c> (409) for a taken name, and a missing preset is
/// <c>NotFound</c> (404).
/// </summary>
public interface IPresetStore
{
    IReadOnlyList<PresetRecord> List();
    PresetRecord Get(string id);
    /// <summary>Finds a preset by name, ignoring case; null when there is none.</summary>
    PresetRecord? Find(string name);
    int Count();
    void Add(PresetRecord preset);
    void Replace(PresetRecord preset);
    void Delete(string id);
}
