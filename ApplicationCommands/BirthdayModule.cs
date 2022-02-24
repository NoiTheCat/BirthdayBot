using Discord.Interactions;

namespace BirthdayBot.ApplicationCommands;

[Group("birthday", "Commands relating to birthdays.")]
public class BirthdayModule : BotModuleBase {
    public const string HelpRemove = "Removes your birthday information from this bot.";

    [Group("set", "Subcommands for setting birthday information.")]
    public class SubCmdsBirthdaySet : BotModuleBase {
        public const string HelpSetBday = "Sets or updates your birthday.";
        public const string HelpSetZone = "Sets or updates your time zone, when your birthday is already set.";
        
        [SlashCommand("date", HelpSetBday)]
        public async Task CmdSetBday([Summary(description: HelpOptDate)] string date,
                                     [Summary(description: HelpOptPfxOptional + HelpOptZone)] string? zone = null) {
            int inmonth, inday;
            try {
                (inmonth, inday) = ParseDate(date);
            } catch (FormatException e) {
                // Our parse method's FormatException has its message to send out to Discord.
                await RespondAsync(e.Message, ephemeral: true).ConfigureAwait(false);
                return;
            }

            string? inzone = null;
            if (zone != null) {
                try {
                    inzone = ParseTimeZone(zone);
                } catch (FormatException e) {
                    await ReplyAsync(e.Message).ConfigureAwait(false);
                    return;
                }
            }

            var user = await Context.GetGuildUserConfAsync().ConfigureAwait(false);
            await user.UpdateAsync(inmonth, inday, inzone ?? user.TimeZone).ConfigureAwait(false);

            await RespondAsync($":white_check_mark: Your birthday has been set to **{FormatDate(inmonth, inday)}**" +
                (inzone == null ? "" : $", with time zone {inzone}") + ".");
        }

        [SlashCommand("zone", HelpSetZone)]
        public async Task CmdSetZone([Summary(description: HelpOptZone)] string zone) {
            var user = await Context.GetGuildUserConfAsync().ConfigureAwait(false);
            if (!user.IsKnown) {
                await RespondAsync(":x: You must first set your birthday to use this command.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            string inzone;
            try {
                inzone = ParseTimeZone(zone);
            } catch (FormatException e) {
                await RespondAsync(e.Message, ephemeral: true).ConfigureAwait(false);
                return;
            }
            await user.UpdateAsync(user.BirthMonth, user.BirthDay, inzone).ConfigureAwait(false);
            await RespondAsync($":white_check_mark: Your time zone has been set to **{inzone}**.").ConfigureAwait(false);
        }
    }

    [SlashCommand("remove", HelpRemove)]
    public async Task CmdRemove() {
        var user = await Context.GetGuildUserConfAsync().ConfigureAwait(false);
        if (user.IsKnown) {
            await user.DeleteAsync().ConfigureAwait(false);
            await RespondAsync(":white_check_mark: Your information for this server has been removed.");
        } else {
            await RespondAsync(":white_check_mark: This bot already does not have your birthday for this server.");
        }
    }
}