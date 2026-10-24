namespace Namerre.Application.Renaming;

public sealed class RenameException(string code, string message, int status = 400, string? fileId = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public string? FileId { get; } = fileId;
}
