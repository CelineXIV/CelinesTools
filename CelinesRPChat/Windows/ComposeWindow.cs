using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace CelinesRPChat.Windows;

internal sealed class ComposeWindow : Window
{
    private const string GenericDraftKey = "*generic*";

    private readonly Plugin plugin;
    private string inputText = string.Empty;
    private string currentDraftKey = string.Empty;
    private string validationMessage = string.Empty;
    private string newSnippetName = string.Empty;

    private string activeWhisperTabTarget = string.Empty;

    private int selectionStart;
    private int selectionEnd;
    private int cursorPos;

    private string cachedInput = string.Empty;
    private int cachedMaxLength = -1;
    private List<string> cachedChunks = new();

    public ComposeWindow(Plugin plugin) : base(WindowTitles.Compose)
    {
        this.plugin = plugin;
        Size = new Vector2(560, 560);
        SizeCondition = ImGuiCond.FirstUseEver;

        var initialState = plugin.GetCharacterState();
        if (initialState.LastChannel == ChatChannel.Whisper)
        {
            activeWhisperTabTarget = initialState.LastWhisperTarget;
        }
    }

    public override void PreDraw()
    {
        ImGui.SetNextWindowBgAlpha(plugin.Configuration.WindowOpacity);
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(plugin.Configuration.FontScale);

        var config = plugin.Configuration;
        var state = plugin.GetCharacterState();

        SyncDraft(state);

        DrawChannelSelector(state);

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

        DrawToolbar(config);

        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextMultiline("##composeText", ref inputText, 4000, new Vector2(-1, 150), ImGuiInputTextFlags.CallbackAlways, TrackTextState))
        {
            state.Drafts[currentDraftKey] = inputText;
            plugin.SaveConfiguration();
        }

        HandleSendHotkey(state);

        ImGui.Separator();

        var chunks = GetChunks(inputText, config.MaxMessageLength);

        ImGui.TextUnformatted(Loc.T("Compose.PreviewHeader") + " " + Loc.T("Compose.MessageCount", chunks.Count));
        ImGui.Spacing();

        ImGui.BeginChild("##preview", new Vector2(-1, -85), true);
        for (var i = 0; i < chunks.Count; i++)
        {
            ColoredTextRenderer.Draw(chunks[i], config.DefaultTextColor, config.EmoteTextColor, config.OocTextColor, config.MentionColor);
            ImGui.TextDisabled(Loc.T("Compose.CharCount", chunks[i].Length, config.MaxMessageLength));
            if (i < chunks.Count - 1)
            {
                ImGui.Separator();
            }
        }
        ImGui.EndChild();

        DrawActionButtons(config, state, chunks);

