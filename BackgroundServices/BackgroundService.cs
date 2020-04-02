using System.Threading.Tasks;

namespace BirthdayBot.BackgroundServices
{
    abstract class BackgroundService
    {
        protected BirthdayBot BotInstance { get; }

        public BackgroundService(BirthdayBot instance) => BotInstance = instance;

        protected void Log(string message) => Program.Log(GetType().Name, message);

        public abstract Task OnTick();
    }
}
