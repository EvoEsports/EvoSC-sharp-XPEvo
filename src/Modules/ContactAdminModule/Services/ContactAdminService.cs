using System.Text;
using EvoSC.Common.Interfaces;
using EvoSC.Common.Interfaces.Models;
using EvoSC.Common.Interfaces.Services;
using EvoSC.Common.Services.Attributes;
using EvoSC.Common.Services.Models;
using EvoSC.Manialinks.Interfaces;
using EvoSC.Modules.Nsgr.ContactAdminModule.Config;
using EvoSC.Modules.Nsgr.ContactAdminModule.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace EvoSC.Modules.Nsgr.ContactAdminModule.Services;

[Service(LifeStyle = ServiceLifeStyle.Singleton)]
public class ContactAdminService(
    IManialinkManager manialinkManager,
    ILogger<ContactAdminService> logger,
    IServerClient client,
    IChatService chat,
    IContactAdminSettings settings
)
    : IContactAdminService
{
    private readonly HttpClient _http = new();
    private DateTime _lastSuccessfulRequest = DateTime.MinValue;
    private static SemaphoreSlim _requestSemaphore = new(1, 1);
    
    public async Task ShowWidgetAsync()
    {
        await manialinkManager.SendPersistentManialinkAsync("ContactAdminModule.ContactAdminWidget");
        logger.LogDebug("Showing 'Contact Admin' widget");
    }

    public async Task HideWidgetAsync()
    {
        await manialinkManager.HideManialinkAsync("ContactAdminModule.ContactAdminWidget");
        logger.LogDebug("Hiding 'Contact Admin' widget");
    }

    public async Task ContactAdminAsync(IOnlinePlayer? contextPlayer)
    {
        await _requestSemaphore.WaitAsync();
        
        try
        {
            await PerformAdminRequestAsync(contextPlayer);
        }
        finally
        {
            _requestSemaphore.Release();
        }
    }

    private async Task PerformAdminRequestAsync(IOnlinePlayer? contextPlayer)
    {
        TimeSpan timeSinceLastRequest = DateTime.UtcNow - _lastSuccessfulRequest;
        
        logger.LogDebug(timeSinceLastRequest.ToString());
        
        if (timeSinceLastRequest <= TimeSpan.FromSeconds(settings.RequestCooldown))
        {
            if (contextPlayer is null)
            {
                await chat.InfoMessageAsync($"Already contacted admins {Math.Truncate(timeSinceLastRequest.TotalSeconds)} seconds ago.");
            }
            else
            {
                await chat.InfoMessageAsync($"Already contacted admins {Math.Truncate(timeSinceLastRequest.TotalSeconds)} seconds ago.", contextPlayer);
            }

            return;
        }
        
        var serverName = await client.Remote.GetServerNameAsync();

        var discordMessage = contextPlayer is null
            ? $"Help requested on server {serverName}"
            : $"{contextPlayer.NickName} requested help on server {serverName}";

        // Check for suffix so we can ping people on Discord
        if (settings.MessageSuffix.Length > 0)
        {
            discordMessage += $" {settings.MessageSuffix}";
        }

        var messageObject = new { content = discordMessage };
        var json = JsonConvert.SerializeObject(messageObject);
        var data = new StringContent(json, Encoding.UTF8, "application/json");
        
        logger.LogTrace($"Requesting {settings.WebhookUrl}");

        try
        {
            var response = await _http.PostAsync(settings.WebhookUrl, data);
            if (!response.IsSuccessStatusCode) throw new Exception("The request status code was not successful.");

            logger.LogDebug("Successfully executed webhook.");
            _lastSuccessfulRequest = DateTime.UtcNow;

            var chatMessage = contextPlayer is null
                ? "The admins were contacted."
                : $"$<{contextPlayer.NickName}$> requested to contact the admins.";

            await chat.InfoMessageAsync(chatMessage);
        }
        catch (Exception ex)
        {
            logger.LogError($"Failed to execute Discord webhook: {ex.ToString()}");

            if (contextPlayer is null)
            {
                await chat.ErrorMessageAsync("Could not notify admins. Please reach out manually.");
            }
            else
            {
                await chat.ErrorMessageAsync("Could not notify admins. Please reach out manually.", contextPlayer);
            }
        }
    }
}