        if (!string.IsNullOrEmpty(validationMessage))
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), validationMessage);
        }
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

    private void DrawToolbar(Configuration config)
    {
        if (ImGui.Button(Loc.T("Compose.WrapEmote")))
        {
            WrapSelection("*", "*");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.WrapHint"));
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Compose.WrapOoc")))
        {
            WrapSelection("((", "))");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Compose.WrapHint"));
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Compose.Snippets")))
        {
            ImGui.OpenPopup("##snippetsPopup");
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Compose.History")))
        {
            ImGui.OpenPopup("##historyPopup");
        }

        DrawSnippetsPopup(config);
        DrawHistoryPopup();
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

    private void DrawSnippetsPopup(Configuration config)
    {
        if (!ImGui.BeginPopup("##snippetsPopup"))
        {
            return;
        }

        if (config.Snippets.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("Compose.NoSnippets"));
        }

        var removeIndex = -1;
        for (var i = 0; i < config.Snippets.Count; i++)
        {
            ImGui.PushID(i);
            if (ImGui.Selectable(config.Snippets[i].Name))
            {
                inputText = string.IsNullOrEmpty(inputText) ? config.Snippets[i].Text : inputText + " " + config.Snippets[i].Text;
                plugin.GetCharacterState().Drafts[currentDraftKey] = inputText;
                plugin.SaveConfiguration();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Compose.RemoveSnippet")))
            {
                removeIndex = i;
            }

            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            config.Snippets.RemoveAt(removeIndex);
            plugin.SaveConfiguration();
        }

        ImGui.Separator();
        ImGui.SetNextItemWidth(150);
        ImGui.InputText(Loc.T("Compose.SnippetNameLabel"), ref newSnippetName, 60);
        ImGui.SameLine();
        var canSave = !string.IsNullOrWhiteSpace(newSnippetName) && !string.IsNullOrWhiteSpace(inputText);
        ImGui.BeginDisabled(!canSave);
        if (ImGui.Button(Loc.T("Compose.SaveSnippet")))
        {
            config.Snippets.Add(new Snippet { Name = newSnippetName, Text = inputText });
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

    private void DrawActionButtons(Configuration config, CharacterState state, List<string> chunks)
    {
        if (plugin.IsSending)
        {
            var done = plugin.SendTotal - plugin.SendRemaining;
            ImGui.TextUnformatted(Loc.T("Compose.Sending", done, plugin.SendTotal));
            ImGui.SameLine();
            if (ImGui.Button(Loc.T("Compose.Cancel")))
            {
                plugin.CancelSending();
            }

            return;
        }

        var canSend = chunks.Count > 0 && (state.LastChannel != ChatChannel.Whisper || !string.IsNullOrWhiteSpace(state.LastWhisperTarget));

        ImGui.BeginDisabled(!canSend);
        if (ImGui.Button(Loc.T("Compose.Send")))
        {
            TrySend(state);
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Compose.Copy")))
        {
            ImGui.SetClipboardText(inputText);
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Compose.Clear")))
        {
            inputText = string.Empty;
            state.Drafts[currentDraftKey] = string.Empty;
            plugin.SaveConfiguration();
            validationMessage = string.Empty;
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Compose.Settings")))
        {
            plugin.ToggleSettingsWindow();
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Compose.OpenLog")))
        {
            plugin.ToggleChatLogWindow();
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Compose.Changelog")))
        {
            ImGui.OpenPopup("##changelogPopup");
        }

        DrawChangelogPopup();
    }

    private static void DrawChangelogPopup()
    {
        if (!ImGui.BeginPopup("##changelogPopup"))
        {
            return;
        }

        for (var i = Changelog.Entries.Length - 1; i >= 0; i--)
        {
            var (version, text) = Changelog.Entries[i];
            ImGui.TextUnformatted($"{version}: {text}");
        }

        ImGui.EndPopup();
    }

    private void DrawWhisperTarget(CharacterState state)
    {
        ImGui.SetNextItemWidth(-160);
        var target = state.LastWhisperTarget;
        if (ImGui.InputText(Loc.T("Whisper.TargetLabel"), ref target, 100))
        {
            state.LastWhisperTarget = target;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var currentTargetName = plugin.GetCurrentTargetPlayerName();
        ImGui.BeginDisabled(currentTargetName == null);
        if (ImGui.Button(Loc.T("Whisper.UseTarget")))
        {
            state.LastWhisperTarget = currentTargetName!;
            activeWhisperTabTarget = currentTargetName!;
            plugin.SaveConfiguration();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Whisper.Suggestions")))
        {
            ImGui.OpenPopup("##whisperSuggestions");
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

        foreach (var targetName in state.RecentWhisperTargets)
        {
            var flags = targetName == activeWhisperTabTarget ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (ImGui.BeginTabItem(targetName, flags))
            {
                state.LastWhisperTarget = targetName;
                activeWhisperTabTarget = targetName;
                ImGui.EndTabItem();
            }
        }

        ImGui.EndTabBar();
    }

    private void DrawChannelSelector(CharacterState state)
    {
        ImGui.TextUnformatted(Loc.T("Channel.Label"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);

        var currentLabel = ChannelLabel(state.LastChannel);
        if (ImGui.BeginCombo("##channel", currentLabel))
        {
            foreach (var channel in AllChannels)
            {
                var isSelected = channel == state.LastChannel;
                if (ImGui.Selectable(ChannelLabel(channel), isSelected))
                {
                    state.LastChannel = channel;
                    activeWhisperTabTarget = channel == ChatChannel.Whisper ? state.LastWhisperTarget : string.Empty;
                    plugin.SaveConfiguration();
                }
            }

            ImGui.EndCombo();
        }
    }

    private static readonly ChatChannel[] AllChannels =
    {
        ChatChannel.Say, ChatChannel.Party, ChatChannel.Whisper,
        ChatChannel.Yell, ChatChannel.Shout, ChatChannel.FreeCompany, ChatChannel.Linkshell,
    };

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
