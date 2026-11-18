namespace NuntivusRedact;

/// <summary>Optional capability for formatting-preserving adapters. Executes detection and strategies once.</summary>
public interface IEditAwareTextRedactor : ITextRedactor
{
    /// <summary>Returns ordered, nonoverlapping original-coordinate edits and final text. Results are sensitive; never log them.</summary>
    ValueTask<EditRedactionResult> RedactWithEditsAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>An accepted detection and its replacement. Contains sensitive transient data; never log or put it in reports.</summary>
public sealed class RedactionEdit
{
    /// <summary>Creates an edit in original UTF-16 coordinates.</summary>
    public RedactionEdit(Detection detection, string replacement)
    {
        Detection = detection ?? throw new ArgumentNullException(nameof(detection));
        Replacement = replacement ?? throw new ArgumentNullException(nameof(replacement));
    }
    /// <summary>Gets the source-free accepted detection.</summary>
    public Detection Detection { get; }
    /// <summary>Gets sensitive replacement text, which may be empty.</summary>
    public string Replacement { get; }
}

/// <summary>Final text and ordered edits. Contains sensitive transient data; never log it.</summary>
public sealed class EditRedactionResult
{
    /// <summary>Copies ordered nonoverlapping edits. The producer must ensure they reconstruct the final text from the original input.</summary>
    public EditRedactionResult(string text, IEnumerable<RedactionEdit> edits)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        ArgumentNullException.ThrowIfNull(edits);
        var snapshot = edits.ToArray();
        int end = 0;
        foreach (var edit in snapshot)
        {
            if (edit is null || edit.Detection.Span.Start < end)
                throw new ArgumentException("Edits must be ordered and nonoverlapping.", nameof(edits));
            end = edit.Detection.Span.End;
        }
        Edits = Array.AsReadOnly(snapshot);
    }
    /// <summary>Gets sensitive final text; undetected sensitive data may remain.</summary>
    public string Text { get; }
    /// <summary>Gets a defensive snapshot of original-coordinate edits, ordered by start.</summary>
    public IReadOnlyList<RedactionEdit> Edits { get; }
}
