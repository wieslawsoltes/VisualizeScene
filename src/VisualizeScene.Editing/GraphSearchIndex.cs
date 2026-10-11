using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using VisualizeScene.Core;

namespace VisualizeScene.Editing;

[Flags] public enum GraphSearchFields { None = 0, Text = 1, Id = 2, All = Text | Id }
public sealed record GraphSearchOptions(bool MatchCase = false, bool WholeWord = false, bool IncludeHidden = false,
    SelectionKinds Kinds = SelectionKinds.All, GraphSearchFields Fields = GraphSearchFields.Text,
    int MaximumResults = 10000, long MaximumCharacters = 16 * 1024 * 1024);
public sealed record GraphSearchMatch(string ItemId, SelectionKinds Kind, GraphSearchFields Field,
    int Start, int Length, string Value, string Preview, bool IsVisible, bool CanReplace);
public readonly record struct GraphReplacementResult(int ChangedItems, int ReplacedOccurrences, int SkippedReadOnly);

/// <summary>Immutable text/identity registry for one graph revision. Literal ordinal searches; no regex evaluation.</summary>
public sealed class GraphSearchIndex
{
    private sealed record Entry(string Id, SelectionKinds Kind, string Text, bool Visible, bool Editable);
    private readonly ImmutableArray<Entry> entries;
    public GraphSnapshot Snapshot { get; }
    public int Count => entries.Length;
    public GraphSearchIndex(GraphSnapshot snapshot)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        var index = snapshot.Index(); var values = ImmutableArray.CreateBuilder<Entry>();
        var locked = new Dictionary<string, bool>(StringComparer.Ordinal); var ancestry = new List<string>();
        bool NodeLocked(string id)
        {
            ancestry.Clear(); var value = false;
            for (var current = id; current is not null; current = index.Nodes[current].ParentId)
            {
                if (locked.TryGetValue(current, out value)) break;
                ancestry.Add(current);
                if (index.Nodes[current].Locked) { value = true; break; }
            }
            foreach (var item in ancestry) locked[item] = value;
            return value;
        }
        foreach (var node in snapshot.Nodes) values.Add(new(node.Id, node.IsGroup ? SelectionKinds.Groups : SelectionKinds.Nodes,
            node.Text, index.IsVisible(node.Id), !NodeLocked(node.Id)));
        bool OwnerLocked(string id)
        {
            if (index.Nodes.ContainsKey(id)) return NodeLocked(id);
            if (index.Ports.TryGetValue(id, out var port)) return NodeLocked(port.NodeId);
            var edge = index.Edges[id]; return NodeLocked(edge.SourceId) || NodeLocked(edge.TargetId);
        }
        foreach (var edge in snapshot.Edges) values.Add(new(edge.Id, SelectionKinds.Edges, edge.Text,
            GraphScene.IsEdgeVisible(index, edge), !OwnerLocked(edge.Id)));
        foreach (var port in snapshot.Ports) values.Add(new(port.Id, SelectionKinds.Ports, "", index.IsVisible(port.NodeId), false));
        foreach (var label in snapshot.Labels) values.Add(new(label.Id, SelectionKinds.Labels, label.Text,
            GraphScene.IsOwnerVisible(index, label.OwnerId), !OwnerLocked(label.OwnerId)));
        entries = values.ToImmutable();
    }
    public GraphSearchResult Find(string query, GraphSearchOptions? options = null,
        IEnumerable<string>? scope = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(query);
        options ??= new();
        if (query.Length > 4096) throw new ArgumentOutOfRangeException(nameof(query), "Search text is limited to 4096 UTF-16 units.");
        if ((options.Kinds & ~SelectionKinds.All) != 0 || (options.Fields & ~GraphSearchFields.All) != 0 ||
            options.MaximumResults is < 1 or > 100000 || options.MaximumCharacters is < 1 or > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(options));
        // Copy caller-owned scope before searching. Null means all items; empty means no items.
        var selected = scope?.ToHashSet(StringComparer.Ordinal);
        var matches = ImmutableArray.CreateBuilder<GraphSearchMatch>();
        var scannedItems = 0; long scannedCharacters = 0;
        if (query.Length == 0) return new(Snapshot, query, options, [], false, 0, 0);
        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((entry.Kind & options.Kinds) == 0 || !options.IncludeHidden && !entry.Visible || selected is not null && !selected.Contains(entry.Id)) continue;
            scannedItems++;
            for (var field = GraphSearchFields.Text; field <= GraphSearchFields.Id; field = (GraphSearchFields)((int)field << 1))
            {
                if ((options.Fields & field) == 0 || field == GraphSearchFields.Text && entry.Kind == SelectionKinds.Ports) continue;
                var text = field == GraphSearchFields.Text ? entry.Text : entry.Id;
                scannedCharacters += text.Length;
                if (scannedCharacters > options.MaximumCharacters)
                    throw new InvalidOperationException("Search character budget exceeded. Narrow the item scope.");
                for (var position = 0; position <= text.Length - query.Length;)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var start = text.IndexOf(query, position, comparison); if (start < 0) break;
                    var end = start + query.Length;
                    if (IsScalarBoundary(text, start) && IsScalarBoundary(text, end) &&
                        (!options.WholeWord || !WordBefore(text, start) && !WordAt(text, end)))
                    {
                        if (matches.Count == options.MaximumResults)
                            return new(Snapshot, query, options, matches.ToImmutable(), true, scannedItems, scannedCharacters);
                        matches.Add(new(entry.Id, entry.Kind, field, start, query.Length, text.Substring(start, query.Length),
                            Preview(text, start), entry.Visible, field == GraphSearchFields.Text && entry.Editable));
                        position = end; // Non-overlapping occurrences, matching replacement semantics.
                    }
                    else position = start + 1;
                }
            }
        }
        return new(Snapshot, query, options, matches.ToImmutable(), false, scannedItems, scannedCharacters);
    }
    private static bool IsScalarBoundary(string text, int offset) => offset <= 0 || offset >= text.Length ||
        !char.IsHighSurrogate(text[offset - 1]) || !char.IsLowSurrogate(text[offset]);
    private static bool WordBefore(string text, int offset)
    {
        if (offset <= 0) return false;
        var i = offset - 1; if (i > 0 && char.IsLowSurrogate(text[i]) && char.IsHighSurrogate(text[i - 1])) i--;
        return WordAt(text, i);
    }
    private static bool WordAt(string text, int offset)
    {
        if (offset >= text.Length || !Rune.TryGetRuneAt(text, offset, out var rune)) return false;
        return Rune.IsLetterOrDigit(rune) || Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.ConnectorPunctuation;
    }
    private static string Preview(string text, int start)
    {
        var from = Math.Max(0, start - 24); if (!IsScalarBoundary(text, from)) from--;
        var to = Math.Min(text.Length, from + 120); if (!IsScalarBoundary(text, to)) to--;
        return (from > 0 ? "…" : "") + text[from..to].Replace('\r', ' ').Replace('\n', ' ') + (to < text.Length ? "…" : "");
    }
}

