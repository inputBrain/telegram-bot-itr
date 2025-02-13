using Telegram.Bot;
using TelegramITRBot.Abstracts;

namespace TelegramITRBot.Services;

public class ReceiverService : ReceiverServiceBase<UpdateHandler>
{
    public ReceiverService(ITelegramBotClient botClient, UpdateHandler updateHandler, ILogger<ReceiverServiceBase<UpdateHandler>> logger) : base(botClient, updateHandler, logger)
    {
    }
}
