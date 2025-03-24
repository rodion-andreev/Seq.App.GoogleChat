using System.Threading.Tasks;

namespace Seq.App.GoogleChat.Api
{
    public interface IGoogleChatApi
    {
        Task SendMessageAsync(string webhookUrl, GoogleChatMessage message);
    }
}
