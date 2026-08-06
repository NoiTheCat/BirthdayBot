using BirthdayBot.Data;
using Discord;
using Discord.Interactions;
using static BirthdayBot.Localization.StringProviders;

namespace BirthdayBot.InteractionModules;

public class AnnouncementMsgModal : IModal
{
    public const string CustomId = "edit-announce";
    const string TxSingleId = "msg-single";
    const string TxMultiId = "msg-multi";

    string IModal.Title => "ignored";

    [ModalTextInput(TxSingleId)]
    public string? TextSingle { get; set; }
    [ModalTextInput(TxMultiId)]
    public string? TextMulti { get; set; }

    public static Modal Create(GuildConfig settings, string locUser, string locGuild)
    {
        return new ModalBuilder
        {
            Title = Responses.Get(locUser, "config.announce.set-message.formTitle"),
            CustomId = CustomId,
        }.AddTextInput(
                label: Responses.Get(locUser, "config.announce.set-message.labelSingle"),
                customId: TxSingleId,
                style: TextInputStyle.Paragraph,
                maxLength: 1500,
                required: false,
                placeholder: Responses.Get(locGuild, "defaultSingle"),
                value: settings.AnnounceMessage ?? string.Empty
            ).AddTextInput(
                label: Responses.Get(locUser, "config.announce.set-message.labelMulti"),
                customId: TxMultiId,
                style: TextInputStyle.Paragraph,
                maxLength: 1500,
                required: false,
                placeholder: Responses.Get(locGuild, "defaultMulti"),
                value: settings.AnnounceMessagePl ?? string.Empty
            ).Build();
    }
}
