## Recognize birthdays in your Discord community!

Birthday Bot is a simple, single-purpose bot. It will set a role on your users for the duration of their birthdays and, if desired, can announce a message in a channel of your choosing. Server owners can further specify a default time zone, with individual users also setting their own to ensure everyone's birthdays are recognized precisely on time.

#### Getting started
* Invite the bot. Be mindful that it requires role setting permissions.
* Create a dedicated birthday role to be used only by the bot. Ensure the new role is placed beneath the bot's own role.
  * **Do not use an existing role!** This bot assumes exclusive control over it. Users that have the role but are not having a birthday *will* be removed from it!
* Instruct the bot to use the role: `bb.config role (role name)`

#### Other tips
* Set the birthday announcement channel: `bb.config channel (channel)`
* Set a default time zone: `bb.config zone (time zone)`
  * Use the command `bb.help-tzdata` for information on how to specify time zones.
* Customize the announcement message: See `bb.help-message` for more information.

#### Note
Birthday information is not shared between servers. This is *by design*, as some people prefer to share their birthdays with select groups of people but keep it obscured from other communities.