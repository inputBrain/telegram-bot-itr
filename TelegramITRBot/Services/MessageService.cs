using Telegram.Bot;

namespace TelegramITRBot.Services;

public class MessageService : IMessageService
{
    private readonly ITelegramBotClient _botClient;

    public MessageService(ITelegramBotClient botClient)
    {
        _botClient = botClient;
    }
}