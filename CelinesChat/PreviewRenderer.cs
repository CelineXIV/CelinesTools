using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace CelinesChat;

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
        bool clicked;
        using (ImRaii.PushColor(ImGuiCol.Text, color)
                   .Push(ImGuiCol.HeaderHovered, new Vector4(1f, 1f, 1f, 0.08f))
                   .Push(ImGuiCol.HeaderActive, new Vector4(1f, 1f, 1f, 0.14f)))
        {
            clicked = ImGui.Selectable($"{display}##preview{uniqueId}", false, ImGuiSelectableFlags.None, size);
        }

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
