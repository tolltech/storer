using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Tolltech.CoreLib;
using Tolltech.TelegramCore;
using File = System.IO.File;

namespace Tolltech.Storer
{
    public class ServerStorerBotDaemon : IBotDaemon
    {
        private readonly ITelegramClient telegramClient;

        private static readonly ILog log = LogManager.GetLogger(typeof(ServerStorerBotDaemon));
        private readonly StorerCustomSettings storerCustomSettings;
        private readonly Dictionary<string, string> storerCustomSettingsRaw;

        public ServerStorerBotDaemon(ITelegramClient telegramClient, CustomSettings customSettings)
        {
            this.telegramClient = telegramClient;
            storerCustomSettingsRaw = customSettings.Raw.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                .ToDictionary(x => x.Split(new[] { "=" }, StringSplitOptions.RemoveEmptyEntries)[0],
                    x => x.Split(new[] { "=" }, StringSplitOptions.RemoveEmptyEntries)[1]);
            storerCustomSettings = new StorerCustomSettings
            {
                RootDir = storerCustomSettingsRaw["RootDir"],
                AllowedUsers = storerCustomSettingsRaw["AllowedUsers"]
                    .Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries)
            };
        }

        public async Task HandleUpdateAsync(ITelegramBotClient client, Update update,
            CancellationToken cancellationToken)
        {
            // Only process Message updates: https://core.telegram.org/bots/api#message
            if (update.Type != UpdateType.Message)
                return;

            try
            {
                var message = update.Message;
                if (message == null)
                {
                    return;
                }

                log.Info($"RecieveMessage {message.Chat.Id} {message.MessageId}");

                await SaveMessageIfPhotoAsync(message).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                log.Error("BotDaemonException", e);
                Console.WriteLine($"BotDaemonException: {e.Message} {e.StackTrace}");
            }
        }

        public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception,
            CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            log.Error("BotDaemonException", exception);
            Console.WriteLine($"BotDaemonException: {ErrorMessage} {exception.StackTrace}");
            return Task.CompletedTask;
        }

        private Task SaveMessageIfPhotoAsync(Message message)
        {
            if (message?.Type != MessageType.Video)
            {
                return Task.CompletedTask;
            }

            var video = message.Video;

            if (video == null)
            {
                return Task.CompletedTask;
            }

            if (!storerCustomSettings.AllowedUsers.Contains(message.From?.Username)
                && !storerCustomSettings.AllowedUsers.Contains(message.From?.Id.ToString()))
            {
                log.Info($"Video was not saved. User {message.From?.Username} {message.From?.Id} is not allowed");
                return Task.CompletedTask;
            }

            var bytes = telegramClient.GetFile(video.FileId);

            //var messageDto = Convert(message, bytes);
            SaveVideo(video, bytes, message);

            log.Info($"SavedMessage {message.Chat.Id} {message.MessageId}");

            return Task.CompletedTask;
        }

        private void SaveVideo(Video video, byte[] bytes, Message message)
        {
            if (storerCustomSettings?.RootDir == null)
            {
                log.Info($"Video was not saved. RootDir is null");
                return;
            }

            var folderName = $"{message.Chat.Title}_" +
                             $"{new string(message.Chat.Id.ToString().Where(char.IsLetterOrDigit).ToArray())}";

            var fullFolderPath = Path.Combine(storerCustomSettings.RootDir, folderName);

            if (!Directory.Exists(fullFolderPath))
            {
                Directory.CreateDirectory(fullFolderPath);
            }

            var fullFileName = Path.Combine(fullFolderPath,
                $"{new string(message.MessageId.ToString().Where(char.IsLetterOrDigit).ToArray())}_{video.FileName}");
            File.WriteAllBytes(fullFileName, bytes);
        }

        //private static string GetBayanMessage(BayanResultDto bayanMetric)
        //{
        //    //" -1001261621141"
        //    var chatIdStr = bayanMetric.PreviousChatId.ToString();
        //    if (chatIdStr.StartsWith("-100"))
        //    {
        //        chatIdStr = chatIdStr.Replace("-100", string.Empty);
        //    }

        //    var chatId = long.Parse(chatIdStr);

        //    return $"[:||[{bayanMetric.AlreadyWasCount}]||:] #bayan\r\n" +
        //           $"https://t.me/c/{chatId}/{bayanMetric.PreviousMessageId}";
        //}
    }
}