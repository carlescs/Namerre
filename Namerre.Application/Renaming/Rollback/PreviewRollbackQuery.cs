using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Rollback;

public sealed record PreviewRollbackQuery(string Id, SelectionRequest Selection) : IRequest<SelectionPreview>;
