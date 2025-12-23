using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using File = System.IO.File;

namespace Tolltech.BayanMeter
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
            storerCustomSettingsRaw = customSettings.Raw.Split([","], StringSplitOptions.RemoveEmptyEntries)
                .ToDictionary(x => x.Split(["="], StringSplitOptions.RemoveEmptyEntries)[0],
                    x => x.Split(["="], StringSplitOptions.RemoveEmptyEntries)[1]);
            storerCustomSettings = new StorerCustomSettings
            {
                RootDir = storerCustomSettingsRaw["RootDir"],
                AllowedUsers = storerCustomSettingsRaw["AllowedUsers"]
                    .Split([" "], StringSplitOptions.RemoveEmptyEntries)
            };
        }

        private static readonly Dictionary<(long ChatId, int MessageId), string> messageHistory = new();
        private static readonly Dictionary<string, (string Text, int OriginalMessageId)> mediaGroupsHistory = new();

        private static readonly Dictionary<long, (string Text, int LeftCount, int OriginalMessageId)> messageLeft =
            new();

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

                if (!string.IsNullOrWhiteSpace(message.Text))
                {
                    messageHistory[(message.Chat.Id, message.MessageId)] = message.Text;

                    var cntStr = new string(message.Text.TakeWhile(char.IsDigit).ToArray());
                    if (int.TryParse(cntStr, out var cnt))
                    {
                        messageLeft[message.Chat.Id] = (message.Text, cnt, message.MessageId);
                    }

                    if (message.Type != MessageType.Video)
                        return;
                }

                log.Info($"ReceiveMessage {message.Chat.Id} {message.MessageId}");

                try
                {
                    if (!storerCustomSettings.AllowedUsers.Contains(message.From?.Username)
                        && !storerCustomSettings.AllowedUsers.Contains(message.From?.Id.ToString()))
                    {
                        log.Info(
                            $"Video was not saved. User {message.From?.Username} {message.From?.Id} is not allowed");
                        return;
                    }

                    if (message.Text?.StartsWith("/ip") ?? false)
                    {
                        if (string.IsNullOrWhiteSpace(storerCustomSettings.PassKey)) return;
                        if (!message.Text.Contains(storerCustomSettings.PassKey)) return;
                        await SendIp(message, client);
                        return;
                    }

                    await SaveMessageIfPhotoAsync(message, client).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    await client.SendTextMessageAsync(message.Chat.Id, $"Error. {e.Message} {e.StackTrace}",
                        cancellationToken: cancellationToken);
                    throw;
                }
                finally
                {
                    if (messageLeft.TryGetValue(message.Chat.Id, out var left))
                    {
                        if (left.LeftCount == 0)
                            messageLeft.Remove(message.Chat.Id);
                        else
                            messageLeft[message.Chat.Id] = (left.Text, left.LeftCount - 1, left.OriginalMessageId);
                    }
                }
            }
            catch (Exception e)
            {
                log.Error("BotDaemonException", e);
                Console.WriteLine($"BotDaemonException: {e.Message} {e.StackTrace}");
            }
        }

        private async Task SendIp(Message message, ITelegramBotClient client)
        {
            using var httpClient = new HttpClient();
            var ip = await httpClient.GetStringAsync("https://ifconfig.me/ip");
            await client.SendTextMessageAsync(message.Chat.Id, $"{ip}", replyToMessageId: message.MessageId);
            await client.SendTextMessageAsync(message.Chat.Id, $"http://{ip}", replyToMessageId: message.MessageId);
            await client.SendTextMessageAsync(message.Chat.Id, $"https://{ip}", replyToMessageId: message.MessageId);
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

        private async Task SaveMessageIfPhotoAsync(Message message, ITelegramBotClient client)
        {
            if (message.Type != MessageType.Video)
            {
                return;
            }

            var video = message.Video;

            if (video == null)
            {
                return;
            }

            await client.SendTextMessageAsync(message.Chat.Id, $"Downloading...", replyToMessageId: message.MessageId)
                .ConfigureAwait(false);

            //var messageDto = Convert(message, bytes);
            log.Info($"Saving {message.Chat.Id} {message.MessageId}");
            await SaveVideo(video, message, client).ConfigureAwait(false);
        }

        private async Task SaveVideo(Video video, Message message, ITelegramBotClient client)
        {
            if (storerCustomSettings?.RootDir == null)
            {
                log.Info($"Video was not saved. RootDir is null");
                return;
            }

            var folderName = GetFolderName(message);

            var fullFolderPath = Path.Combine(storerCustomSettings.RootDir, folderName);

            if (!Directory.Exists(fullFolderPath))
            {
                Directory.CreateDirectory(fullFolderPath);
            }

            var customFileName = GetFileNameFromMessage(message);
            var videoFileName = video.FileName ?? string.Empty;
            if (videoFileName.Any(c => c is '@' or '!'))
            {
                videoFileName = string.Empty;
            }

            var defaultFileName =
                $"{new string(message.MessageId.ToString().Where(char.IsLetterOrDigit).ToArray())}_{videoFileName}";

            var ext = Path.GetExtension(defaultFileName);
            if (string.IsNullOrWhiteSpace(ext))
            {
                ext = ".mp4";
                defaultFileName += ext;
            }

            var fileName = customFileName != null ? customFileName + ext : defaultFileName;

            if (customFileName == null)
            {
                fileName = $"{DateTime.UtcNow:yyMMdd}_{fileName}";
            }

            var fullFileName = Path.Combine(fullFolderPath,
                fileName);

            var bytes = telegramClient.GetFile(video.FileId);

            await client.SendTextMessageAsync(message.Chat.Id, $"Saving...", replyToMessageId: message.MessageId)
                .ConfigureAwait(false);

            await File.WriteAllBytesAsync(fullFileName, bytes).ConfigureAwait(false);

            await ChangeTitleAsync(fullFileName, fileName, client, message, ext).ConfigureAwait(false);

            await client.SendTextMessageAsync(message.Chat.Id, $"Saved {fullFileName}",
                    replyToMessageId: message.MessageId)
                .ConfigureAwait(false);
        }

        private async Task ChangeTitleAsync(string fullFilePath, string fileName, ITelegramBotClient client,
            Message message, string ext)
        {
            try
            {
                if (ext.ToLower() == ".mov") return;

                using var tfile = TagLib.File.Create(fullFilePath);

                var title = tfile.Tag.Title;
                if (string.IsNullOrWhiteSpace(title)) return;

                // change title in the file
                tfile.Tag.Title = fileName;
                tfile.Save();
            }
            catch (Exception e)
            {
                await client.SendTextMessageAsync(message.Chat.Id, $"Error changing title to {fileName}. {e.Message}",
                        replyToMessageId: message.MessageId)
                    .ConfigureAwait(false);
            }
        }

        private static string GetFolderName(Message message)
        {
            var defaultFolderName = $"{message.Chat.Title}";
            var customFolderName = GetFolderNameFromMessage(message);

            if (customFolderName != null)
            {
                var args = GetArgsFromMessageText(message, out _);
                var useTitleStr = args.SafeGet("title", "true");
                var useTitle = bool.Parse(useTitleStr);
                if (useTitle)
                    customFolderName = Path.Combine(message.Chat.Title ?? string.Empty, customFolderName);
            }

            return customFolderName ?? defaultFolderName;
        }

        private static string? GetFolderNameFromMessage(Message message)
        {
            var args = GetArgsFromMessageText(message, out _);

            return args.GetValueOrDefault("folder");
        }

        private static string? GetFileNameFromMessage(Message messageText)
        {
            var args = GetArgsFromMessageText(messageText, out var delta);

            if (args.TryGetValue("file", out var dir))
            {
                if (delta.HasValue)
                {
                    dir += $"_{delta.Value}";
                }

                return dir;
            }

            return null;
        }

        private static Dictionary<string, string> GetArgsFromMessageText(Message message, out int? delta)
        {
            delta = null;
            var messageText = message.Text ?? GetPreviousMessageText(message, out delta);

            if (string.IsNullOrWhiteSpace(messageText)) return new Dictionary<string, string>();

            var args = messageText.Split([","], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Split(["="], StringSplitOptions.RemoveEmptyEntries))
                .Where(x => x.Length == 2)
                .GroupBy(x => x[0].Trim().ToLower())
                .ToDictionary(x => x.Key, x => x.First()[1].Trim());
            return args;
        }

        private static string? GetPreviousMessageText(Message message, out int? delta)
        {
            delta = null;

            if (messageHistory.TryGetValue((message.Chat.Id, message.MessageId - 1), out var msg))
            {
                if (!string.IsNullOrWhiteSpace(message.MediaGroupId))
                    mediaGroupsHistory[message.MediaGroupId] = (msg, message.MessageId - 1);
                return msg;
            }

            if (!string.IsNullOrWhiteSpace(message.MediaGroupId) &&
                mediaGroupsHistory.TryGetValue(message.MediaGroupId, out var msg2))
            {
                delta = message.MessageId - msg2.OriginalMessageId;
                return msg2.Text;
            }

            if (messageLeft.TryGetValue(message.Chat.Id, out var left))
            {
                delta = message.MessageId - left.OriginalMessageId;
                return left.Text;
            }

            return null;
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