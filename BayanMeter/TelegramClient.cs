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
            var file = client.GetFileAsync(fileId).GetAwaiter().GetResult();

            //for using without local server
            // using var stream = new MemoryStream();
            // client.DownloadFileAsync(file.FilePath, stream).GetAwaiter().GetResult();
            // stream.Seek(0, SeekOrigin.Begin);
            //
            // return stream.ReadToByteArray();

            return File.ReadAllBytes(file.FilePath!);
        }
    }
}