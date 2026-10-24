namespace Namerre.Application.Renaming;

public sealed record MetadataSelection(string FileId, string InstanceId, string InputPath, string OptionsHash,
    string ConfirmationToken);
