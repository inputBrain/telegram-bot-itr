using System.Threading;
using System.Threading.Tasks;

namespace TelegramITRBot.Abstracts;

public interface IReceiverService
{
    Task ReceiveAsync(CancellationToken stoppingToken);
}