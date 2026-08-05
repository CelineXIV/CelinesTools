using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using CelinesRPChat.Services;

namespace CelinesRPChat.Windows;

internal sealed class ChatWindow : Window
{
    private const string GenericDraftKey = "*generic*";
    private const float SplitterHeight = 6f;
    private const float MinComposeAreaHeight = 70f;
    private const float MaxComposeAreaHeight = 400f;
    private const float ActionRowHeight = 30f;
    private const float PreviewToggleLineHeight = 24f;
    private static readonly Vector4 SearchHighlightColor = new(1f, 0.65f, 0.15f, 1f);

    private static readonly (ChatChannel Channel, FontAwesomeIcon Icon)[] ChannelIcons =
    {
        (ChatChannel.Say, FontAwesomeIcon.Comment),
        (ChatChannel.Party, FontAwesomeIcon.Users),
        (ChatChannel.Whisper, FontAwesomeIcon.EnvelopeOpen),
        (ChatChannel.Yell, FontAwesomeIcon.Bullhorn),
        (ChatChannel.Shout, FontAwesomeIcon.VolumeUp),
        (ChatChannel.FreeCompany, FontAwesomeIcon.Flag),
        (ChatChannel.Linkshell, FontAwesomeIcon.Link),
    };

    private readonly Plugin plugin;

    // Compose state
    private string inputText = string.Empty;
    private string currentDraftKey = string.Empty;
    private string validationMessage = string.Empty;
    private string newSnippetName = string.Empty;
    private string snippetSearchFilter = string.Empty;
    private string activeWhisperTabTarget = string.Empty;
    private string? pendingTabForceSelect;
    private int selectionStart;
    private int selectionEnd;
    private int cursorPos;
    private string cachedInput = string.Empty;
    private int cachedMaxLength = -1;
    private List<string> cachedChunks = new();
    private bool splitterDragging;

    // Log state
    private int lastEntryCount;
    private List<ChatLogEntry>? loadedHistoryEntries;
    private string? loadedHistoryLabel;
    private string logSearchFilter = string.Empty;
    private string historySearchFilter = string.Empty;

    public ChatWindow(Plugin plugin) : base(WindowTitles.Chat)
    {
        this.plugin = plugin;
        Size = new Vector2(480, 620);
        SizeCondition = ImGuiCond.FirstUseEver;

        var initialState = plugin.GetCharacterState();
        if (initialState.LastChannel == ChatChannel.Whisper)
        {
            activeWhisperTabTarget = initialState.LastWhisperTarget;
            pendingTabForceSelect = activeWhisperTabTarget;
        }
    }

    public override void PreDraw()
    {
        var opacity = IsFocused ? plugin.Configuration.WindowOpacity : plugin.Configuration.UnfocusedWindowOpacity;
        ImGui.SetNextWindowBgAlpha(opacity);
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(plugin.Configuration.FontScale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 4f));

        var config = plugin.Configuration;
        var state = plugin.GetCharacterState();

        if (plugin.PendingWhisperTarget is { } pendingTarget)
        {
            activeWhisperTabTarget = pendingTarget;
            pendingTabForceSelect = pendingTarget;
            plugin.PendingWhisperTarget = null;
        }

        SyncDraft(state);

        DrawChannelRow(state);

        if (state.LastChannel == ChatChannel.Whisper)
        {
            DrawWhisperTarget(state);
            DrawWhisperTabs(state);
        }
        else if (state.LastChannel == ChatChannel.Linkshell)
        {
            ImGui.SetNextItemWidth(80);
            var ls = state.LinkshellNumber;
            if (ImGui.InputInt(Loc.T("Channel.LinkshellNumberLabel"), ref ls))
            {
                state.LinkshellNumber = Math.Clamp(ls, 1, 8);
                plugin.SaveConfiguration();
            }
        }

        DrawLogToolbar();

        var chunks = GetChunks(inputText, config.MaxMessageLength);
        plugin.CurrentPreviewChunks = chunks;

