﻿using EvoSC.Common.Interfaces;
using EvoSC.Common.Interfaces.Models;
using EvoSC.Common.Interfaces.Services;
using EvoSC.Common.Services.Attributes;
using EvoSC.Common.Services.Models;
using EvoSC.Common.Util;
using EvoSC.Manialinks.Interfaces;
using EvoSC.Modules.Official.SpectatorTargetInfoModule.Config;
using EvoSC.Modules.Official.SpectatorTargetInfoModule.Interfaces;
using EvoSC.Modules.Official.SpectatorTargetInfoModule.Models;
using LinqToDB.Common;

namespace EvoSC.Modules.Official.SpectatorTargetInfoModule.Services;

[Service(LifeStyle = ServiceLifeStyle.Singleton)]
public class SpectatorTargetInfoService(
    IManialinkManager manialinks,
    IServerClient server,
    IPlayerManagerService playerManagerService,
    ISpectatorTargetInfoSettings settings
) : ISpectatorTargetInfoService
{
    private const string WidgetTemplate = "SpectatorTargetInfoModule.SpectatorTargetInfo";

    private readonly Dictionary<int, CheckpointsGroup> _checkpointTimes = new(); // cp-id -> data
    private readonly Dictionary<string, string> _spectatorTargets = new(); // login -> login

    /*
     * Spectator select new target -> update widget for them
     * Driver passes new checkpoint -> update widget for all spectators of that player
     * New round starts -> clear diffs in widgets
     * Hide on podium
     * Re-send widget after map change
     */

    public Task<IOnlinePlayer> GetOnlinePlayerByLoginAsync(string playerLogin)
        => playerManagerService.GetOnlinePlayerAsync(PlayerUtils.ConvertLoginToAccountId(playerLogin));

    public async Task AddCheckpointAsync(string playerLogin, int checkpointIndex, int checkpointTime)
    {
        var player = await GetOnlinePlayerByLoginAsync(playerLogin);
        var newCheckpointData = new CheckpointData(player, checkpointTime);
        
        if (!_checkpointTimes.TryGetValue(checkpointIndex, out var checkpointGroup))
        {
            checkpointGroup = [];
            _checkpointTimes.Add(checkpointIndex, checkpointGroup);
        }
        checkpointGroup.Add(newCheckpointData);
        checkpointGroup = checkpointGroup.ToSortedGroup();
        _checkpointTimes[checkpointIndex] = checkpointGroup;
        
        var playerLogins = GetLoginsOfPlayersSpectatingTarget(playerLogin).ToList();
        if (playerLogins.IsNullOrEmpty())
        {
            return;
        }

        var leadingCheckpointData = checkpointGroup.First();
        var rank = checkpointGroup.GetRank(playerLogin);
        var timeDifference = GetTimeDifference(leadingCheckpointData, newCheckpointData);

        await SendWidgetAsync(playerLogins, player, rank, timeDifference);
    }

    public Task ClearCheckpointsAsync()
    {
        _checkpointTimes.Clear();

        return Task.CompletedTask;
    }

    public async Task<string?> GetLoginOfDedicatedPlayerAsync(int targetPlayerIdDedicated)
    {
        var serverPlayers = await server.Remote.GetPlayerListAsync();

        return serverPlayers.Where(player => player.PlayerId == targetPlayerIdDedicated)
            .Select(player => player.Login)
            .FirstOrDefault();
    }

    public async Task UpdateSpectatorTargetAsync(string spectatorLogin, int targetPlayerIdDedicated)
    {
        var targetLogin = await GetLoginOfDedicatedPlayerAsync(targetPlayerIdDedicated);

        if (targetLogin == null)
        {
            await RemovePlayerFromSpectatorsListAsync(spectatorLogin);
            return;
        }

        await SetSpectatorTargetLoginAsync(spectatorLogin, targetLogin);
    }

    public async Task SetSpectatorTargetLoginAsync(string spectatorLogin, string targetLogin)
    {
        _spectatorTargets[spectatorLogin] = targetLogin;

        var checkpointIndex = GetLastCheckpointIndexOfPlayer(targetLogin);
        if (!_checkpointTimes.ContainsKey(checkpointIndex))
        {
            //TODO: error?
            return;
        }

        var targetPlayer = await GetOnlinePlayerByLoginAsync(targetLogin);
        var checkpointsGroup = _checkpointTimes[checkpointIndex];
        var leadingCpData = checkpointsGroup.First();
        var targetCpData = checkpointsGroup.GetPlayer(targetLogin);
        var targetRank = checkpointsGroup.GetRank(targetLogin);
        var timeDifference = GetTimeDifference(leadingCpData, targetCpData!);

        await SendWidgetAsync(spectatorLogin, targetPlayer, targetRank, timeDifference);
    }

    public async Task RemovePlayerFromSpectatorsListAsync(string spectatorLogin)
    {
        _spectatorTargets.Remove(spectatorLogin);

        await HideWidgetAsync(spectatorLogin);
    }

    public IEnumerable<string> GetLoginsOfPlayersSpectatingTarget(string targetPlayerLogin)
    {
        return _spectatorTargets.Where(specTarget => specTarget.Value == targetPlayerLogin)
            .Select(specTarget => specTarget.Key);
    }

    public SpectatorInfo ParseSpectatorStatus(int spectatorStatus)
    {
        return new SpectatorInfo(
            Convert.ToBoolean(spectatorStatus % 10),
            Convert.ToBoolean((spectatorStatus / 10) % 10),
            Convert.ToBoolean((spectatorStatus / 100) % 10),
            Convert.ToBoolean((spectatorStatus / 1000) % 10),
            spectatorStatus / 10_000
        );
    }

    public int GetTimeDifference(CheckpointData leadingCheckpointData, CheckpointData targetCheckpointData)
    {
        return GetTimeDifference(leadingCheckpointData.time, targetCheckpointData.time);
    }

    public int GetTimeDifference(int leadingCheckpointTime, int targetCheckpointTime)
    {
        return targetCheckpointTime - leadingCheckpointTime;
    }

    public int GetLastCheckpointIndexOfPlayer(string playerLogin)
    {
        foreach (var (checkpointIndex, checkpointsGroup) in _checkpointTimes.Reverse())
        {
            if (checkpointsGroup.GetPlayer(playerLogin) != null)
            {
                return checkpointIndex;
            }
        }

        return -1;
    }

    public Dictionary<int, CheckpointsGroup> GetCheckpointTimes()
    {
        return _checkpointTimes;
    }

    public async Task SendWidgetAsync(IEnumerable<string> playerLogins, IOnlinePlayer targetPlayer,
        int targetPlayerRank, int timeDifference)
    {
        foreach (var playerLogin in playerLogins)
        {
            await SendWidgetAsync(playerLogin, targetPlayer, targetPlayerRank, timeDifference);
        }
    }

    public Task SendWidgetAsync(string playerLogin, IOnlinePlayer targetPlayer, int targetPlayerRank,
        int timeDifference) =>
        manialinks.SendManialinkAsync(playerLogin, WidgetTemplate,
            new { settings, timeDifference, playerRank = targetPlayerRank, playerName = targetPlayer.NickName });

    public Task HideWidgetAsync()
        => manialinks.HideManialinkAsync(WidgetTemplate);

    public Task HideWidgetAsync(string playerLogin)
        => manialinks.HideManialinkAsync(playerLogin, WidgetTemplate);

    public Task AddFakePlayerAsync() => //TODO: remove before mergin into master
        server.Remote.ConnectFakePlayerAsync();
}
