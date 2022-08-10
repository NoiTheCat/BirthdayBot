namespace NoiTheCat;
static class TextCommandRemovalWarning {
    public const string StopUsingTextCommands = ":warning: **Reminder**: Text-based commands will be phased out by the end of August. " +
        "Please switch to using slash commands. For details on their usage, use this bot's `/help` command.";
    private static readonly RateLimit<ulong> _warnedList = new(8 * 60 * 60); // 8 hours

    public static void Intercept(SocketMessage msg, ulong gid) {
        lock (_warnedList) {
            if (!_warnedList.IsPermitted(gid)) return;
            try {
                msg.Channel.SendMessageAsync(StopUsingTextCommands).GetAwaiter().GetResult();
            } catch (Exception e) {
                _warnedList.Reset(gid);
                Console.WriteLine(e.ToString());
            }
        }
    }
    private class RateLimit<T> where T : notnull {
        private const int DefaultTimeout = 20;
        public int Timeout { get; }
        private Dictionary<T, DateTime> Entries { get; } = new Dictionary<T, DateTime>();
        public RateLimit() : this(DefaultTimeout) { }
        public RateLimit(int timeout) {
            if (timeout < 0) throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout valie cannot be negative.");
            Timeout = timeout;
        }
        public bool IsPermitted(T value) {
            if (Timeout == 0) return true;
            var now = DateTime.Now;
            var expired = Entries.Where(x => x.Value.AddSeconds(Timeout) <= now).Select(x => x.Key).ToList();
            foreach (var item in expired) Entries.Remove(item);
            if (Entries.ContainsKey(value)) return false;
            else {
                Entries.Add(value, DateTime.Now);
                return true;
            }
        }
        public bool Reset(T value) => Entries.Remove(value);
    }

}