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

        private static readonly Dictionary<(long ChatId, int MessageId), string> messageHistory = new();

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

                messageHistory[(message.Chat.Id, message.MessageId)] = message.Text;

                log.Info($"RecieveMessage {message.Chat.Id} {message.MessageId}");

                try
                {
                    await SaveMessageIfPhotoAsync(message, client).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    await client.SendTextMessageAsync(message.Chat.Id, $"Error. {e.Message} {e.StackTrace}")
                        .ConfigureAwait(false);
                    throw;
                }
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

        private Task SaveMessageIfPhotoAsync(Message message, ITelegramBotClient client)
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
            log.Info($"Saving {message.Chat.Id} {message.MessageId}");
            return SaveVideo(video, bytes, message, client);
        }

        private async Task SaveVideo(Video video, byte[] bytes, Message message, ITelegramBotClient client)
        {
            if (storerCustomSettings?.RootDir == null)
            {
                log.Info($"Video was not saved. RootDir is null");
                return;
            }

            await client.SendTextMessageAsync(message.Chat.Id, $"Saving...", replyToMessageId: message.MessageId).ConfigureAwait(false);

            var folderName = GetFolderName(message);

            var fullFolderPath = Path.Combine(storerCustomSettings.RootDir, folderName);

            if (!Directory.Exists(fullFolderPath))
            {
                Directory.CreateDirectory(fullFolderPath);
            }

            var customFileName = GetFileNameFromMessage(message);
            var defaultFileName =
                $"{new string(message.MessageId.ToString().Where(char.IsLetterOrDigit).ToArray())}_{video.FileName}";

            var ext = Path.GetExtension(defaultFileName);

            var fullFileName = Path.Combine(fullFolderPath,
                customFileName != null ? customFileName + ext : defaultFileName);
            await File.WriteAllBytesAsync(fullFileName, bytes).ConfigureAwait(false);

            await client.SendTextMessageAsync(message.Chat.Id, $"Saved {fullFileName}",
                    replyToMessageId: message.MessageId)
                .ConfigureAwait(false);
        }

        private static string GetFolderName(Message message)
        {
            var defaultFolderName = $"{message.Chat.Title}_" +
                                    $"{new string(message.Chat.Id.ToString().Where(char.IsLetterOrDigit).ToArray())}";
            var customFolderName = GetFolderNameFromMessage(message);

            return customFolderName ?? defaultFolderName;
        }

        private static string GetFolderNameFromMessage(Message message)
        {
            var args = GetArgsFromMessageText(message);

            if (args.TryGetValue("folder", out var dir)) return dir;
            return null;
        }

        private static string GetFileNameFromMessage(Message messageText)
        {
            var args = GetArgsFromMessageText(messageText);

            if (args.TryGetValue("file", out var dir)) return dir;
            return null;
        }

        private static Dictionary<string, string> GetArgsFromMessageText(Message message)
        {
            var messageText = message.Text ?? GetPreviousMessageText(message);
            
            if (string.IsNullOrWhiteSpace(messageText)) return new Dictionary<string, string>();

            var args = messageText.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Split(new[] { "=" }, StringSplitOptions.RemoveEmptyEntries))
                .Where(x => x.Length == 2)
                .GroupBy(x => x[0].Trim().ToLower())
                .ToDictionary(x => x.Key, x => x.First()[1].Trim());
            return args;
        }

        private static string GetPreviousMessageText(Message message)
        {
            return messageHistory.TryGetValue((message.Chat.Id, message.MessageId - 1), out var msg)
                ? msg
                : null;
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