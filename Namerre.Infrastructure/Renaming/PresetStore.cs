using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Presets;

namespace Namerre.Infrastructure.Renaming;

/// <summary>Keeps presets in the <c>Presets</c> table of the rename database.</summary>
public sealed class PresetStore(RenameStore store) : IPresetStore
{
    public IReadOnlyList<PresetRecord> List()
        => store.Use(db => db.Presets.AsNoTracking().AsEnumerable().Select(Record).ToArray());

    public PresetRecord Get(string id)
        => store.Use(db => db.Presets.AsNoTracking().SingleOrDefault(p => p.Id == id) is { } row
            ? Record(row) : throw NotFound());

    public PresetRecord? Find(string name)
        // Name uses the NOCASE collation, so this comparison ignores case like the unique index.
        => store.Use(db => db.Presets.AsNoTracking().SingleOrDefault(p => p.Name == name) is { } row ? Record(row) : null);

    public int Count() => store.Use(db => db.Presets.Count());

    public void Add(PresetRecord preset) => Save(db =>
    {
        db.Presets.Add(new RenamePresetRow
        {
            Id = preset.Id, Name = preset.Name, Json = Serialize(preset), CreatedAt = preset.CreatedAt,
            UpdatedAt = preset.UpdatedAt, Revision = 1
        });
    });

    public void Replace(PresetRecord preset) => Save(db =>
    {
        var row = db.Presets.SingleOrDefault(p => p.Id == preset.Id) ?? throw NotFound();
        row.Name = preset.Name;
        row.Json = Serialize(preset);
        row.UpdatedAt = preset.UpdatedAt;
        row.Revision++;
    });

    public void Delete(string id) => store.Use(db =>
    {
        if (db.Presets.Where(p => p.Id == id).ExecuteDelete() == 0) throw NotFound();
        return true;
    });

    private void Save(Action<RenameDbContext> change) => store.Use(db =>
    {
        change(db);
        try { db.SaveChanges(); }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            throw new RenameException("PresetNameTaken", "Another preset already has this name.", 409, innerException: exception);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new RenameException("PresetChanged", "The preset changed meanwhile. Reload it and try again.", 409,
                innerException: exception);
        }
        return true;
    });

    private static string Serialize(PresetRecord preset)
        => JsonSerializer.Serialize(new Content(preset.Plugins, preset.VerifyContent, preset.DeleteEmptyFolders,
            preset.OnCollision), RenameStore.Json);

    private static PresetRecord Record(RenamePresetRow row)
    {
        var content = JsonSerializer.Deserialize<Content>(row.Json, RenameStore.Json)!;
        return new(row.Id, row.Name, content.Plugins ?? [], content.VerifyContent, content.DeleteEmptyFolders,
            content.OnCollision, row.CreatedAt, row.UpdatedAt);
    }

    private static RenameException NotFound() => new("NotFound", "The requested resource was not found.", 404);

    private sealed record Content(IReadOnlyList<PluginInstance>? Plugins, bool VerifyContent, bool DeleteEmptyFolders,
        string? OnCollision);
}
