namespace Tolltech.TelegramCore
{
    public interface ITelegramClient
    {
        byte[] GetFile(string fileId);
    }
}