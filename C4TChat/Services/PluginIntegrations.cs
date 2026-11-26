using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;
using MoodleTuple = (int Version, System.Guid Guid, int IconId, string Title, string Description, string CustomVfxPath, long ExpireTicks, int Type, int Stacks, int StackSteps, uint Modifiers, System.Guid ChainedStatus, int ChainTrigger, string Applier, string Dispeller, bool Permanent);

namespace C4TChat.Services;

internal sealed class PluginIntegrations
{
    internal sealed record HonorificTitle(string Text, bool IsPrefix, Vector4? Color);

    internal sealed record MoodleStatus(uint Icon, string Title, string Description, int Type, int Stacks);

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;

    private readonly ICallGateSubscriber<int, string> honorificTitle;
    private readonly ICallGateSubscriber<nint, List<MoodleTuple>> moodlesByPtr;
    private readonly ICallGateSubscriber<string, bool> lifestreamChangeWorld;
    private readonly ICallGateSubscriber<bool> lifestreamIsBusy;
    private readonly ICallGateSubscriber<string, bool> lifestreamSameDc;
    private readonly ICallGateSubscriber<string, bool> lifestreamCrossDc;

    private readonly Dictionary<string, bool> loaded = new(StringComparer.OrdinalIgnoreCase);
    private long loadedCheckedAt;
    private readonly HashSet<string> loggedFailures = new();

    public PluginIntegrations(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
        honorificTitle = pluginInterface.GetIpcSubscriber<int, string>("Honorific.GetCharacterTitle");
        moodlesByPtr = pluginInterface.GetIpcSubscriber<nint, List<MoodleTuple>>("Moodles.GetStatusManagerInfoByPtrV2");
        lifestreamChangeWorld = pluginInterface.GetIpcSubscriber<string, bool>("Lifestream.ChangeWorld");
        lifestreamIsBusy = pluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        lifestreamSameDc = pluginInterface.GetIpcSubscriber<string, bool>("Lifestream.CanVisitSameDC");
        lifestreamCrossDc = pluginInterface.GetIpcSubscriber<string, bool>("Lifestream.CanVisitCrossDC");
    }

    internal bool HonorificAvailable => IsLoaded("Honorific");

    internal bool MoodlesAvailable => IsLoaded("Moodles");

    internal bool LifestreamAvailable => IsLoaded("Lifestream");

    private bool IsLoaded(string internalName)
    {
        var now = Environment.TickCount64;
        if (now - loadedCheckedAt > 5000)
        {
            loadedCheckedAt = now;
            loaded.Clear();
            foreach (var plugin in pluginInterface.InstalledPlugins)
            {
                if (plugin.IsLoaded)
                {
                    loaded[plugin.InternalName] = true;
                }
            }
        }

        return loaded.ContainsKey(internalName);
    }

    internal HonorificTitle? GetTitle(IPlayerCharacter player)
    {
        if (!HonorificAvailable)
        {
            return null;
        }

        try
        {
            var json = honorificTitle.InvokeFunc(player.ObjectIndex);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var data = JObject.Parse(json);
            var text = data.Value<string>("Title");
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            Vector4? color = null;
            if (data["Color"] is JObject c)
            {
                color = new Vector4(c.Value<float>("X"), c.Value<float>("Y"), c.Value<float>("Z"), 1f);
            }

            return new HonorificTitle(text, data.Value<bool>("IsPrefix"), color);
        }
        catch (Exception ex)
        {
            LogOnce("honorific", ex);
            return null;
        }
    }

    internal List<MoodleStatus> GetMoodles(IPlayerCharacter player)
    {
        var result = new List<MoodleStatus>();
        if (!MoodlesAvailable)
        {
            return result;
        }

        try
        {
            var list = moodlesByPtr.InvokeFunc(player.Address);
            if (list == null)
            {
                return result;
            }

            foreach (var status in list)
            {
                var icon = status.IconId > 0 ? (uint)(status.IconId + Math.Max(0, status.Stacks - 1)) : 0u;
                result.Add(new MoodleStatus(icon, StripTags(status.Title), StripTags(status.Description), status.Type, status.Stacks));
            }
        }
        catch (Exception ex)
        {
            LogOnce("moodles", ex);
        }

        return result;
    }

    internal bool CanTravelTo(string world)
    {
        if (!LifestreamAvailable || string.IsNullOrEmpty(world))
        {
            return false;
        }

        try
        {
            return lifestreamSameDc.InvokeFunc(world) || lifestreamCrossDc.InvokeFunc(world);
        }
        catch (Exception ex)
        {
            LogOnce("lifestream-check", ex);
            return false;
        }
    }

    internal bool TravelTo(string world)
    {
        if (!LifestreamAvailable)
        {
            return false;
        }

        try
        {
            return !lifestreamIsBusy.InvokeFunc() && lifestreamChangeWorld.InvokeFunc(world);
        }
        catch (Exception ex)
        {
            LogOnce("lifestream-travel", ex);
            return false;
        }
    }

    private void LogOnce(string key, Exception ex)
    {
        if (loggedFailures.Add(key))
        {
            log.Warning(ex, $"[C4TChat] Anbindung ({key}) fehlgeschlagen.");
        }
    }

    private static string StripTags(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return System.Text.RegularExpressions.Regex.Replace(text, @"\[/?[a-zA-Z]+(=[^\]]*)?\]", string.Empty).Trim();
    }
}
