﻿using EvoSC.Common.Interfaces.Models;
using EvoSC.Common.Interfaces.Services;
using EvoSC.Common.Services.Attributes;
using EvoSC.Common.Services.Models;
using EvoSC.Modules.EvoEsports.ToornamentModule.Interfaces;
using EvoSC.Modules.EvoEsports.ToornamentModule.Settings;
using ToornamentApi.Models.Api.TournamentApi;

namespace EvoSC.Modules.EvoEsports.ToornamentModule.Services;

[Service(LifeStyle = ServiceLifeStyle.Transient)]
public class MatchPlayerService(
    IPlayerManagerService playerManagerService,
    IPermissionManager permissionManager,
    IToornamentSettings settings
    ) : IMatchPlayerService
{
    public async Task<List<IPlayer>> GetPlayersFromOpponents(OpponentInfo[] opponents)
    {
        List<IPlayer> players = [];
        foreach (var opponent in opponents)
        {
            if (opponent is not null)
            {
                IPlayer player = null;
                if (opponent.Participant is not null)
                {
                    var tmId = opponent.Participant.CustomFields["trackmania_id"];
                    if (tmId is not null)
                    {
                        player = await playerManagerService.GetOrCreatePlayerAsync(tmId.ToString().ToLowerInvariant(),
                            opponent.Participant.Name);
                        players.Add(player);
                    }
                }

                if (player is not null && player.Groups.Count() == 0)
                {
                    await permissionManager.AddPlayerToGroupAsync(player, settings.DefaultGroupId);
                }
            }
        }

        return players;
    }
}