        var composeAreaHeight = Math.Clamp(config.ComposeAreaHeight, MinComposeAreaHeight, MaxComposeAreaHeight);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0f, 0f, 0f, config.ChatLogBackgroundOpacity));
        ImGui.BeginChild("##chatLogScroll", new Vector2(-1, -(composeAreaHeight + SplitterHeight)), true);
        DrawChatLogEntries(config);
        ImGui.EndChild();
        ImGui.PopStyleColor();

        DrawSplitter(config);

        var inputBoxHeight = Math.Max(30f, composeAreaHeight - PreviewToggleLineHeight - ActionRowHeight - 16f);

        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextMultiline("##composeText", ref inputText, 4000, new Vector2(-1, inputBoxHeight), ImGuiInputTextFlags.CallbackAlways, TrackTextState))
        {
            state.Drafts[currentDraftKey] = inputText;
            plugin.SaveConfiguration();
        }

        HandleSendHotkey(state);

        DrawPreviewToggle(chunks);

        DrawActionRow(state, chunks);

        if (!string.IsNullOrEmpty(validationMessage))
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), validationMessage);
        }

        ImGui.PopStyleVar(2);
    }

    private void DrawSplitter(Configuration config)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1f, 1f, 1f, 0.06f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.16f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1f, 1f, 1f, 0.26f));
        ImGui.Button("##chatSplitter", new Vector2(-1, SplitterHeight));
        ImGui.PopStyleColor(3);

        if (ImGui.IsItemActive())
        {
            splitterDragging = true;
            config.ComposeAreaHeight = Math.Clamp(config.ComposeAreaHeight - ImGui.GetIO().MouseDelta.Y, MinComposeAreaHeight, MaxComposeAreaHeight);
        }
        else if (splitterDragging)
        {
            splitterDragging = false;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);
        }
    }

    private void DrawChannelRow(CharacterState state)
    {
        for (var i = 0; i < ChannelIcons.Length; i++)
        {
            var (channel, icon) = ChannelIcons[i];
            var isActive = channel == state.LastChannel;

            if (isActive)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
            }

            if (ImGuiComponents.IconButton(channel.ToString(), icon))
            {
                state.LastChannel = channel;
                activeWhisperTabTarget = channel == ChatChannel.Whisper ? state.LastWhisperTarget : string.Empty;
                if (channel == ChatChannel.Whisper)
                {
                    pendingTabForceSelect = activeWhisperTabTarget;
                }

                plugin.SaveConfiguration();
            }

            if (isActive)
            {
                ImGui.PopStyleColor();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(ChannelLabel(channel));
            }

            if (i < ChannelIcons.Length - 1)
            {
                ImGui.SameLine();
            }
        }
    }

    private void DrawLogToolbar()
    {
        ImGui.SetNextItemWidth(160);
        ImGui.InputTextWithHint("##chatLogSearch", Loc.T("ChatLog.SearchHint"), ref logSearchFilter, 200);

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.CalendarAlt))
        {
            ImGui.OpenPopup("##historyDatesPopup");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("ChatLog.LoadHistory"));
        }

        DrawHistoryDatesPopup();

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.FolderOpen))
        {
            Process.Start(new ProcessStartInfo(plugin.ChatLog.LogsFolderPath) { UseShellExecute = true });
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("ChatLog.OpenFolder"));
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Eraser))
        {
            plugin.ChatLog.Clear();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.Clear"));
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog))
        {
            plugin.ToggleSettingsWindow();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.Settings"));
        }

        if (loadedHistoryEntries != null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"({loadedHistoryLabel})");
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Bolt))
            {
                loadedHistoryEntries = null;
                loadedHistoryLabel = null;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("ChatLog.BackToLive"));
            }
        }
    }

    private void DrawHistoryDatesPopup()
    {
        if (!ImGui.BeginPopup("##historyDatesPopup"))
        {
            return;
        }

        var dates = plugin.ChatLog.GetAvailableLogDates();
        if (dates.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("ChatLog.NoHistoryFiles"));
            ImGui.EndPopup();
            return;
        }

        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##historyDateSearch", Loc.T("ChatLog.HistorySearchHint"), ref historySearchFilter, 20);

        ImGui.BeginChild("##historyDateList", new Vector2(180, 220), true);
        foreach (var date in dates)
        {
            if (historySearchFilter.Length > 0 && date.IndexOf(historySearchFilter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (ImGui.Selectable(date))
            {
                loadedHistoryEntries = plugin.ChatLog.LoadHistoryFile(date);
                loadedHistoryLabel = date;
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.EndChild();

        ImGui.EndPopup();
    }

    private void DrawChatLogEntries(Configuration config)
    {
        var viewingHistory = loadedHistoryEntries != null;
        IReadOnlyList<ChatLogEntry> entries = loadedHistoryEntries ?? plugin.ChatLog.Entries;

        var nearBottomBeforeDraw = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 5f;
        var mentionTerm = plugin.MentionFirstName;

        var index = 0;
        foreach (var entry in entries)
        {
            if (!ChannelDisplay.IsVisible(entry.ChatType, config))
            {
                continue;
            }

            if (logSearchFilter.Length > 0
                && entry.Text.IndexOf(logSearchFilter, StringComparison.OrdinalIgnoreCase) < 0
                && entry.Sender.IndexOf(logSearchFilter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            DrawLogEntry(entry, config, mentionTerm, index);
            index++;
        }

        if (!viewingHistory && entries.Count != lastEntryCount && nearBottomBeforeDraw)
        {
            ImGui.SetScrollHereY(1f);
        }

        lastEntryCount = entries.Count;
    }

    private void DrawLogEntry(ChatLogEntry entry, Configuration config, string mentionTerm, int index)
    {
        ImGui.PushID(index);
        ImGui.BeginGroup();

        var prefixSize = Vector2.Zero;

        var timestamp = entry.Timestamp.ToString("HH:mm") + " ";
        ImGui.TextDisabled(timestamp);
        ImGui.SameLine(0, 0);
        prefixSize += ImGui.CalcTextSize(timestamp);

        var tag = ChannelDisplay.Tag(entry.ChatType) + " ";
        ImGui.TextColored(ChannelDisplay.Color(entry.ChatType), tag);
        ImGui.SameLine(0, 0);
        prefixSize += ImGui.CalcTextSize(tag);

        var namePart = entry.Sender + ": ";
        var senderColor = logSearchFilter.Length > 0 && entry.Sender.Contains(logSearchFilter, StringComparison.OrdinalIgnoreCase)
            ? SearchHighlightColor
            : config.ChatLogSenderNameColor;
        ImGui.TextColored(senderColor, namePart);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(Loc.T("ChatLog.ClickToWhisper"));
        }

        if (ImGui.IsItemClicked())
        {
            var whisperTarget = entry.SenderWorld != null ? $"{entry.Sender}@{entry.SenderWorld}" : entry.Sender;
            plugin.SetWhisperTarget(whisperTarget);
        }

        var namePartSize = ImGui.CalcTextSize(namePart);

        ImGui.SameLine(0, 0);
        ColoredTextRenderer.Draw(
            entry.Text,
            config.DefaultTextColor,
            config.EmoteTextColor,
            config.OocTextColor,
            config.MentionColor,
            mentionTerm,
            prefixSize.X + namePartSize.X,
            logSearchFilter,
            SearchHighlightColor);
        ImGui.Spacing();

        ImGui.EndGroup();

        if (ImGui.IsItemHovered())
        {
            var min = ImGui.GetItemRectMin() - new Vector2(4f, 2f);
            var max = ImGui.GetItemRectMax() + new Vector2(4f, 2f);
            ImGui.GetWindowDrawList().AddRect(min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.35f)), 3f, ImDrawFlags.None, 1.3f);
        }

        if (ImGui.BeginPopupContextItem("##logEntryContext"))
        {
            if (ImGui.Selectable(Loc.T("ChatLog.CopyMessage")))
            {
                ImGui.SetClipboardText($"{entry.Sender}: {entry.Text}");
            }

            ImGui.EndPopup();
        }

        ImGui.PopID();
    }

    private void SyncDraft(CharacterState state)
    {
        var key = state.LastChannel == ChatChannel.Whisper && !string.IsNullOrEmpty(activeWhisperTabTarget)
            ? activeWhisperTabTarget
            : GenericDraftKey;

        if (key == currentDraftKey)
        {
            return;
        }

        if (!string.IsNullOrEmpty(currentDraftKey))
        {
            state.Drafts[currentDraftKey] = inputText;
        }

        inputText = state.Drafts.TryGetValue(key, out var draft) ? draft : string.Empty;
        currentDraftKey = key;
    }

    private int TrackTextState(ref ImGuiInputTextCallbackData data)
    {
        selectionStart = data.SelectionStart;
        selectionEnd = data.SelectionEnd;
        cursorPos = data.CursorPos;
        return 0;
    }

    private void HandleSendHotkey(CharacterState state)
    {
        if (!ImGui.IsItemFocused())
        {
            return;
        }

        var ctrlEnter = ImGui.GetIO().KeyCtrl && (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter));
        if (ctrlEnter)
        {
            TrySend(state);
        }
    }

    private List<string> GetChunks(string text, int maxLength)
    {
        if (text != cachedInput || maxLength != cachedMaxLength)
        {
            cachedInput = text;
            cachedMaxLength = maxLength;
            cachedChunks = MessageSplitter.BuildMessages(text, maxLength);
        }

        return cachedChunks;
    }

    private void TrySend(CharacterState state)
    {
        var chunks = GetChunks(inputText, plugin.Configuration.MaxMessageLength);

        if (chunks.Count == 0)
        {
            validationMessage = Loc.T("Compose.EmptyText");
            return;
        }

        if (state.LastChannel == ChatChannel.Whisper && string.IsNullOrWhiteSpace(state.LastWhisperTarget))
        {
            validationMessage = Loc.T("Compose.WhisperTargetMissing");
            return;
        }

        validationMessage = string.Empty;
        plugin.SendChunks(chunks, inputText);
        inputText = string.Empty;
        state.Drafts[currentDraftKey] = string.Empty;
        plugin.SaveConfiguration();
    }

    private void DrawPreviewToggle(List<string> chunks)
    {
        if (ImGuiComponents.IconButton(FontAwesomeIcon.WindowRestore))
        {
            plugin.TogglePreviewWindow();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.OpenPreviewWindow"));
        }

        ImGui.SameLine();
        ImGui.TextDisabled(Loc.T("Compose.MessageCount", chunks.Count));
    }

    private void DrawActionRow(CharacterState state, List<string> chunks)
    {
        if (plugin.IsSending)
        {
            var done = plugin.SendTotal - plugin.SendRemaining;
            ImGui.TextUnformatted(Loc.T("Compose.Sending", done, plugin.SendTotal));
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Ban, new Vector4(0.55f, 0.18f, 0.18f, 1f), new Vector4(0.55f, 0.18f, 0.18f, 1f), new Vector4(0.68f, 0.22f, 0.22f, 1f)))
            {
                plugin.CancelSending();
            }

            return;
        }

        if (ImGuiComponents.IconButton(FontAwesomeIcon.TheaterMasks))
        {
            WrapSelection("*", "*");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.WrapEmote") + " - " + Loc.T("Compose.WrapHint"));
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.CommentDots))
        {
            WrapSelection("((", "))");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.WrapOoc") + " - " + Loc.T("Compose.WrapHint"));
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.StickyNote))
        {
            ImGui.OpenPopup("##snippetsPopup");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.Snippets"));
        }

        DrawSnippetsPopup();

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Redo))
        {
            ImGui.OpenPopup("##historyPopup");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.History"));
        }

        DrawHistoryPopup();

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Copy))
        {
            ImGui.SetClipboardText(inputText);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.Copy"));
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.TrashAlt))
        {
            inputText = string.Empty;
            state.Drafts[currentDraftKey] = string.Empty;
            plugin.SaveConfiguration();
            validationMessage = string.Empty;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.Clear"));
        }

        var canSend = chunks.Count > 0 && (state.LastChannel != ChatChannel.Whisper || !string.IsNullOrWhiteSpace(state.LastWhisperTarget));
        var accent = plugin.Configuration.SendAccentColor;
        var accentHovered = new Vector4(
            Math.Min(1f, accent.X + 0.08f),
            Math.Min(1f, accent.Y + 0.1f),
            Math.Min(1f, accent.Z + 0.08f),
            accent.W);

        ImGui.SameLine();
        ImGui.BeginDisabled(!canSend);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.PaperPlane, Loc.T("Compose.Send"), accent, accent, accentHovered))
        {
            TrySend(state);
        }

        ImGui.EndDisabled();
    }

    private void WrapSelection(string open, string close)
    {
        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, inputText.Length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, inputText.Length);

        if (start != end)
        {
            var selected = inputText.Substring(start, end - start);
            inputText = inputText[..start] + open + selected + close + inputText[end..];
        }
        else
        {
            var pos = Math.Clamp(cursorPos, 0, inputText.Length);
            inputText = inputText[..pos] + open + close + inputText[pos..];
        }

        plugin.GetCharacterState().Drafts[currentDraftKey] = inputText;
        plugin.SaveConfiguration();
    }

    private void DrawSnippetsPopup()
    {
        if (!ImGui.BeginPopup("##snippetsPopup"))
        {
            return;
        }

        var snippets = plugin.GetCharacterState().Snippets;

        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##snippetSearch", Loc.T("Compose.SnippetSearchHint"), ref snippetSearchFilter, 100);

        if (snippets.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("Compose.NoSnippets"));
        }

        var removeIndex = -1;
        for (var i = 0; i < snippets.Count; i++)
        {
            if (snippetSearchFilter.Length > 0 && snippets[i].Name.IndexOf(snippetSearchFilter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            ImGui.PushID(i);
            if (ImGui.Selectable(snippets[i].Name))
            {
                inputText = string.IsNullOrEmpty(inputText) ? snippets[i].Text : inputText + " " + snippets[i].Text;
                plugin.GetCharacterState().Drafts[currentDraftKey] = inputText;
                plugin.SaveConfiguration();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
            {
                removeIndex = i;
            }

            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            snippets.RemoveAt(removeIndex);
            plugin.SaveConfiguration();
        }

        ImGui.Separator();
        ImGui.SetNextItemWidth(150);
        ImGui.InputText(Loc.T("Compose.SnippetNameLabel"), ref newSnippetName, 60);
        ImGui.SameLine();
        var canSave = !string.IsNullOrWhiteSpace(newSnippetName) && !string.IsNullOrWhiteSpace(inputText);
        ImGui.BeginDisabled(!canSave);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Save, Loc.T("Compose.SaveSnippet")))
        {
            snippets.Add(new Snippet { Name = newSnippetName, Text = inputText });
            plugin.SaveConfiguration();
            newSnippetName = string.Empty;
        }

        ImGui.EndDisabled();

        ImGui.EndPopup();
    }

    private void DrawHistoryPopup()
    {
        if (!ImGui.BeginPopup("##historyPopup"))
        {
            return;
        }

        var history = plugin.SentHistory;
        if (history.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("Compose.NoHistory"));
        }

        foreach (var text in history)
        {
            var preview = text.Length > 50 ? text.Substring(0, 50) + "..." : text;
            if (ImGui.Selectable(preview))
            {
                inputText = text;
                plugin.GetCharacterState().Drafts[currentDraftKey] = inputText;
                plugin.SaveConfiguration();
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.EndPopup();
    }

    private void DrawWhisperTarget(CharacterState state)
    {
        ImGui.SetNextItemWidth(-90);
        var target = state.LastWhisperTarget;
        if (ImGui.InputText(Loc.T("Whisper.TargetLabel"), ref target, 100))
        {
            state.LastWhisperTarget = target;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var currentTargetName = plugin.GetCurrentTargetPlayerName();
        ImGui.BeginDisabled(currentTargetName == null);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Crosshairs))
        {
            state.LastWhisperTarget = currentTargetName!;
            activeWhisperTabTarget = currentTargetName!;
            pendingTabForceSelect = currentTargetName;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Whisper.UseTarget"));
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Users))
        {
            ImGui.OpenPopup("##whisperSuggestions");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Whisper.Suggestions"));
        }

        if (ImGui.BeginPopup("##whisperSuggestions"))
        {
            var partyMembers = plugin.GetPartyMemberNames();
            var recents = state.RecentWhisperTargets;

            if (partyMembers.Count == 0 && recents.Count == 0)
            {
                ImGui.TextDisabled(Loc.T("Whisper.NoSuggestions"));
            }

            if (partyMembers.Count > 0)
            {
                ImGui.TextDisabled(Loc.T("Whisper.SuggestionsParty"));
                foreach (var name in partyMembers)
                {
                    if (ImGui.Selectable(name))
                    {
                        state.LastWhisperTarget = name;
                        activeWhisperTabTarget = name;
                        pendingTabForceSelect = name;
                        plugin.SaveConfiguration();
                        ImGui.CloseCurrentPopup();
                    }
                }
            }

            if (recents.Count > 0)
            {
                ImGui.TextDisabled(Loc.T("Whisper.SuggestionsRecent"));
                foreach (var name in recents)
                {
                    if (ImGui.Selectable(name))
                    {
                        state.LastWhisperTarget = name;
                        activeWhisperTabTarget = name;
                        pendingTabForceSelect = name;
                        plugin.SaveConfiguration();
                        ImGui.CloseCurrentPopup();
                    }
                }
            }

            ImGui.EndPopup();
        }
    }

    private void DrawWhisperTabs(CharacterState state)
    {
        if (state.RecentWhisperTargets.Count == 0)
        {
            return;
        }

        if (!ImGui.BeginTabBar("##whisperTabs", ImGuiTabBarFlags.FittingPolicyScroll))
        {
            return;
        }

        string? closedTarget = null;

        foreach (var targetName in state.RecentWhisperTargets)
        {
            // SetSelected is only passed on the frame we explicitly want to force a tab switch
            // (see pendingTabForceSelect), not every frame based on activeWhisperTabTarget -
            // otherwise it fights with the user's own clicks on a different tab and can never
            // "let go" of whichever tab happened to be first.
            var flags = targetName == pendingTabForceSelect ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            var open = true;
            if (ImGui.BeginTabItem(targetName, ref open, flags))
            {
                // IsItemActivated fires only on the frame this tab actually becomes active
                // (a real click), unlike BeginTabItem's return value which stays true for
                // whichever tab ImGui already considers current on every subsequent frame too.
                if (ImGui.IsItemActivated())
                {
                    state.LastWhisperTarget = targetName;
                    activeWhisperTabTarget = targetName;
                    plugin.SaveConfiguration();
                }

                ImGui.EndTabItem();
            }

            if (!open)
            {
                closedTarget = targetName;
            }
        }

        ImGui.EndTabBar();
        pendingTabForceSelect = null;

        if (closedTarget != null)
        {
            state.RecentWhisperTargets.Remove(closedTarget);
            if (activeWhisperTabTarget == closedTarget)
            {
                activeWhisperTabTarget = state.RecentWhisperTargets.Count > 0 ? state.RecentWhisperTargets[0] : string.Empty;
                pendingTabForceSelect = activeWhisperTabTarget.Length > 0 ? activeWhisperTabTarget : null;
            }

            plugin.SaveConfiguration();
        }
    }

    private static string ChannelLabel(ChatChannel channel) => channel switch
    {
        ChatChannel.Say => Loc.T("Channel.Say"),
        ChatChannel.Party => Loc.T("Channel.Party"),
        ChatChannel.Whisper => Loc.T("Channel.Whisper"),
        ChatChannel.Yell => Loc.T("Channel.Yell"),
        ChatChannel.Shout => Loc.T("Channel.Shout"),
        ChatChannel.FreeCompany => Loc.T("Channel.FreeCompany"),
        ChatChannel.Linkshell => Loc.T("Channel.Linkshell"),
        _ => channel.ToString(),
    };
}
