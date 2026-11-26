using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using Action = System.Action;

namespace C4TChat.Windows;

internal sealed unsafe class SocialWindow : Window
{
    internal enum SocialTab
    {
        Party,
        Friends,
        Search,
    }

    private sealed class Row
    {
        public ulong ContentId;
        public string Name = string.Empty;
        public ushort HomeWorld;
        public ushort CurrentWorld;
        public ushort Location;
        public byte Job;
        public ulong State;
        public string FcTag = string.Empty;
        public byte Languages;
        public byte MainLanguage;
        public sbyte Group;
        public bool OtherServer;

        public bool Stale;
    }

    private sealed class StatusInfo
    {
        public uint Icon;
        public int Priority;
        public bool List;
        public string Name = string.Empty;
    }

    private const int OfflineBit = 10;
    private const int NotFoundBit = 9;
    private const int PartyLeaderBit = 36;
    private const int PartyLeaderCrossWorldBit = 38;
    private const int AllianceLeaderBit = 33;

    private const long InfoRequestGapMs = 3500;
    private const long InfoAnswerWaitMs = 3500;
    private const long InfoRecheckMs = 5 * 60 * 1000;
    private readonly Dictionary<ulong, long> infoRequested = new();
    private long lastInfoRequest;

    private static readonly string[] GroupSymbols = { "", "★", "●", "▲", "◆", "♥", "♠", "♣" };
    private static readonly string[] LanguageLetters = { "J", "E", "D", "F" };
    private static readonly string[] SortKeys = { "Social.SortOnline", "Social.SortName", "Social.SortGroup", "Social.SortLocation" };

    private static readonly (string Key, uint Icon)[] Roles =
    {
        ("Social.RoleTank", 62581),
        ("Social.RoleHealer", 62582),
        ("Social.RoleMelee", 62583),
        ("Social.RolePhysRanged", 62586),
        ("Social.RoleMagicRanged", 62587),
    };

    private readonly Plugin plugin;
    private readonly Dictionary<int, StatusInfo> statuses = new();
    private readonly Dictionary<ushort, string> worldNames = new();
    private readonly Dictionary<ushort, string> placeNames = new();
    private readonly Dictionary<byte, string> jobNames = new();
    private readonly Dictionary<byte, int> jobRoles = new();

    private SocialTab? requestedTab;
    private SocialTab activeTab = SocialTab.Friends;
    private ulong selectedContentId;
    private string friendFilter = string.Empty;
    private int friendGroupFilter = -1;
    private bool friendsOnlineOnly;
    private int friendSort;
    private long lastFriendRequest;
    private long lastPartyRequest;
    private bool confirmLeave;
    private bool confirmDisband;
    private int pushedColors;

    private bool friendsLoading;
    private long friendsLoadingSince;
    private int lastLiveFriendCount = -1;
    private long liveFriendCountChangedAt;

    public SocialWindow(Plugin plugin)
        : base(Loc.T("Social.Title") + "###celineschatSocial", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(520, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(360, 260), MaximumSize = new Vector2(4000, 4000) };
    }

    internal SocialTab ActiveTab => activeTab;

    internal void Show(SocialTab tab, bool toggle)
    {
        if (toggle && IsOpen)
        {
            IsOpen = false;
            return;
        }

        requestedTab = tab;
        IsOpen = true;
        BringToFront();
    }

    public override void OnOpen()
    {
        lastFriendRequest = 0;
        lastPartyRequest = 0;
    }

    private float currentOpacity = 1f;

    public override void PreDraw()
    {
        var config = plugin.Configuration;
        currentOpacity = config.SocialWindowOpacity;
        ImGui.SetNextWindowBgAlpha(currentOpacity);
        pushedColors = C4tTheme.PushColors(config);
    }

    public override void PostDraw()
    {
        C4tTheme.PopColors(pushedColors);
        pushedColors = 0;
    }

    public override void Draw()
    {
        var config = plugin.Configuration;
        C4tTheme.DrawDecorations(config, currentOpacity);

        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 6f * ImGuiHelpers.GlobalScale)
            .Push(ImGuiStyleVar.PopupRounding, 6f * ImGuiHelpers.GlobalScale)
            .Push(ImGuiStyleVar.ChildRounding, 6f * ImGuiHelpers.GlobalScale)
            .Push(ImGuiStyleVar.FrameBorderSize, 1f);
        using var frameBorder = ImRaii.PushColor(ImGuiCol.Border, ImGui.GetStyle().Colors[(int)ImGuiCol.Separator]);

        if (requestedTab is { } requested)
        {
            SwitchTab(requested);
            requestedTab = null;
        }

        DrawTabStrip(config);
        ImGui.Spacing();