/// <summary>Revision-bound, immutable occurrences. Replacement is validated and committed atomically.</summary>
public sealed class GraphSearchResult
{
    public GraphSnapshot Snapshot { get; }
    public string Query { get; }
    public GraphSearchOptions Options { get; }
    public ImmutableArray<GraphSearchMatch> Matches { get; }
    public bool IsTruncated { get; }
    public int ExaminedItems { get; }
    public long ExaminedCharacters { get; }
    internal GraphSearchResult(GraphSnapshot snapshot, string query, GraphSearchOptions options,
        ImmutableArray<GraphSearchMatch> matches, bool truncated, int items, long characters)
        => (Snapshot, Query, Options, Matches, IsTruncated, ExaminedItems, ExaminedCharacters) = (snapshot, query, options, matches, truncated, items, characters);

    /// <param name="matchIndex">One result index; null replaces all complete results. IDs and locked owners are read-only.</param>
    public GraphReplacementResult Replace(GraphDocument document, string replacement, int? matchIndex = null,
        long? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(replacement);
        cancellationToken.ThrowIfCancellationRequested();
        if (replacement.Length > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(replacement));
        if (!ReferenceEquals(document.Snapshot, Snapshot) || expectedRevision.HasValue && expectedRevision != document.Revision)
            throw new InvalidOperationException("Search results are stale. Search the current graph again.");
        if (matchIndex is < 0 || matchIndex >= Matches.Length) throw new ArgumentOutOfRangeException(nameof(matchIndex));
        if (matchIndex is null && IsTruncated) throw new InvalidOperationException("Replace All requires complete results. Narrow the query or increase the result limit.");
        var revision = document.Revision; var index = Snapshot.Index(); var edits = new Dictionary<string, string>(StringComparer.Ordinal);
        ImmutableArray<GraphSearchMatch> chosen = matchIndex is { } i ? [Matches[i]] : Matches;
        var skipped = 0; var occurrences = 0; long output = 0;
        foreach (var group in chosen.GroupBy(m => m.ItemId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var writable = group.Where(m => m.CanReplace).OrderBy(m => m.Start).ToArray(); skipped += group.Count(m => !m.CanReplace);
            if (writable.Length == 0) continue;
            var text = index.Nodes.TryGetValue(group.Key, out var node) ? node.Text : index.Edges.TryGetValue(group.Key, out var edge) ? edge.Text : index.Labels[group.Key].Text;
            long length = text.Length;
            foreach (var match in writable) length += (long)replacement.Length - match.Length;
            output += length;
            if (length > 16 * 1024 * 1024 || output > 64 * 1024 * 1024)
                throw new InvalidOperationException("Replacement output budget exceeded. Narrow the item scope.");
            var builder = new StringBuilder((int)length); var offset = 0;
            foreach (var match in writable)
            {
                cancellationToken.ThrowIfCancellationRequested();
                builder.Append(text, offset, match.Start - offset).Append(replacement); offset = match.Start + match.Length;
                if (match.Value != replacement) occurrences++;
            }
            builder.Append(text, offset, text.Length - offset); var next = builder.ToString();
            if (next != text) edits.Add(group.Key, next);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (edits.Count > 0) document.Edit(matchIndex.HasValue ? "Replace graph text" : "Replace all graph text", edit =>
        {
            foreach (var pair in edits)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (index.Nodes.TryGetValue(pair.Key, out var node)) edit.Update(node with { Text = pair.Value });
                else if (index.Edges.TryGetValue(pair.Key, out var edge)) edit.Update(edge with { Text = pair.Value });
                else edit.Update(index.Labels[pair.Key] with { Text = pair.Value });
            }
        }, revision);
        return new(edits.Count, occurrences, skipped);
    }
}
