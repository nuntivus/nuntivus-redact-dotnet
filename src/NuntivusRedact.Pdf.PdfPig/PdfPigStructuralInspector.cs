using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.CrossReference;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace NuntivusRedact.Pdf.PdfPig;

/// <summary>Bounded current-xref token inspection with optional Flate cross-reference stream decoding; never certifies a secure format profile.
/// Supports bounded PNG xref predictors and direct-Flate object streams. Encryption is unsupported. No native runtime is required.</summary>
public sealed class PdfPigStructuralInspector : IPdfStructuralInspector
{
    /// <inheritdoc />
    public ValueTask<PdfStructuralInventory> InspectAsync(ReadOnlyMemory<byte> input, PdfInspectionLimits limits, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (input.Length > limits.MaximumInputBytes) throw new RedactionException(RedactionError.LimitExceeded);
        if (!input.Span.StartsWith("%PDF-"u8)) throw new RedactionException(RedactionError.InvalidDocument);
        using var stream = new BudgetedReadStream(input.ToArray(), limits, cancellationToken);
        var filters = new StructuralFilterProvider(limits, cancellationToken);
        try
        {
            using var document = PdfDocument.Open(stream, new ParsingOptions
            {
                UseLenientParsing = false,
                SkipMissingFonts = false,
                MaxStackDepth = limits.MaximumDepth,
                FilterProvider = filters
            });
            if (filters.Unsupported || document.Structure.Trailer.EncryptionToken is not null)
                throw new RedactionException(RedactionError.UnsupportedFormat);
            if (filters.LimitExceeded) throw new RedactionException(RedactionError.LimitExceeded);
            if (filters.InvalidDocument) throw new RedactionException(RedactionError.InvalidDocument);
            var table = document.Structure.CrossReferenceTable;
            if (table.Parts.Count < 1 || document.NumberOfPages < 1) throw new RedactionException(RedactionError.InvalidDocument);
            if (table.Parts.Count > limits.MaximumRevisions || document.NumberOfPages > limits.MaximumPages ||
                (long)table.ObjectOffsets.Count > (long)limits.MaximumObjects + 1)
                throw new RedactionException(RedactionError.LimitExceeded);
            if (table.Parts.Any(part => part.Type != CrossReferenceType.Table && part.Type != CrossReferenceType.Stream || part.TiedToXrefAtOffset is not null))
                throw new RedactionException(RedactionError.UnsupportedFormat);
            var objects = new Dictionary<IndirectReference, IToken>();
            foreach (var pair in table.ObjectOffsets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pair.Value.Type == XrefEntryType.Free) continue;
                if (pair.Value.Type != XrefEntryType.File && pair.Value.Type != XrefEntryType.ObjectStream)
                    throw new RedactionException(RedactionError.UnsupportedFormat);
                if (pair.Value.Value1 <= 0 || pair.Value.Type == XrefEntryType.File && pair.Value.Value1 >= input.Length || pair.Value.Value2 < 0)
                    throw new RedactionException(RedactionError.InvalidDocument);
                var token = document.Structure.GetObject(pair.Key);
                if (token.Number != pair.Key || token.Position.Type != pair.Value.Type || token.Position.Value1 != pair.Value.Value1)
                    throw new RedactionException(RedactionError.InvalidDocument);
                objects.Add(pair.Key, token.Data);
                if (objects.Count > limits.MaximumObjects) throw new RedactionException(RedactionError.LimitExceeded);
            }
            foreach (var pair in objects)
            {
                if (pair.Value is not StreamToken container || !container.StreamDictionary.Data.TryGetValue("Type", out var type) ||
                    type is not NameToken { Data: "ObjStm" }) continue;
                if (!filters.ObjectHeaders.TryGetValue(container.StreamDictionary, out var header))
                {
                    filters.GetFilters(container.StreamDictionary);
                    filters.Decode(container.Data.ToArray(), container.StreamDictionary, filters, 0);
                    header = filters.ObjectHeaders[container.StreamDictionary];
                }
                for (int index = 0; index < header.Numbers.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var reference = new IndirectReference(header.Numbers[index], 0);
                    if (!table.ObjectOffsets.TryGetValue(reference, out var location) || location.Type != XrefEntryType.ObjectStream ||
                        location.Value1 != pair.Key.ObjectNumber || location.Value2 != index || !objects.ContainsKey(reference))
                        throw new RedactionException(RedactionError.InvalidDocument);
                }
            }
            foreach (var pair in table.ObjectOffsets.Where(pair => pair.Value.Type == XrefEntryType.ObjectStream))
            {
                if (!objects.TryGetValue(new IndirectReference(pair.Value.Value1, 0), out var container) || container is not StreamToken streamToken ||
                    !filters.ObjectHeaders.TryGetValue(streamToken.StreamDictionary, out var header) || pair.Value.Value2 >= header.Numbers.Length ||
                    header.Numbers[pair.Value.Value2] != pair.Key.ObjectNumber)
                    throw new RedactionException(RedactionError.InvalidDocument);
            }
            var state = new InspectionState(objects, limits, cancellationToken);
            foreach (var token in objects.Values) state.Observe(token);
            foreach (var part in table.Parts) state.Observe(part.Dictionary);
            var roots = new List<IToken> { new IndirectReferenceToken(document.Structure.Trailer.Root) };
            if (document.Structure.Trailer.Info is not null) roots.Add(document.Structure.Trailer.Info);
            int reachable = state.CountReachable(new ArrayToken(roots.ToArray()));
            int unreferenced = objects.Count - reachable;
            var features = state.Features;
            if (table.Parts.Any(part => part.Type == CrossReferenceType.Stream)) features |= PdfStructuralFeatures.CrossReferenceStreams;
            if (filters.Decoded) features |= PdfStructuralFeatures.DecodedStructuralStreams;
            if (table.Parts.Count > 1) features |= PdfStructuralFeatures.AdditionalRevisions;
            if (document.Structure.Trailer.Info is not null) features |= PdfStructuralFeatures.Metadata;
            if (unreferenced > 0) features |= PdfStructuralFeatures.UnreferencedObjects;
            if (filters.Unsupported) throw new RedactionException(RedactionError.UnsupportedFormat);
            if (filters.LimitExceeded) throw new RedactionException(RedactionError.LimitExceeded);
            if (filters.InvalidDocument) throw new RedactionException(RedactionError.InvalidDocument);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new PdfStructuralInventory(document.NumberOfPages, objects.Count, state.Streams,
                state.MetadataStreams, table.Parts.Count, unreferenced, state.MaximumDepth, features));
        }
        catch (RedactionException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stream.LimitExceeded || filters.LimitExceeded) throw new RedactionException(RedactionError.LimitExceeded);
            if (filters.Unsupported) throw new RedactionException(RedactionError.UnsupportedFormat);
            throw new RedactionException(RedactionError.InvalidDocument);
        }
    }

    private sealed class InspectionState(Dictionary<IndirectReference, IToken> objects, PdfInspectionLimits limits, CancellationToken cancellationToken)
    {
        private long visits;
        private long encodedBytes;
        internal PdfStructuralFeatures Features { get; private set; }
        internal int Streams { get; private set; }
        internal int MetadataStreams { get; private set; }
        internal int MaximumDepth { get; private set; }

        private void Visit(int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++visits > limits.MaximumTokens || depth > limits.MaximumDepth)
                throw new RedactionException(RedactionError.LimitExceeded);
            MaximumDepth = Math.Max(MaximumDepth, depth);
        }

        internal void Observe(IToken root)
        {
            var stack = new Stack<(IToken Token, int Depth)>();
            stack.Push((root, 1));
            while (stack.TryPop(out var item))
            {
                Visit(item.Depth);
                switch (item.Token)
                {
                    case StreamToken value:
                        if (value.Data.Length > limits.MaximumEncodedStreamBytes ||
                            encodedBytes > limits.MaximumTotalEncodedStreamBytes - value.Data.Length)
                            throw new RedactionException(RedactionError.LimitExceeded);
                        encodedBytes += value.Data.Length;
                        Streams++;
                        Features |= PdfStructuralFeatures.OpaqueStreams;
                        if (GetName(value.StreamDictionary, "Type") == "Metadata") MetadataStreams++;
                        stack.Push((value.StreamDictionary, item.Depth + 1));
                        break;
                    case DictionaryToken dictionary:
                        ObserveDictionary(dictionary);
                        BoundPending(stack.Count, dictionary.Data.Count);
                        foreach (var value in dictionary.Data.Values) stack.Push((value, item.Depth + 1));
                        break;
                    case ArrayToken array:
                        BoundPending(stack.Count, array.Data.Count);
                        foreach (var value in array.Data) stack.Push((value, item.Depth + 1));
                        break;
                    case IndirectReferenceToken reference:
                        if (!objects.ContainsKey(reference.Data)) throw new RedactionException(RedactionError.InvalidDocument);
                        break;
                }
            }
        }

        private void BoundPending(int pending, int additional)
        {
            if ((long)pending + additional > limits.MaximumTokens - visits)
                throw new RedactionException(RedactionError.LimitExceeded);
        }

        private string? GetName(DictionaryToken dictionary, string key)
        {
            if (!dictionary.Data.TryGetValue(key, out var token)) return null;
            var seen = new HashSet<IndirectReference>();
            while (token is IndirectReferenceToken reference)
            {
                Visit(seen.Count + 1);
                if (!seen.Add(reference.Data) || !objects.TryGetValue(reference.Data, out token!))
                    throw new RedactionException(RedactionError.InvalidDocument);
            }
            if (token is NameToken name) return name.Data;
            Features |= PdfStructuralFeatures.UnclassifiedObjects;
            return null;
        }

        private void ObserveDictionary(DictionaryToken dictionary)
        {
            foreach (var key in dictionary.Data.Keys)
            {
                Features |= key switch
                {
                    "Metadata" => PdfStructuralFeatures.Metadata,
                    "Annots" => PdfStructuralFeatures.Annotations,
                    "AcroForm" or "XFA" or "FT" => PdfStructuralFeatures.Forms,
                    "EmbeddedFiles" or "EF" or "AF" or "Collection" => PdfStructuralFeatures.Attachments,
                    "OpenAction" or "AA" or "JS" or "JavaScript" or "Launch" or "RichMedia" or "Movie" or "Sound" or "S" => PdfStructuralFeatures.Actions,
                    "ByteRange" or "Sig" or "DSS" or "VRI" => PdfStructuralFeatures.Signatures,
                    "OCProperties" or "OC" => PdfStructuralFeatures.OptionalContent,
                    "SMask" or "Mask" or "SMaskInData" => PdfStructuralFeatures.ImageMasks,
                    "FontFile" or "FontFile2" or "FontFile3" => PdfStructuralFeatures.EmbeddedFonts,
                    _ => PdfStructuralFeatures.None
                };
            }
            Features |= GetName(dictionary, "Type") switch
            {
                null or "Catalog" or "Pages" or "Page" or "Font" or "FontDescriptor" or "Encoding" or "XObject" => PdfStructuralFeatures.None,
                "XRef" => PdfStructuralFeatures.CrossReferenceStreams,
                "ObjStm" => PdfStructuralFeatures.ObjectStreams,
                "Metadata" => PdfStructuralFeatures.Metadata,
                "Annot" => PdfStructuralFeatures.Annotations,
                "Filespec" or "EmbeddedFile" => PdfStructuralFeatures.Attachments,
                "Sig" => PdfStructuralFeatures.Signatures,
                "OCG" or "OCMD" => PdfStructuralFeatures.OptionalContent,
                "Action" => PdfStructuralFeatures.Actions,
                _ => PdfStructuralFeatures.UnclassifiedObjects
            };
            if (GetName(dictionary, "Type") == "XObject")
            {
                Features |= GetName(dictionary, "Subtype") switch
                {
                    "Image" => PdfStructuralFeatures.Images,
                    "Form" => PdfStructuralFeatures.FormXObjects,
                    _ => PdfStructuralFeatures.UnclassifiedObjects
                };
            }
        }

        internal int CountReachable(IToken trailer)
        {
            var reachable = new HashSet<IndirectReference>();
            var stack = new Stack<(IToken Token, int Depth)>();
            stack.Push((trailer, 1));
            while (stack.TryPop(out var item))
            {
                if (item.Token is IndirectReferenceToken alreadySeen && reachable.Contains(alreadySeen.Data)) continue;
                Visit(item.Depth);
                switch (item.Token)
                {
                    case IndirectReferenceToken reference:
                        if (!objects.TryGetValue(reference.Data, out var target)) throw new RedactionException(RedactionError.InvalidDocument);
                        reachable.Add(reference.Data);
                        stack.Push((target, item.Depth + 1));
                        break;
                    case StreamToken stream:
                        stack.Push((stream.StreamDictionary, item.Depth + 1));
                        break;
                    case DictionaryToken dictionary:
                        BoundPending(stack.Count, dictionary.Data.Count);
                        foreach (var value in dictionary.Data.Values) stack.Push((value, item.Depth + 1));
                        break;
                    case ArrayToken array:
                        BoundPending(stack.Count, array.Data.Count);
                        foreach (var value in array.Data) stack.Push((value, item.Depth + 1));
                        break;
                }
            }
            return reachable.Count;
        }
    }
}
