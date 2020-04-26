# Birthday Bot

* Info: https://discord.bots.gg/bots/470673087671566366

## Recognize birthdays in your Discord community!

Birthday Bot is a simple, single-purpose bot. It will set a role on your users for the duration of their birthdays and, if desired, can announce a message in a channel of your choosing. Server owners can further specify a default time zone, with individual users also setting their own to ensure everyone's birthdays are recognized precisely on time.

#### Getting started
This bot requires a small amount of initial set-up before it's ready for use. To quickly get started, ensure that you:
* Create a dedicated birthday role to be used only by the bot. Ensure the new role is placed beneath the bot's own role.
  * **Do not use an existing role!** This bot assumes exclusive control over it. Users that have the role but are not having a birthday *will* be removed from it!
* Instruct the bot to use the role: `bb.config role (role name)`
At this point, you may also wish to do the following optional steps:
* Set the birthday announcement channel: `bb.config channel (channel)`
* Set a default time zone: `bb.config zone (time zone)`
  * Use the command `bb.help-tzdata` for information on how to specify time zones.
* Customize the announcement message: See `bb.help-message` for more information.

#### Support the bot
Birthday Bot is and shall remain free. I have no plans to hide new or existing features behind pay-only, premium features. This is an independent hobby project and all costs associated with it come out of my pocket.

This bot has had a far greater response than I've ever expected, and at this point I find it difficult to pay for the server it runs on as its resource needs grow. I would greatly appreciate if you consider pitching in a little bit to cover my recurring costs by checking out my Patreon link: https://www.patreon.com/noibots.

#### Support, Privacy and Security
The support server for my bots can be accessed via the given link: https://discord.gg/JCRyFk7. Further information in setting up the bot can be found within it, as well as a small group of volunteers who are willing to answer questions.

This bot collects and stores only information necessary for its operation, including user, server, and role IDs. This data is not stored indefinitely, and is removed after some period of time depending on if the bot has been removed from a respective server or its users have been absent for a long enough time.

Information is not shared between servers. This is *by design*, for those preferring to share their birthdays with only certain communities instead of automatically sharing it to all of them. Users must enter their birthday information onto every server they share with the bot for the servers they wish for it to be known in.

Any questions and concerns regarding data and security may be sent to the bot author via the support server or GitHub.

#### Image credit
The icon used by this bot is from Flaticon, found at: https://www.flaticon.com/free-icon/birthday-cake_168532