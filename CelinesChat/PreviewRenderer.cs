using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CelinesChat;

/// <summary>
/// Renders one outgoing-message preview chunk word by word (rather than reusing
/// ColoredTextRenderer's run-batched DrawSequence) so every word's own click/hover can be tested
/// individually - clicking anywhere in the preview jumps the compose box's cursor to the exact
/// matching character via MappedChunk.MapToSource, and a thin caret follows the mouse letter by
/// letter while hovering to show precisely where that click would land. Preview text is always
/// short (at most a handful of outgoing message chunks), so the extra per-word ImGui calls compared
/// to the batched renderer used for chat log entries are not a concern here.
///
/// Shared between PreviewWindow (a separate, freely movable window) and ChatWindow's embedded
/// "Inside" preview mode - the two only differ in how a click's resulting source character index
/// gets applied (cross-window activation vs. a same-window field write), which the caller supplies
/// via onCharacterClicked.
/// </summary>
internal static class PreviewRenderer
{
    private static readonly Vector4 LinkColor = new(0.4f, 0.7f, 1f, 1f);
    private static readonly Vector4 CaretColor = new(1f, 1f, 1f, 0.9f);

    public static void DrawMappedChunk(MappedChunk chunk, Configuration config, float availableWidth, Action<int> onCharacterClicked, int idSalt = 0)
    {
        var wrapWidth = MathF.Max(10f, availableWidth - ImGui.GetStyle().ScrollbarSize - 6f);
        var tokenizeState = new ColoredTextRenderer.TokenizeState();
        var words = SplitWordsWithOffsets(chunk.Text);
        var tokens = new List<(string Word, ColoredTextRenderer.TextSegmentKind Kind)>(
            ColoredTextRenderer.Tokenize(chunk.Text, string.Empty, tokenizeState));

        var lineWidthUsed = 0f;
        var startOfLine = true;

        for (var i = 0; i < words.Count; i++)
        {
            var (word, wordOffset) = words[i];
            var kind = i < tokens.Count ? tokens[i].Kind : ColoredTextRenderer.TextSegmentKind.Default;
            var color = ColoredTextRenderer.ResolveColor(null, null, kind, config.DefaultTextColor, config.EmoteTextColor, config.OocTextColor, config.MentionColor, LinkColor);

            var display = word + " ";
            var size = ImGui.CalcTextSize(display);

            // No explicit ImGui.NewLine() here - simply not calling SameLine() below is enough to
            // start a fresh line (every Text-family call does that on its own by default). Calling
            // NewLine() as well double-advanced the cursor at every wrap point - one line from the
            // explicit call, another from the next word naturally starting its own line - stacking
            // into the doubled gap between wrapped lines the preview showed.
            if (!startOfLine && lineWidthUsed + size.X > wrapWidth)
            {
                startOfLine = true;
                lineWidthUsed = 0f;
            }

            if (!startOfLine)
            {
                ImGui.SameLine(0, 0);
            }

            DrawClickableWord(display, word, wordOffset, size, color, chunk, onCharacterClicked, idSalt + i);

            lineWidthUsed += size.X;
            startOfLine = false;
        }

        if (startOfLine)
        {
            ImGui.TextColored(config.DefaultTextColor, " ");
        }
    }

    private static void DrawClickableWord(string display, string bareWord, int wordChunkOffset, Vector2 size, Vector4 color, MappedChunk chunk, Action<int> onCharacterClicked, int uniqueId)
    {
        // Deliberately ImGui.Selectable rather than ImGui.TextColored + manual IsItemClicked - this
        // matches how Chat2's own click-to-cursor preview draws each letter (a real, nav-participating
        // widget). A plain Text item's click never reliably let a later SetKeyboardFocusHere() call
        // actually activate the compose box for keyboard input in this project, even once every
        // cross-window/same-frame timing issue was ruled out - Selectable is the one thing that fixed
        // it, matching WrapSelection's own trigger (also a Selectable, in its rp-actions popup).
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(1f, 1f, 1f, 0.08f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(1f, 1f, 1f, 0.14f));
        var clicked = ImGui.Selectable($"{display}##preview{uniqueId}", false, ImGuiSelectableFlags.None, size);
        ImGui.PopStyleColor(3);

        if (!ImGui.IsItemHovered() || bareWord.Length == 0)
        {
            return;
        }

        ImGui.SetMouseCursor(ImGuiMouseCursor.TextInput);

        var rectMin = ImGui.GetItemRectMin();
        var rectMax = ImGui.GetItemRectMax();
        var localOffset = FindCharOffset(bareWord, ImGui.GetMousePos().X - rectMin.X);

        var caretX = rectMin.X + ImGui.CalcTextSize(bareWord[..localOffset]).X;
        ImGui.GetWindowDrawList().AddLine(
            new Vector2(caretX, rectMin.Y),
            new Vector2(caretX, rectMax.Y),
            ImGui.GetColorU32(CaretColor),
            1.5f);

        if (clicked)
        {
            onCharacterClicked(chunk.MapToSource(wordChunkOffset + localOffset));
        }
    }

    /// Finds the character offset within `word` whose boundary is pixel-closest to `localPixelX`
    /// (measured from the word's own left edge) - the "letter by letter" precision the caret and the
    /// resulting cursor jump both rely on.
    private static int FindCharOffset(string word, float localPixelX)
    {
        if (localPixelX <= 0f)
        {
            return 0;
        }

        var previousWidth = 0f;
        for (var k = 1; k <= word.Length; k++)
        {
            var width = ImGui.CalcTextSize(word[..k]).X;
            if (width >= localPixelX)
            {
                return localPixelX - previousWidth <= width - localPixelX ? k - 1 : k;
            }

            previousWidth = width;
        }

        return word.Length;
    }

    /// Same whitespace-splitting rule ColoredTextRenderer.Tokenize uses internally, but also
    /// returning each word's start offset within `text` - the two are zipped by index in
    /// DrawMappedChunk, which only works because both split on the exact same characters with the
    /// exact same "no empty entries" behavior.
    private static List<(string Word, int Offset)> SplitWordsWithOffsets(string text)
    {
        var result = new List<(string, int)>();
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && IsWordSeparator(text[i]))
            {
                i++;
            }

            if (i >= text.Length)
            {
                break;
            }

            var start = i;
            while (i < text.Length && !IsWordSeparator(text[i]))
            {
                i++;
            }

            result.Add((text[start..i], start));
        }

        return result;
    }

    private static bool IsWordSeparator(char c) => c is ' ' or '\t' or '\r' or '\n';
}
