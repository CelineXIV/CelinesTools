using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CelinesChat.Windows.SettingsPages;

internal sealed class WhisperContactsPage
{
    private readonly Plugin plugin;
    private string selectedTarget = string.Empty;
    private string newNameInput = string.Empty;
    private string? statusMessage;

    public WhisperContactsPage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Draw()
    {
        ImGui.TextWrapped(Loc.T("WhisperContacts.Description"));
        ImGui.Spacing();

        var targets = plugin.ChatLog.GetWhisperLogTargets();
        if (targets.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("WhisperContacts.None"));
            return;
        }

        if (!targets.Contains(selectedTarget))
        {
            selectedTarget = string.Empty;
        }

        ImGui.SetNextItemWidth(240);
        if (ImGui.BeginCombo("##whisperContactSelect", selectedTarget.Length > 0 ? selectedTarget : Loc.T("WhisperContacts.Pick")))
        {
            foreach (var target in targets)
            {
                if (ImGui.Selectable(target, target == selectedTarget))
                {
                    selectedTarget = target;
                    statusMessage = null;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SetNextItemWidth(240);
        ImGui.InputTextWithHint("##whisperContactNewName", Loc.T("WhisperContacts.NewNameHint"), ref newNameInput, 64);

        var canApply = selectedTarget.Length > 0 && newNameInput.Trim().Length > 0;
        ImGui.BeginDisabled(!canApply);
        if (ImGui.Button(Loc.T("WhisperContacts.Apply")))
        {
            var newTarget = newNameInput.Trim();
            plugin.RenameWhisperTarget(selectedTarget, newTarget);
            statusMessage = Loc.T("WhisperContacts.Applied", selectedTarget, newTarget);
            selectedTarget = string.Empty;
            newNameInput = string.Empty;
        }

        ImGui.EndDisabled();

        if (statusMessage != null)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), statusMessage);
        }
    }
}
