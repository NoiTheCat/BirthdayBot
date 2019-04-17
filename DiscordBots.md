## Recognize birthdays in your Discord community!

This bot will automatically set a role to users during their birthdays. If desired, birthdays will also be announced in a channel of your choosing. Time zones are supported per-user as well as per-server, and it is possible to limit usage of the bot if users are being abusive.

#### Getting started
* Invite the bot. Be mindful that it requires role setting permissions.
* Create a dedicated birthday role to be used only by the bot. Ensure the new role is under the bot's role.
  * **Do not use an existing role!** This bot assumes exclusive control over it. Any users without birthdays will have the role automatically removed.
* Instruct the bot to use the role: `bb.config role (role name)`
* Optional: Set a birthday announcement channel: `bb.config channel (channel)`
* Optional: Set a server default time zone: `bb.config zone (time zone)`
  * Use the command `bb.help-tzdata` for information on how to specify time zones.