        switch (activeTab)
        {
            case SocialTab.Party:
                DrawParty();
                break;
            case SocialTab.Search:
                DrawSearch();
                break;
            default:
                DrawFriends();
                break;
        }
    }

    private void SwitchTab(SocialTab tab)
    {
        if (activeTab != tab)
        {
            activeTab = tab;
            selectedContentId = 0;
        }
    }

    private void DrawTabStrip(Configuration config)
    {
        C4tTheme.DrawLogoItem(plugin);

        using (ImRaii.PushStyle(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f)))
        {
            DrawStripTab(config, SocialTab.Party, FontAwesomeIcon.Users, Loc.T("Social.TabParty"));
            ImGui.SameLine(0, 2f);
            DrawStripTab(config, SocialTab.Friends, FontAwesomeIcon.UserFriends, Loc.T("Social.TabFriends"));
            ImGui.SameLine(0, 2f);
            DrawStripTab(config, SocialTab.Search, FontAwesomeIcon.Search, Loc.T("Social.TabSearch"));
        }

        DrawViewOptions(config);

        var min = ImGui.GetItemRectMax() with { X = ImGui.GetWindowPos().X + ImGui.GetStyle().WindowPadding.X };
        var lineY = ImGui.GetItemRectMax().Y + 1f;
        var max = new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowSize().X - ImGui.GetStyle().WindowPadding.X, lineY);
        ImGui.GetWindowDrawList().AddLine(min with { Y = lineY }, max, ImGui.GetColorU32(ImGuiCol.Separator));
    }

    private void DrawViewOptions(Configuration config)
    {
        var buttonWidth = ImGui.GetFrameHeight();
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - buttonWidth);
        if (ImGuiComponents.IconButton(config.SocialCardView ? FontAwesomeIcon.ThLarge : FontAwesomeIcon.List))
        {
            ImGui.OpenPopup("##socialView");
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Social.ViewOptions"));
        }

        using var popup = ImRaii.Popup("##socialView");
        if (!popup.Success)
        {
            return;
        }

        if (ImGui.RadioButton(Loc.T("Social.ViewCards"), config.SocialCardView))
        {
            config.SocialCardView = true;
            plugin.SaveConfiguration();
        }

        if (ImGui.RadioButton(Loc.T("Social.ViewList"), !config.SocialCardView))
        {
            config.SocialCardView = false;
            plugin.SaveConfiguration();
        }

        ImGui.Separator();
        var grey = config.SocialGreyOffline;
        if (ImGui.Checkbox(Loc.T("Social.GreyOffline"), ref grey))
        {
            config.SocialGreyOffline = grey;
            plugin.SaveConfiguration();
        }

        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        var opacity = config.SocialWindowOpacity * 100f;
        if (ImGui.SliderFloat(Loc.T("Social.Opacity"), ref opacity, 20f, 100f, "%.0f %%"))
        {
            config.SocialWindowOpacity = Math.Clamp(opacity / 100f, 0.2f, 1f);
            plugin.SaveConfiguration();
        }
    }

    private void DrawStripTab(Configuration config, SocialTab tab, FontAwesomeIcon icon, string label)
    {
        var selected = activeTab == tab;
        var size = new Vector2(ChatWindow.StripTabWidth(label, 0, false, icon), ImGui.GetFrameHeight() + (2f * ImGuiHelpers.GlobalScale));
        if (ChatWindow.DrawStripTab(label, "##stripTab" + tab, selected, size, 0, false, null, null, config, icon) && !selected)
        {
            SwitchTab(tab);
        }
    }

    private void DrawParty()
    {
        var proxy = (InfoProxyCommonList*)InfoProxyPartyMember.Instance();
        RequestThrottled(proxy, ref lastPartyRequest);
        var rows = Read(proxy);

        var self = rows.Find(r => r.ContentId == plugin.CurrentCharacterKey);
        var inParty = rows.Count > 1;
        var isLeader = self != null && (HasBit(self.State, PartyLeaderBit) || HasBit(self.State, PartyLeaderCrossWorldBit) || HasBit(self.State, AllianceLeaderBit));

        DrawSectionHeader(inParty ? Loc.T("Social.PartyCount", rows.Count) : Loc.T("Social.Solo"));

        var footer = (ImGui.GetFrameHeightWithSpacing() * 2) + ImGui.GetStyle().ItemSpacing.Y;
        DrawList("##party", rows, footer, sectioned: false, row =>
        {
            DrawCommonActions(row, allowInvite: false);
            if (isLeader && row.ContentId != plugin.CurrentCharacterKey)
            {
                ImGui.Separator();
                if (ImGui.Selectable(Loc.T("Social.Promote")))
                {
                    plugin.SendRawCommand("/promote " + row.Name);
                }

                if (ImGui.Selectable(Loc.T("Social.Kick")))
                {
                    plugin.SendRawCommand("/kick " + row.Name);
                }
            }
        });

        DrawPartyBonus(rows, inParty);

        if (inParty)
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.SignOutAlt, Loc.T("Social.Leave")))
            {
                confirmLeave = true;
            }

            if (isLeader)
            {
                ImGui.SameLine();
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.UsersSlash, Loc.T("Social.Disband")))
                {
                    confirmDisband = true;
                }
            }

            ImGui.SameLine();
        }

        DrawOriginalButton(SocialTab.Party);

        DrawConfirm(ref confirmLeave, "##confirmLeave", Loc.T("Social.LeaveConfirm"), () => InfoProxyPartyMember.Instance()->LeaveParty());
        DrawConfirm(ref confirmDisband, "##confirmDisband", Loc.T("Social.DisbandConfirm"), () => InfoProxyPartyMember.Instance()->DisbandParty());
    }

    private int RoleOf(byte job)
    {
        if (!jobRoles.TryGetValue(job, out var role))
        {
            role = -1;
            if (plugin.DataManager.GetExcelSheet<ClassJob>().GetRowOrDefault(job) is { } row)
            {
                role = row.Role switch
                {
                    1 => 0,
                    4 => 1,
                    2 => 2,
                    3 => row.PrimaryStat == 4 ? 4 : 3,
                    _ => -1,
                };
            }

            jobRoles[job] = role;
        }

        return role;
    }

    private void DrawPartyBonus(List<Row> rows, bool inParty)
    {
        var present = new bool[Roles.Length];
        foreach (var row in rows)
        {
            var role = RoleOf(row.Job);
            if (role >= 0)
            {
                present[role] = true;
            }
        }

        var bonus = 0;
        if (inParty)
        {
            foreach (var has in present)
            {
                bonus += has ? 1 : 0;
            }
        }

        var size = new Vector2(ImGui.GetTextLineHeight() * 1.15f);
        var drawList = ImGui.GetWindowDrawList();
        for (var i = 0; i < Roles.Length; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine(0, 3f * ImGuiHelpers.GlobalScale);
            }

            var min = ImGui.GetCursorScreenPos();
            ImGui.Dummy(size);
            if (plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(Roles[i].Icon)).GetWrapOrDefault() is { } wrap)
            {
                var tint = present[i] && inParty ? Vector4.One : new Vector4(1f, 1f, 1f, 0.25f);
                drawList.AddImageRounded(wrap.Handle, min, min + size, Vector2.Zero, Vector2.One, ImGui.GetColorU32(tint), 4f * ImGuiHelpers.GlobalScale);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T(Roles[i].Key));
            }
        }

        ImGui.SameLine(0, 10f * ImGuiHelpers.GlobalScale);
        ImGui.TextColored(C4tTheme.ActiveMarkerColor(plugin.Configuration), Loc.T("Social.PartyBonus"));
        ImGui.SameLine();
        ImGui.TextUnformatted("+" + bonus + "%");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Social.PartyBonusValue", bonus));
        }
    }

    private static void DrawConfirm(ref bool request, string id, string text, Action onYes)
    {
        if (request)
        {
            ImGui.OpenPopup(id);
            request = false;
        }

        using var popup = ImRaii.Popup(id);
        if (!popup.Success)
        {
            return;
        }

        ImGui.TextUnformatted(text);
        if (ImGui.Button(Loc.T("Social.Yes")))
        {
            onYes();
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Social.No")))
        {
            ImGui.CloseCurrentPopup();
        }
    }

    private void DrawFriends()
    {
        var proxy = (InfoProxyCommonList*)InfoProxyFriendList.Instance();
        if (RequestThrottled(proxy, ref lastFriendRequest))
        {
            friendsLoading = true;
            friendsLoadingSince = Environment.TickCount64;
        }

        var all = FriendsWithCache(Read(proxy));
        CheckOtherDataCenters(all);

        var online = 0;
        var rows = new List<Row>(all.Count);
        foreach (var row in all)
        {
            var isOnline = SectionOf(row) <= 1;
            if (isOnline)
            {
                online++;
            }

            if ((friendsOnlineOnly && !isOnline)
                || (friendGroupFilter >= 0 && row.Group != friendGroupFilter)
                || (friendFilter.Length > 0 && !row.Name.Contains(friendFilter, StringComparison.OrdinalIgnoreCase)
                    && !(Nickname(row) ?? string.Empty).Contains(friendFilter, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            rows.Add(row);
        }

        SortFriends(rows);
        DrawFriendToolbar(proxy);

        var footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        DrawList("##friends", rows, footer, sectioned: friendSort == 0, row =>
        {
            DrawCommonActions(row, allowInvite: true);
            if (ImGui.Selectable(Loc.T("ChatLog.EstateTeleportAction")))
            {
                AgentFriendlist.Instance()->OpenFriendEstateTeleportation(row.ContentId);
            }

            if (ImGui.Selectable(Loc.T("Social.RequestStatus")))
            {
                AgentFriendlist.Instance()->RequestFriendInfo(row.ContentId);
            }

            if (ImGui.Selectable(Loc.T("Social.SearchInfo")))
            {
                plugin.RunFriendMenuItem(row.ContentId, row.Name, Plugin.FriendMenuSearchInfo);
            }

            if (ImGui.Selectable(Loc.T("Social.AssignGroup")))
            {
                plugin.RunFriendMenuItem(row.ContentId, row.Name, Plugin.FriendMenuAssignGroup);
            }

            ImGui.Separator();
            using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(1f, 0.55f, 0.5f, 1f)))
            {
                if (ImGui.Selectable(Loc.T("Social.RemoveFriend")))
                {
                    plugin.RunFriendMenuItem(row.ContentId, row.Name, Plugin.FriendMenuRemove);
                }
            }

            ImGui.Separator();
            if (ImGui.Selectable(Loc.T("Social.MoreInOriginal")))
            {
                plugin.OpenNativeSocial(SocialTab.Friends);
            }
        });

        DrawOriginalButton(SocialTab.Friends);
        ImGui.SameLine();
        ImGui.TextDisabled(Loc.T("Social.FriendCount", online, all.Count));
        if (friendsLoading)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("· " + Loc.T("Social.Loading"));
        }
    }

    private void DrawFriendToolbar(InfoProxyCommonList* proxy)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var refreshWidth = ImGui.GetFrameHeight();
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        ImGui.SetNextItemWidth(Math.Max(100f * scale, ImGui.GetContentRegionAvail().X - (230f * scale) - refreshWidth - (spacing * 4)));
        ImGui.InputTextWithHint("##friendFilter", Loc.T("Social.FilterHint"), ref friendFilter, 40);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(70f * scale);
        using (var combo = ImRaii.Combo("##friendGroup", friendGroupFilter < 0 ? Loc.T("Social.AllGroups") : GroupLabel(friendGroupFilter)))
        {
            if (combo.Success)
            {
                if (ImGui.Selectable(Loc.T("Social.AllGroups"), friendGroupFilter < 0))
                {
                    friendGroupFilter = -1;
                }

                for (var g = 0; g < GroupSymbols.Length; g++)
                {
                    if (ImGui.Selectable(GroupLabel(g) + "##group" + g, friendGroupFilter == g))
                    {
                        friendGroupFilter = g;
                    }
                }
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Social.SortGroup"));
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(110f * scale);
        using (var combo = ImRaii.Combo("##friendSort", Loc.T(SortKeys[friendSort])))
        {
            if (combo.Success)
            {
                for (var i = 0; i < SortKeys.Length; i++)
                {
                    if (ImGui.Selectable(Loc.T(SortKeys[i]), friendSort == i))
                    {
                        friendSort = i;
                    }
                }
            }
        }

        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, C4tTheme.ActiveMarkerColor(plugin.Configuration), friendsOnlineOnly))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Eye))
            {
                friendsOnlineOnly = !friendsOnlineOnly;
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Social.OnlineOnly"));
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.SyncAlt))
        {
            lastFriendRequest = 0;
            if (RequestThrottled(proxy, ref lastFriendRequest))
            {
                friendsLoading = true;
                friendsLoadingSince = Environment.TickCount64;
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Social.Refresh"));
        }
    }

    private List<Row> FriendsWithCache(List<Row> live)
    {
        var now = Environment.TickCount64;
        if (live.Count != lastLiveFriendCount)
        {
            lastLiveFriendCount = live.Count;
            liveFriendCountChangedAt = now;
        }

        var state = plugin.GetCharacterState();
        var cache = state.FriendCache;

        if (friendsLoading
            && ((live.Count > 0 && now - liveFriendCountChangedAt > 1500 && live.Count >= cache.Count)
                || (live.Count > 0 && now - liveFriendCountChangedAt > 4000)
                || now - friendsLoadingSince > 20_000))
        {
            friendsLoading = false;
        }

        if (!friendsLoading && live.Count > 0)
        {
            if (CacheDiffers(cache, live))
            {
                state.FriendCache = live.ConvertAll(ToCached);
                plugin.SaveConfiguration();
            }

            return live;
        }

        if (cache.Count == 0)
        {
            return live;
        }

        var merged = new List<Row>(Math.Max(live.Count, cache.Count));
        var seen = new HashSet<ulong>();
        Dictionary<ulong, CachedFriend>? byId = null;
        foreach (var row in live)
        {
            if (!IsOnline(row.State))
            {
                byId ??= BuildCacheIndex(cache);
                if (byId.TryGetValue(row.ContentId, out var cachedRow) && IsOnline(cachedRow.State))
                {
                    var keep = FromCached(cachedRow);
                    keep.Stale = true;
                    merged.Add(keep);
                    seen.Add(row.ContentId);
                    continue;
                }
            }

            merged.Add(row);
            seen.Add(row.ContentId);
        }

        foreach (var cached in cache)
        {
            if (seen.Add(cached.ContentId))
            {
                var row = FromCached(cached);
                row.Stale = true;
                merged.Add(row);
            }
        }

        return merged;
    }

    private static Dictionary<ulong, CachedFriend> BuildCacheIndex(List<CachedFriend> cache)
    {
        var index = new Dictionary<ulong, CachedFriend>(cache.Count);
        foreach (var c in cache)
        {
            index.TryAdd(c.ContentId, c);
        }

        return index;
    }

    private static bool CacheDiffers(List<CachedFriend> cache, List<Row> live)
    {
        if (cache.Count != live.Count)
        {
            return true;
        }

        for (var i = 0; i < live.Count; i++)
        {
            var c = cache[i];
            var l = live[i];
            if (c.ContentId != l.ContentId || c.State != l.State || c.Location != l.Location || c.Job != l.Job
                || c.CurrentWorld != l.CurrentWorld || c.Group != l.Group || c.Name != l.Name || c.FcTag != l.FcTag)
            {
                return true;
            }
        }

        return false;
    }

    private static CachedFriend ToCached(Row r) => new()
    {
        ContentId = r.ContentId,
        Name = r.Name,
        HomeWorld = r.HomeWorld,
        CurrentWorld = r.CurrentWorld,
        Location = r.Location,
        Job = r.Job,
        State = r.State,
        FcTag = r.FcTag,
        Languages = r.Languages,
        MainLanguage = r.MainLanguage,
        Group = r.Group,
    };

    private static Row FromCached(CachedFriend c) => new()
    {
        ContentId = c.ContentId,
        Name = c.Name,
        HomeWorld = c.HomeWorld,
        CurrentWorld = c.CurrentWorld,
        Location = c.Location,
        Job = c.Job,
        State = c.State,
        FcTag = c.FcTag,
        Languages = c.Languages,
        MainLanguage = c.MainLanguage,
        Group = c.Group,
    };

    private void SortFriends(List<Row> rows)
    {
        Comparison<Row> byName = (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        rows.Sort(friendSort switch
        {
            1 => byName,
            2 => (a, b) => a.Group != b.Group ? (a.Group == 0 ? 1 : b.Group == 0 ? -1 : a.Group.CompareTo(b.Group)) : OnlineFirst(a, b, byName),
            3 => (a, b) =>
            {
                var c = string.Compare(LocationText(a), LocationText(b), StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : byName(a, b);
            },
            _ => (a, b) => OnlineFirst(a, b, byName),
        });
    }

    private int OnlineFirst(Row a, Row b, Comparison<Row> then)
    {
        var sa = SectionOf(a);
        var sb = SectionOf(b);
        return sa != sb ? sa.CompareTo(sb) : then(a, b);
    }

    private static bool HasNoDetails(Row row) => row.Job == 0 && row.Location == 0;

    private bool IsUnconfirmed(Row row)
    {
        if (activeTab != SocialTab.Friends || row.Stale || !IsOnline(row.State) || !HasNoDetails(row))
        {
            return false;
        }

        var now = Environment.TickCount64;
        return !infoRequested.TryGetValue(row.ContentId, out var askedAt) || now - askedAt < InfoAnswerWaitMs || now - askedAt > InfoRecheckMs;
    }

    private bool IsElsewhere(Row row)
    {
        var here = plugin.CurrentWorldId;
        return IsOnline(row.State) && here != 0 && row.CurrentWorld != 0 && row.CurrentWorld != here;
    }

    private int SectionOf(Row row) => IsUnconfirmed(row) ? 2 : !IsOnline(row.State) ? 3 : IsElsewhere(row) ? 1 : 0;

    private void CheckOtherDataCenters(List<Row> rows)
    {
        var now = Environment.TickCount64;
        if (now - lastInfoRequest < InfoRequestGapMs)
        {
            return;
        }

        foreach (var row in rows)
        {
            if (!IsUnconfirmed(row))
            {
                continue;
            }

            if (infoRequested.TryGetValue(row.ContentId, out var askedAt) && now - askedAt < InfoRecheckMs)
            {
                continue;
            }

            var agent = AgentFriendlist.Instance();
            if (agent == null)
            {
                return;
            }

            agent->RequestFriendInfo(row.ContentId);
            infoRequested[row.ContentId] = now;
            lastInfoRequest = now;
            return;
        }
    }

    private static string GroupLabel(int group) => group == 0 ? Loc.T("Social.NoGroup") : GroupSymbols[group];

    private void DrawSearch()
    {
        var rows = Read((InfoProxyCommonList*)InfoProxySearch.Instance());

        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
        {
            ImGui.TextWrapped(Loc.T("Social.SearchHint"));
        }

        var footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        DrawList("##search", rows, footer, sectioned: false, row => DrawCommonActions(row, allowInvite: true));

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Search, Loc.T("Social.NewSearch")))
        {
            plugin.OpenNativeSocial(SocialTab.Search, openSearchConditions: true);
        }

        ImGui.SameLine();
        ImGui.TextDisabled(Loc.T("Social.SearchCount", rows.Count));
    }

    private void DrawOriginalButton(SocialTab tab)
    {
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.ExternalLinkAlt, Loc.T("Social.OpenOriginal")))
        {
            plugin.OpenNativeSocial(tab);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Social.OpenOriginalTooltip"));
        }
    }

    private void DrawCommonActions(Row row, bool allowInvite)
    {
        var world = WorldName(row.HomeWorld);
        var self = row.ContentId == plugin.CurrentCharacterKey;

        if (!self && ImGui.Selectable(Loc.T("ChatLog.WhisperAction")))
        {
            plugin.OpenWhisperTab(world != null ? row.Name + "@" + world : row.Name);
        }

        if (!self && ImGui.Selectable(Loc.T("ChatLog.PlayerCardAction")))
        {
            plugin.OpenPlayerCard(row.Name, world, row.ContentId);
        }

        if (allowInvite && !self && ImGui.Selectable(Loc.T("Social.Invite")))
        {
            plugin.InviteByContentId(row.Name, row.HomeWorld, row.ContentId);
        }

        if (ImGui.Selectable(Loc.T("Social.Plate")))
        {
            plugin.OpenAdventurerPlate(row.Name, world, row.ContentId);
        }
    }

    private void DrawSectionHeader(string text)
    {
        var accent = C4tTheme.ActiveMarkerColor(plugin.Configuration);
        ImGui.TextColored(accent, text.ToUpperInvariant());
    }

    private bool DrawSection(string text, int count, Vector4 color, bool foldable, bool collapsed)
    {
        var scale = ImGuiHelpers.GlobalScale;
        if (ImGui.GetCursorPosY() > 1f)
        {
            ImGui.Dummy(new Vector2(1f, 6f * scale));
        }

        var label = (foldable ? (collapsed ? "▸ " : "▾ ") : string.Empty) + text + "  ·  " + count;
        var pos = ImGui.GetCursorScreenPos();
        var textSize = ImGui.CalcTextSize(label);
        var width = ImGui.GetContentRegionAvail().X;

        var clicked = false;
        if (foldable)
        {
            clicked = ImGui.InvisibleButton("##section" + text, new Vector2(width, textSize.Y + (4f * scale)));
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }
        }
        else
        {
            ImGui.Dummy(new Vector2(width, textSize.Y + (4f * scale)));
        }

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddText(pos, ImGui.GetColorU32(color), label);
        var lineY = pos.Y + (textSize.Y / 2f) + 1f;
        drawList.AddLine(new Vector2(pos.X + textSize.X + (8f * scale), lineY), new Vector2(pos.X + width, lineY), ImGui.GetColorU32(color with { W = 0.3f }), 1f);
        return clicked;
    }

    private void DrawList(string id, List<Row> rows, float footerHeight, bool sectioned, Action<Row> contextMenu)
    {
        var config = plugin.Configuration;
        var height = Math.Max(60f, ImGui.GetContentRegionAvail().Y - footerHeight);
        using var child = ImRaii.Child(id, new Vector2(0, height), false);
        if (!child.Success)
        {
            return;
        }

        if (rows.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled(Loc.T("Social.Empty"));
            return;
        }

        var counts = new int[4];
        foreach (var row in rows)
        {
            counts[SectionOf(row)]++;
        }

        var accent = C4tTheme.ActiveMarkerColor(config);
        var muted = ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
        int? lastSection = null;
        var skipOffline = false;

        foreach (var row in rows)
        {
            var section = SectionOf(row);
            if (sectioned && lastSection != section)
            {
                lastSection = section;
                switch (section)
                {
                    case 0:
                        DrawSection(Loc.T("Social.SectionHere", WorldName(plugin.CurrentWorldId) ?? "?"), counts[0], accent, foldable: false, collapsed: false);
                        break;
                    case 1:
                        DrawSection(Loc.T("Social.SectionElsewhere"), counts[1], accent with { W = 0.7f }, foldable: false, collapsed: false);
                        break;
                    case 2:
                        DrawSection(Loc.T("Social.SectionChecking"), counts[2], muted with { W = 1f }, foldable: false, collapsed: false);
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(Loc.T("Social.SectionCheckingTooltip"));
                        }

                        break;
                    default:
                        if (DrawSection(Loc.T("Social.SectionOffline"), counts[3], muted, foldable: true, config.SocialOfflineCollapsed))
                        {
                            config.SocialOfflineCollapsed = !config.SocialOfflineCollapsed;
                            plugin.SaveConfiguration();
                        }

                        skipOffline = config.SocialOfflineCollapsed;
                        break;
                }
            }

            if (skipOffline && section == 3)
            {
                continue;
            }

            DrawRow(row, contextMenu, config.SocialCardView);
        }
    }

    private void DrawRow(Row row, Action<Row> contextMenu, bool card)
    {
        var config = plugin.Configuration;
        var scale = ImGuiHelpers.GlobalScale;
        var lineHeight = ImGui.GetTextLineHeight();
        var avatarSize = card ? MathF.Round(lineHeight * 2.1f) : MathF.Round(lineHeight * 1.25f);
        var rowHeight = avatarSize + ((card ? 10f : 5f) * scale);
        var gap = (card ? 4f : 1f) * scale;
        var style = ImGui.GetStyle();
        var drawList = ImGui.GetWindowDrawList();
        var text = style.Colors[(int)ImGuiCol.Text];
        var muted = style.Colors[(int)ImGuiCol.TextDisabled];
        var accent = C4tTheme.ActiveMarkerColor(config);
        var unconfirmed = IsUnconfirmed(row);
        var online = IsOnline(row.State);

        using var rowId = ImRaii.PushId(row.ContentId.ToString());
        var min = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var size = new Vector2(width, rowHeight);
        var visible = ImGui.IsRectVisible(size);

        ImGui.InvisibleButton("##row", size);
        var hovered = ImGui.IsItemHovered();
        var world = WorldName(row.HomeWorld);
        var fullName = world != null ? row.Name + "@" + world : row.Name;

        var self = row.ContentId == plugin.CurrentCharacterKey;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            selectedContentId = row.ContentId;
            if (!self)
            {
                plugin.OpenPlayerCard(row.Name, world, row.ContentId);
            }
        }
        else if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Middle) && !self)
        {
            selectedContentId = row.ContentId;
            plugin.OpenWhisperTab(fullName);
        }

        using (var popup = ImRaii.ContextPopupItem("##rowMenu"))
        {
            if (popup.Success)
            {
                selectedContentId = row.ContentId;
                ImGui.TextDisabled(fullName);
                ImGui.Separator();
                contextMenu(row);
            }
        }

        var nickname = Nickname(row);
        var status = row.Stale ? null : StatusOf(row.State);
        if (hovered)
        {
            ImGui.SetTooltip((nickname != null ? fullName + "\n" : string.Empty)
                + (row.Stale ? Loc.T("Social.Loading") : unconfirmed ? Loc.T("Social.Checking") : status?.Name ?? Loc.T("Social.Offline"))
                + (online && IsElsewhere(row) ? "\n" + Loc.T("Social.ElsewhereTooltip", WorldName(row.CurrentWorld) ?? "?") : string.Empty)
                + (self ? string.Empty : "\n\n" + Loc.T("Social.ClickHint")));
        }

        ImGui.Dummy(new Vector2(1f, gap));
        if (!visible)
        {
            return;
        }

        var max = min + size;
        var radius = 6f * scale;

        var cardBg = style.Colors[(int)ImGuiCol.WindowBg] with { W = 0.55f };
        if (card)
        {
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(cardBg), radius);
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.035f)), radius);
        }

        if (selectedContentId == row.ContentId)
        {
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.Header), radius);
        }
        else if (hovered)
        {
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.HeaderHovered, 0.6f), radius);
        }

        if (card && online)
        {
            drawList.AddRectFilled(min + new Vector2(0f, radius * 0.6f), new Vector2(min.X + (3f * scale), max.Y - (radius * 0.6f)), ImGui.GetColorU32(accent));
        }

        var fade = online ? 1f : 0.55f;
        var pad = (card ? 8f : 4f) * scale;
        var avatarMin = min + new Vector2(pad, (rowHeight - avatarSize) / 2f);
        DrawAvatar(drawList, row, fullName, world, avatarMin, avatarSize, online, fade);

        var badge = MathF.Round(lineHeight * (card ? 0.95f : 0.9f));
        var badgeMin = card
            ? avatarMin + new Vector2(avatarSize - (badge * 0.8f), avatarSize - (badge * 0.8f))
            : avatarMin + new Vector2(avatarSize + (4f * scale), (avatarSize - badge) / 2f);
        if (status?.Icon is { } statusIcon and > 0 && Icon(statusIcon) is { } statusTex)
        {
            if (card)
            {
                drawList.AddCircleFilled(badgeMin + new Vector2(badge / 2f), (badge / 2f) + 1.5f, ImGui.GetColorU32(ImGuiCol.WindowBg));
            }

            drawList.AddImage(statusTex, badgeMin, badgeMin + new Vector2(badge), Vector2.Zero, Vector2.One, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, online ? 1f : 0.6f)));
        }

        if (HasNoDetails(row) && IsOnline(row.State) && activeTab == SocialTab.Friends && hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
            && ImGui.IsMouseHoveringRect(badgeMin, badgeMin + new Vector2(badge)))
        {
            AgentFriendlist.Instance()->RequestFriendInfo(row.ContentId);
        }

        var nameColor = online ? text : muted;
        var subColor = muted with { W = muted.W * fade };
        var label = nickname ?? row.Name;
        var job = row.Job != 0 ? JobName(row.Job) : string.Empty;
        var location = LocationText(row);
        var languagesWidth = ImGui.CalcTextSize("JEDF").X + (8f * scale);

        if (card)
        {
            var textX = avatarMin.X + avatarSize + (10f * scale);
            var line1Y = min.Y + (rowHeight / 2f) - lineHeight - (1f * scale);
            var line2Y = min.Y + (rowHeight / 2f) + (1f * scale);

            var x = DrawNameLine(drawList, row, label, textX, line1Y, nameColor, subColor, accent, fade);

            var x2 = textX;
            if (row.Job != 0 && Icon(62100u + row.Job) is { } jobTex)
            {
                drawList.AddImage(jobTex, new Vector2(x2, line2Y), new Vector2(x2 + lineHeight, line2Y + lineHeight), Vector2.Zero, Vector2.One, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, fade)));
                x2 += lineHeight + (4f * scale);
            }

            var line2 = job.Length > 0 && location.Length > 0 ? job + "  \u00b7  " + location : job + location;
            drawList.PushClipRect(new Vector2(x2, min.Y), new Vector2(max.X - pad, max.Y), true);
            drawList.AddText(new Vector2(x2, line2Y), ImGui.GetColorU32(subColor), line2);
            drawList.PopClipRect();

            DrawLanguages(drawList, row, max.X - pad - languagesWidth, line1Y, nameColor, muted, accent, fade);

            if (online && IsElsewhere(row))
            {
                DrawElsewhereBadge(drawList, avatarMin - new Vector2(2f * scale), lineHeight * 0.95f);
            }
            _ = x;
        }
        else
        {
            var y = min.Y + ((rowHeight - lineHeight) / 2f);
            var nameX = badgeMin.X + badge + (6f * scale);
            if (online && IsElsewhere(row))
            {
                DrawElsewhereBadge(drawList, new Vector2(nameX, y), lineHeight);
                nameX += lineHeight + (4f * scale);
            }

            var columnsStart = min.X + (width * 0.45f);
            drawList.PushClipRect(new Vector2(nameX, min.Y), new Vector2(columnsStart - (6f * scale), max.Y), true);
            DrawNameLine(drawList, row, label, nameX, y, nameColor, subColor, accent, fade);
            drawList.PopClipRect();

            var jx = columnsStart;
            if (row.Job != 0 && Icon(62100u + row.Job) is { } jobTex)
            {
                drawList.AddImage(jobTex, new Vector2(jx, y), new Vector2(jx + lineHeight, y + lineHeight), Vector2.Zero, Vector2.One, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, fade)));
                drawList.AddText(new Vector2(jx + lineHeight + (3f * scale), y), ImGui.GetColorU32(subColor), job);
            }

            var locationX = jx + (lineHeight * 3.4f);
            drawList.PushClipRect(new Vector2(locationX, min.Y), new Vector2(max.X - pad - languagesWidth, max.Y), true);
            drawList.AddText(new Vector2(locationX, y), ImGui.GetColorU32(subColor), location);
            drawList.PopClipRect();

            DrawLanguages(drawList, row, max.X - pad - languagesWidth, y, nameColor, muted, accent, fade);
        }
    }

    private void DrawElsewhereBadge(ImDrawListPtr drawList, Vector2 min, float size)
    {
        var center = min + new Vector2(size / 2f);
        var accent = C4tTheme.ActiveMarkerColor(plugin.Configuration);
        drawList.AddCircleFilled(center, (size / 2f) + 1.5f, ImGui.GetColorU32(ImGuiCol.WindowBg));
        drawList.AddCircleFilled(center, size / 2f, ImGui.GetColorU32(accent with { W = 0.9f }));
        var glyph = FontAwesomeIcon.Globe.ToIconString();
        var fontSize = size * 0.72f;
        Vector2 glyphSize;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            glyphSize = ImGui.CalcTextSize(glyph) * (fontSize / ImGui.GetFontSize());
        }

        drawList.AddText(UiBuilder.IconFont, fontSize, center - (glyphSize / 2f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)), glyph);
    }

    private float DrawNameLine(ImDrawListPtr drawList, Row row, string label, float x, float y, Vector4 nameColor, Vector4 subColor, Vector4 accent, float fade)
    {
        var scale = ImGuiHelpers.GlobalScale;
        drawList.AddText(new Vector2(x, y), ImGui.GetColorU32(nameColor), label);
        x += ImGui.CalcTextSize(label).X + (6f * scale);

        if (row.Group > 0 && row.Group < GroupSymbols.Length)
        {
            drawList.AddText(new Vector2(x, y), ImGui.GetColorU32(accent with { W = fade }), GroupSymbols[row.Group]);
            x += ImGui.CalcTextSize(GroupSymbols[row.Group]).X + (6f * scale);
        }

        if (row.FcTag.Length > 0)
        {
            var tag = "\u00ab" + row.FcTag + "\u00bb";
            drawList.AddText(new Vector2(x, y), ImGui.GetColorU32(subColor), tag);
            x += ImGui.CalcTextSize(tag).X;
        }

        return x;
    }

    private static void DrawLanguages(ImDrawListPtr drawList, Row row, float x, float y, Vector4 text, Vector4 muted, Vector4 accent, float fade)
    {
        var scale = ImGuiHelpers.GlobalScale;
        for (var i = 0; i < LanguageLetters.Length; i++)
        {
            var has = (row.Languages & (1 << i)) != 0;
            var color = row.MainLanguage == i && has
                ? accent with { W = fade }
                : has ? text with { W = 0.85f * fade } : muted with { W = 0.25f * fade };
            drawList.AddText(new Vector2(x, y), ImGui.GetColorU32(color), LanguageLetters[i]);
            x += ImGui.CalcTextSize(LanguageLetters[i]).X + (2f * scale);
        }
    }

    private void DrawAvatar(ImDrawListPtr drawList, Row row, string key, string? world, Vector2 min, float size, bool online, float fade)
    {
        var config = plugin.Configuration;
        config.CustomAvatars.TryGetValue(key, out var customFile);
        ImTextureID? texture = null;
        if (world != null || customFile != null)
        {
            if (!online && config.SocialGreyOffline)
            {
                texture = plugin.Avatars.GetGrey(key, customFile, config.LodestoneAvatars, row.Name, world);
            }

            texture ??= plugin.Avatars.Get(key, customFile, config.LodestoneAvatars, row.Name, world, out _);
        }

        var max = min + new Vector2(size);
        var center = min + new Vector2(size / 2f);
        if (texture is { } handle)
        {
            var (uv0, uv1) = plugin.AvatarUv(key, customFile);
            drawList.AddImageRounded(handle, min, max, uv0, uv1, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, online ? 1f : 0.7f)), size / 2f);
        }
        else
        {
            var tone = online ? C4tTheme.ActiveMarkerColor(config) : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
            drawList.AddCircleFilled(center, size / 2f, ImGui.GetColorU32(tone with { W = 0.22f }));
            var initial = row.Name.Length > 0 ? row.Name[..1].ToUpperInvariant() : "?";
            var textSize = ImGui.CalcTextSize(initial);
            drawList.AddText(center - (textSize / 2f), ImGui.GetColorU32(ImGuiCol.Text, fade), initial);
        }

        if (online)
        {
            drawList.AddCircle(center, (size / 2f) + 1f, ImGui.GetColorU32(C4tTheme.ActiveMarkerColor(config) with { W = 0.7f }), 0, 1.5f * ImGuiHelpers.GlobalScale);
        }
    }

    private ImTextureID? Icon(uint icon) =>
        plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrDefault()?.Handle;

    private string? Nickname(Row row)
    {
        var world = WorldName(row.HomeWorld);
        return plugin.Configuration.ShowNicknames
            ? plugin.FindNickname(world != null ? row.Name + "@" + world : row.Name, row.Name, world)
            : null;
    }

    private string LocationText(Row row)
    {
        if (IsOnline(row.State) && !row.OtherServer && row.Location != 0 && PlaceName(row.Location) is { Length: > 0 } place)
        {
            return place;
        }

        return WorldName(row.CurrentWorld != 0 ? row.CurrentWorld : row.HomeWorld) ?? string.Empty;
    }

    private static bool RequestThrottled(InfoProxyCommonList* proxy, ref long last)
    {
        var now = Environment.TickCount64;
        if (proxy == null || now - last < 60_000)
        {
            return false;
        }

        last = now;
        proxy->RequestData();
        return true;
    }

    private static List<Row> Read(InfoProxyCommonList* proxy)
    {
        var rows = new List<Row>();
        if (proxy == null)
        {
            return rows;
        }

        var count = proxy->GetEntryCount();
        for (uint i = 0; i < count; i++)
        {
            var data = proxy->GetEntry(i);
            if (data == null || data->ContentId == 0)
            {
                continue;
            }

            var name = data->NameString;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            rows.Add(new Row
            {
                ContentId = data->ContentId,
                Name = name,
                HomeWorld = data->HomeWorld,
                CurrentWorld = data->CurrentWorld,
                Location = data->Location,
                Job = data->Job,
                State = (ulong)data->State,
                FcTag = data->FCTagString ?? string.Empty,
                Languages = (byte)data->Languages,
                MainLanguage = (byte)data->ClientLanguage,
                Group = (sbyte)data->Group,
                OtherServer = data->IsOtherServer,
            });
        }

        return rows;
    }

    private static bool HasBit(ulong state, int bit) => (state & (1UL << bit)) != 0;

    private static bool IsOnline(ulong state) => state != 0 && !HasBit(state, OfflineBit) && !HasBit(state, NotFoundBit);

    private StatusInfo? StatusOf(ulong state)
    {
        LoadStatuses();
        if (!IsOnline(state) && !HasBit(state, NotFoundBit))
        {
            return statuses.GetValueOrDefault(OfflineBit);
        }

        StatusInfo? best = null;
        for (var bit = 0; bit < 64; bit++)
        {
            if (HasBit(state, bit) && statuses.TryGetValue(bit, out var info) && info.List && info.Priority > 0
                && (best == null || info.Priority < best.Priority))
            {
                best = info;
            }
        }

        return best;
    }

    private void LoadStatuses()
    {
        if (statuses.Count > 0)
        {
            return;
        }

        try
        {
            foreach (var row in plugin.DataManager.GetExcelSheet<OnlineStatus>())
            {
                if (row.RowId < 64)
                {
                    statuses[(int)row.RowId] = new StatusInfo
                    {
                        Icon = row.Icon,
                        Priority = row.Priority,
                        List = row.List,
                        Name = row.Name.ExtractText(),
                    };
                }
            }
        }
        catch (Exception ex)
        {
            plugin.Log.Warning(ex, "[C4TChat] OnlineStatus-Tabelle konnte nicht geladen werden.");
            statuses[-1] = new StatusInfo();
        }
    }

    private string? WorldName(ushort id)
    {
        if (id == 0)
        {
            return null;
        }

        if (!worldNames.TryGetValue(id, out var name))
        {
            name = plugin.DataManager.GetExcelSheet<World>().GetRowOrDefault(id)?.Name.ExtractText() ?? string.Empty;
            worldNames[id] = name;
        }

        return name.Length > 0 ? name : null;
    }

    private string PlaceName(ushort territory)
    {
        if (!placeNames.TryGetValue(territory, out var name))
        {
            name = plugin.DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territory)?.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty;
            placeNames[territory] = name;
        }

        return name;
    }

    private string JobName(byte job)
    {
        if (!jobNames.TryGetValue(job, out var name))
        {
            name = plugin.DataManager.GetExcelSheet<ClassJob>().GetRowOrDefault(job)?.Abbreviation.ExtractText() ?? string.Empty;
            jobNames[job] = name;
        }

        return name;
    }
}
