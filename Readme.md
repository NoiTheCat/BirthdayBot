# Birthday Bot

Recognize birthdays in your Discord community!

* Info: https://discord.bots.gg/bots/470673087671566366
* Invite: https://discordapp.com/oauth2/authorize?client_id=470673087671566366&scope=bot&permissions=268435456

This bot will automatically add a role to users during their birthdays. If desired, it will also announce birthdays in a channel of your choosing. Time zones are supported per-server and per-user to ensure that birthdays and events are recognized at appropriate times.

## Setup
1. Create a dedicated birthday role for use by the bot.
2. Invite the bot to your server. Ensure that the bot is able to manipulate this role.
3. Instruct the bot to use the role: `bb.config role (role name)`
4. (Optional) Set a birthday announcement channel: `bb.config channel (channel)`
5. (Optional) Set a server default time zone: `bb.config zone (time zone)`

## Other details
* Birthday information is not shared between servers. If you are in multiple servers that this bot is in, you must register your birthday (including time zone) within each server.
  * This is in case one wishes to share their birthday only in certain communities.
* Problematic users? This bot supports blocking certain or all users from using commands. Server moderators are able to issue commands on a user's behalf.
