namespace Sia.Examples.Notebook;

internal static class NotebookDocumentEdits
{
    internal static IReadOnlyList<NotebookSection> InsertBlockAfter(
        IReadOnlyList<NotebookSection> sections, string? afterCellId, NotebookBlock newBlock)
    {
        if (afterCellId is null) {
            if (sections.Count == 0) {
                throw new InvalidOperationException("Cannot insert a cell into a notebook with no sections.");
            }
            var lastIndex = sections.Count - 1;
            return ReplaceBlocks(sections, lastIndex, [..sections[lastIndex].Blocks, newBlock]);
        }
        if (!TryFindBlock(sections, afterCellId, out var sectionIndex, out var blockIndex)) {
            throw new ArgumentException($"No block with id '{afterCellId}' found.", nameof(afterCellId));
        }
        var blocks = sections[sectionIndex].Blocks.ToList();
        blocks.Insert(blockIndex + 1, newBlock);
        return ReplaceBlocks(sections, sectionIndex, blocks);
    }

    internal static IReadOnlyList<NotebookSection> RemoveBlock(
        IReadOnlyList<NotebookSection> sections, string cellId)
    {
        if (!TryFindBlock(sections, cellId, out var sectionIndex, out var blockIndex)) {
            throw new ArgumentException($"No block with id '{cellId}' found.", nameof(cellId));
        }
        var blocks = sections[sectionIndex].Blocks.ToList();
        blocks.RemoveAt(blockIndex);
        return ReplaceBlocks(sections, sectionIndex, blocks);
    }

    internal static IReadOnlyList<NotebookSection> MoveBlock(
        IReadOnlyList<NotebookSection> sections, string cellId, int offset)
    {
        if (!TryFindBlock(sections, cellId, out var sectionIndex, out var blockIndex)) {
            throw new ArgumentException($"No block with id '{cellId}' found.", nameof(cellId));
        }
        var blocks = sections[sectionIndex].Blocks;
        var targetIndex = blockIndex + offset;
        if (targetIndex < 0 || targetIndex >= blocks.Count) {
            return sections;
        }
        var updated = blocks.ToList();
        (updated[blockIndex], updated[targetIndex]) = (updated[targetIndex], updated[blockIndex]);
        return ReplaceBlocks(sections, sectionIndex, updated);
    }

    internal static IReadOnlyList<NotebookSection> UpdateBlock(
        IReadOnlyList<NotebookSection> sections, string blockId, Func<NotebookBlock, NotebookBlock> transform)
    {
        if (!TryFindBlock(sections, blockId, out var sectionIndex, out var blockIndex)) {
            return sections;
        }
        var blocks = sections[sectionIndex].Blocks.ToList();
        blocks[blockIndex] = transform(blocks[blockIndex]);
        return ReplaceBlocks(sections, sectionIndex, blocks);
    }

    internal static string? GetBlockId(NotebookBlock block)
        => block switch {
            CodeCellBlock cell => cell.Id,
            ParagraphBlock paragraph => paragraph.Id,
            _ => null,
        };

    private static bool TryFindBlock(
        IReadOnlyList<NotebookSection> sections, string blockId, out int sectionIndex, out int blockIndex)
    {
        for (sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++) {
            var blocks = sections[sectionIndex].Blocks;
            for (blockIndex = 0; blockIndex < blocks.Count; blockIndex++) {
                if (GetBlockId(blocks[blockIndex]) == blockId) {
                    return true;
                }
            }
        }
        blockIndex = -1;
        return false;
    }

    private static IReadOnlyList<NotebookSection> ReplaceBlocks(
        IReadOnlyList<NotebookSection> sections, int sectionIndex, IReadOnlyList<NotebookBlock> blocks)
    {
        var updated = sections.ToList();
        updated[sectionIndex] = sections[sectionIndex] with { Blocks = blocks };
        return updated;
    }
}
