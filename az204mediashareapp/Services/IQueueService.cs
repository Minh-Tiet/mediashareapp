using System.Threading.Tasks;

namespace MVCMediaShareAppNew.Services
{
    public interface IQueueService
    {
        Task SendMessageAsync(string message);
    }
} 