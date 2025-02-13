using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using TelegramITRBot.Configs;

namespace TelegramITRBot.Services;

public class UpdateHandler : IUpdateHandler
{
    private readonly ILogger<UpdateHandler> _logger;
    private readonly TelegramBotConfig _telegramBotConfig;
    private readonly ITelegramBotClient _botClient;
    private readonly IMessageService _messageService;


    public UpdateHandler(
        ILogger<UpdateHandler> logger,
        IOptions<TelegramBotConfig> telegramBotConfig,
        ITelegramBotClient botClient,
        IMessageService messageService
    )
    {
        _logger = logger;
        _telegramBotConfig = telegramBotConfig.Value;
        _botClient = botClient;
        _messageService = messageService;
    }


    public async Task HandleUpdateAsync(ITelegramBotClient _, Update update, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Username {Username} with Id {Id} | sent a message: {Message}",
            update.Message!.From!.Username,
            update.Message.From.Id,
            update.Message.Text
        );

        var handler = update switch
        {
            { Message: { } message }                       => BotOnMessageReceived(message, cancellationToken),
            { EditedMessage: { } message }                 => BotOnMessageReceived(message, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(update), update, null)
        };

        await handler;
    }


    private async Task BotOnMessageReceived(Message message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Receive message type: {MessageType}", message.Type);
        if (message.Text is not { } messageText)
            return;

        var action = messageText.Split(' ')[0] switch
        {
            "/option1"                                         => StartWebsiteHandler(_botClient, message, cancellationToken),
            "/option1@temp65535_test_ax_link_bot"              => StartWebsiteHandler(_botClient, message, cancellationToken),
            _                                                  => throw new ArgumentOutOfRangeException()
        };

        var sentMessage = await action;
        _logger.LogInformation("The message was sent with id: {SentMessageId}", sentMessage.MessageId);
    }

    public  async Task<Message> StartWebsiteHandler(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(message.Text) == false)
        {
            var user = message.From;
            var userInfo = $"User info:\n\n" +
                              $"ID: {user.Id}\n" +
                              $"Name: {user.FirstName}\n" +
                              $"Surname: {user.LastName ?? "not set"}\n" +
                              $"Username: @{user.Username ?? "not set"}\n" +
                              $"Language Code: {user.LanguageCode ?? "not set"}\n" +
                              $"Is premium: {(user.IsPremium.HasValue && user.IsPremium.Value ? "YES" : "NO")}\n" +
                              $"Is added to attach menu: {(user.AddedToAttachmentMenu.HasValue && user.AddedToAttachmentMenu.Value ? "YES" : "NO")}\n" +
                              $"Is user a bot: {(user.IsBot ? "YES" : "NO")}";
            
            _logger.LogInformation("Command '/option1' received. User details:\n{UserInfo}", userInfo);

            await botClient.SendTextMessageAsync(chatId: _telegramBotConfig.PrivateChatId, text: $"{userInfo}", cancellationToken: cancellationToken);
        }

        return await botClient.SendTextMessageAsync(
            chatId: _telegramBotConfig.PrivateChatId,
            text: "-- message sent --",
            cancellationToken: cancellationToken);
    }


    public async Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        var ErrorMessage = exception switch
        {
            ApiRequestException apiRequestException => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        _logger.LogInformation("HandleError: {ErrorMessage}", ErrorMessage);

        if (exception is RequestException)
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    }
}