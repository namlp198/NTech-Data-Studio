using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Annotate;

/// <summary>
/// Applies edits to one image at a time. Each edit is written to the database
/// first, and only then to the in-memory document and the undo history, so a
/// failed write leaves everything as it was.
/// </summary>
public sealed class AnnotateService
{
    private const int MaxUndoPerImage = 200;

    private sealed class History
    {
        public readonly Stack<AnnotationEdit> Undo = new();
        public readonly Stack<AnnotationEdit> Redo = new();
    }

    private readonly IAnnotationStore _store;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<long, History> _histories = new();

    public AnnotateService(IAnnotationStore store, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public DateTimeOffset Now => _clock();

    public IReadOnlyList<AnnotateImage> LoadImages() => _store.LoadImages();

    public bool LoadSelectionOnly() => _store.LoadSelectionOnly();

    public void SetSelectionOnly(bool value) => _store.SetSelectionOnly(value);

    /// <summary>Not a label edit, so it stays out of undo.</summary>
    public void SetInDataset(long imageId, bool value) => _store.SetInDataset(imageId, value);

    public ImageContent LoadContent(long imageId) => _store.LoadContent(imageId);

    public ImageDocument Open(AnnotateImage image) => new(image, _store.LoadContent(image.Id));

    /// <summary>Undo stacks live as long as the screen is open, one per image.</summary>
    public void ClearHistory() => _histories.Clear();

    public bool CanUndo(long imageId) => _histories.TryGetValue(imageId, out var history) && history.Undo.Count > 0;

    public bool CanRedo(long imageId) => _histories.TryGetValue(imageId, out var history) && history.Redo.Count > 0;

    /// <returns>The edit that was applied, or null when it would change nothing.</returns>
    public AnnotationEdit? Execute(ImageDocument document, EditRequest request)
    {
        var tagsAdded = request.TagsAdded.Where(tag => !document.Tags.Contains(tag)).Distinct().ToList();
        var tagsRemoved = request.TagsRemoved.Where(document.Tags.Contains).Distinct().ToList();

        var afterIds = request.After.Select(annotation => annotation.Id).ToHashSet();
        var beforeIds = request.Before.Select(annotation => annotation.Id).ToHashSet();
        var existing = document.Annotations.Select(annotation => annotation.Id).ToHashSet();
        if (beforeIds.Any(id => !existing.Contains(id)))
            throw new InvalidOperationException("An edit refers to a label that is not on this image.");

        var changesLabels = request.Before.Count > 0 || request.After.Count > 0 || tagsAdded.Count > 0 || tagsRemoved.Count > 0;
        if (!changesLabels && request.ForcedStatus is null) return null;

        var remaining = document.Annotations.Count(annotation => !beforeIds.Contains(annotation.Id) || afterIds.Contains(annotation.Id))
            + request.After.Count(annotation => !existing.Contains(annotation.Id));
        var tagCount = document.Tags.Count + tagsAdded.Count - tagsRemoved.Count;
        var status = request.ForcedStatus
            ?? StatusRules.AfterContentEdit(document.Status, remaining + tagCount > 0);

        var edit = new AnnotationEdit(document.ImageId, request.Before, request.After, tagsAdded, tagsRemoved, document.Status, status);
        WriteAndApply(document, edit, forward: true);

        var history = HistoryOf(document.ImageId);
        history.Undo.Push(edit);
        history.Redo.Clear();
        if (history.Undo.Count > MaxUndoPerImage)
        {
            var kept = history.Undo.Take(MaxUndoPerImage).Reverse().ToList();
            history.Undo.Clear();
            foreach (var item in kept) history.Undo.Push(item);
        }

        return edit;
    }

    public AnnotationEdit? Undo(ImageDocument document)
    {
        var history = HistoryOf(document.ImageId);
        if (history.Undo.Count == 0) return null;

        var edit = history.Undo.Peek();
        WriteAndApply(document, edit, forward: false);
        history.Undo.Pop();
        history.Redo.Push(edit);
        return edit;
    }

    public AnnotationEdit? Redo(ImageDocument document)
    {
        var history = HistoryOf(document.ImageId);
        if (history.Redo.Count == 0) return null;

        var edit = history.Redo.Peek();
        WriteAndApply(document, edit, forward: true);
        history.Redo.Pop();
        history.Undo.Push(edit);
        return edit;
    }

    /// <returns>Null when the image has labels, which a Null image cannot have.</returns>
    public AnnotationEdit? ToggleNull(ImageDocument document) =>
        StatusRules.ToggleNull(document.Status, document.HasLabels) is { } status
            ? Execute(document, EditRequest.Status(status))
            : null;

    /// <returns>Null for an image with nothing to review yet.</returns>
    public AnnotationEdit? ToggleReviewed(ImageDocument document) =>
        StatusRules.ToggleReviewed(document.Status, document.HasLabels) is { } status
            ? Execute(document, EditRequest.Status(status))
            : null;

    private History HistoryOf(long imageId)
    {
        if (!_histories.TryGetValue(imageId, out var history))
            _histories[imageId] = history = new History();
        return history;
    }

    private void WriteAndApply(ImageDocument document, AnnotationEdit edit, bool forward)
    {
        var from = forward ? edit.Before : edit.After;
        var to = forward ? edit.After : edit.Before;
        var tagsAdded = forward ? edit.TagsAdded : edit.TagsRemoved;
        var tagsRemoved = forward ? edit.TagsRemoved : edit.TagsAdded;
        var status = forward ? edit.StatusAfter : edit.StatusBefore;

        var keptIds = to.Select(annotation => annotation.Id).ToHashSet();
        var deletes = from.Select(annotation => annotation.Id).Where(id => !keptIds.Contains(id)).ToList();

        _store.Apply(new StoreChange(document.ImageId, deletes, to, tagsAdded, tagsRemoved, status));

        var list = document.MutableAnnotations;
        list.RemoveAll(annotation => deletes.Contains(annotation.Id));
        foreach (var annotation in to)
        {
            var index = list.FindIndex(existing => existing.Id == annotation.Id);
            if (index >= 0) list[index] = annotation; else list.Add(annotation);
        }

        foreach (var tag in tagsRemoved) document.MutableTags.Remove(tag);
        foreach (var tag in tagsAdded) document.MutableTags.Add(tag);
        document.Status = status;
    }
}
