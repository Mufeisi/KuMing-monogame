using System;
using System.Collections.Generic;

using S = ServerPackets;

namespace MonoShare.MirScenes;

/// <summary>
/// Mobile-only state seam for the server's Hero/ManageHeroes protocol.
/// The server remains authoritative; this class only keeps the latest snapshot
/// needed by the FairyGUI overlay and applies the server's ChangeHero swap.
/// </summary>
public sealed class MobileHeroState
{
    private ClientHeroInformation[] _heroes = Array.Empty<ClientHeroInformation>();

    public int MaximumCount { get; private set; }

    public ClientHeroInformation CurrentHero { get; private set; }

    public IReadOnlyList<ClientHeroInformation> Heroes => _heroes;

    public bool IsOpen { get; private set; }

    public string Error { get; private set; }

    public int Revision { get; private set; }

    public bool ApplyManageHeroes(S.ManageHeroes packet)
    {
        if (packet == null)
        {
            SetFailure("英雄列表响应为空。");
            return false;
        }

        MaximumCount = Math.Max(0, packet.MaximumCount);
        CurrentHero = Clone(packet.CurrentHero);
        // The server intentionally omits Heroes after the first snapshot in a
        // connection (HeroStorageSent). Preserve the prior storage in that case;
        // an explicit empty array still clears it.
        if (packet.Heroes != null)
            _heroes = CloneArray(packet.Heroes);
        IsOpen = true;
        Error = null;
        Revision++;
        return true;
    }

    public bool ApplyChangeHero(S.ChangeHero packet)
    {
        if (packet == null)
        {
            SetFailure("英雄切换响应为空。", clearSnapshot: false);
            return false;
        }

        int index = packet.FromIndex;
        if (index < 0 || index >= _heroes.Length)
        {
            SetFailure("英雄索引无效。", clearSnapshot: false);
            return false;
        }

        ClientHeroInformation selected = _heroes[index];
        if (selected == null)
        {
            SetFailure("该英雄槽位为空。", clearSnapshot: false);
            return false;
        }

        _heroes[index] = CurrentHero;
        CurrentHero = selected;
        IsOpen = true;
        Error = null;
        Revision++;
        return true;
    }

    public bool ApplyNewHeroInfo(S.NewHeroInfo packet)
    {
        if (packet?.Info == null || packet.StorageIndex < 0)
        {
            SetFailure("新增英雄响应无效。");
            return false;
        }

        int index = packet.StorageIndex;
        if (index >= _heroes.Length)
            Array.Resize(ref _heroes, index + 1);

        _heroes[index] = Clone(packet.Info);
        IsOpen = true;
        Error = null;
        Revision++;
        return true;
    }

    public void ResetForSession()
    {
        MaximumCount = 0;
        CurrentHero = null;
        _heroes = Array.Empty<ClientHeroInformation>();
        IsOpen = false;
        Error = null;
        Revision++;
    }

    private void SetFailure(string message, bool clearSnapshot = true)
    {
        if (clearSnapshot)
        {
            MaximumCount = 0;
            CurrentHero = null;
            _heroes = Array.Empty<ClientHeroInformation>();
        }
        IsOpen = true;
        Error = string.IsNullOrWhiteSpace(message) ? "英雄数据不可用。" : message;
        Revision++;
    }

    private static ClientHeroInformation[] CloneArray(ClientHeroInformation[] heroes)
    {
        if (heroes == null || heroes.Length == 0)
            return Array.Empty<ClientHeroInformation>();

        var clone = new ClientHeroInformation[heroes.Length];
        for (int i = 0; i < heroes.Length; i++)
            clone[i] = Clone(heroes[i]);
        return clone;
    }

    private static ClientHeroInformation Clone(ClientHeroInformation hero)
    {
        if (hero == null)
            return null;

        return new ClientHeroInformation
        {
            Index = hero.Index,
            Name = hero.Name,
            Level = hero.Level,
            Class = hero.Class,
            Gender = hero.Gender,
        };
    }
}
