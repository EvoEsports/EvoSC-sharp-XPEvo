﻿using EvoSC.Common.Controllers.Attributes;
using EvoSC.Common.Util;
using EvoSC.Manialinks;
using EvoSC.Modules.Official.SpectatorTargetInfoModule.Interfaces;

namespace EvoSC.Modules.Official.SpectatorTargetInfoModule.Controllers;

[Controller]
public class SpectatorTargetInfoManialinkController(ISpectatorTargetInfoService spectatorTargetInfoService)
    : ManialinkController
{
    public async Task SetSpectatorTargetAsync(string targetLogin)
    {
        var spectatingPlayerLogin = Context.Player.GetLogin();

        if (targetLogin != "" && targetLogin != spectatingPlayerLogin)
        {
            await spectatorTargetInfoService.SetSpectatorTargetLoginAsync(spectatingPlayerLogin, targetLogin);
        }
        else
        {
            await spectatorTargetInfoService.RemovePlayerFromSpectatorsListAsync(spectatingPlayerLogin);
            await spectatorTargetInfoService.HideSpectatorInfoWidgetAsync(spectatingPlayerLogin);
        }
    }
}
