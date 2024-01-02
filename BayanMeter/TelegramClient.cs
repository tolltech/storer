using System.IO;
using Telegram.Bot;
using Tolltech.CoreLib.Helpers;

namespace Tolltech.TelegramCore
{
    public class TelegramClient : ITelegramClient
    {
        private readonly TelegramBotClient client;

        public TelegramClient(TelegramBotClient client)
        {
            this.client = client;
        }

        public byte[] GetFile(string fileId)
        {
            using (var stream = new MemoryStream())
            {
                var file = client.GetFileAsync(fileId).GetAwaiter().GetResult();
                client.DownloadFileAsync(file.FilePath, stream).GetAwaiter().GetResult();
                stream.Seek(0, SeekOrigin.Begin);

                return stream.ReadToByteArray();
            }
        }
    }
}