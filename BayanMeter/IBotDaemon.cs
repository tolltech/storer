using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Tolltech.TelegramCore
{
    public interface IBotDaemon
    {
        Task HandleErrorAsync(ITelegramBotClient client, Exception exception, CancellationToken cancellationToken);
        Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken cancellationToken);
    }
}