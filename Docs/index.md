---
layout: default
title: Documentation
---
# Birthday Bot

**Recognize birthdays in your Discord community!**

Birthday Bot is a simple, single-purpose bot. It will set a role on your users for the duration of their birthdays and, if desired, can
announce a message in a channel of your choosing. Server owners can further specify a default time zone, with individual users also setting
their own to ensure everyone's birthdays are recognized precisely on time.

#### Getting started
This bot requires a small amount of initial set-up before it's ready for use. To quickly get started, ensure that you:
* Create a dedicated birthday role to be used only by the bot. Ensure the new role is placed beneath the bot's own role.
  * **Do not use an existing role!** This bot assumes exclusive control over it. Users that have the role but are not having a birthday
  *will* be removed from it!
* Instruct the bot to use the role: `/config role set-birthday-role`
At this point, you may also wish to do the following optional steps:
* Set the birthday announcement channel: `/config announce set-channel`
* Set a default time zone: `/config set-timezone`
* Customize the announcement message: See `/config announce help` for more information.

#### Time zone support
You may specify a time zone in order to have your birthday recognized with respect to your local time. This bot only accepts zone names
from the IANA Time Zone Database (a.k.a. Olson Database).
* To find your zone: https://xske.github.io/tz/
* Interactive map: https://kevinnovak.github.io/Time-Zone-Picker/
* Complete list: https://en.wikipedia.org/wiki/List_of_tz_database_time_zones

#### Support the bot
Birthday Bot is and shall remain fully free to use. I have no plans to hide any new or existing features behind pay-only, premium features.
This is an independent hobby project and all costs associated with it come out of my pocket.

This bot has had a far greater response than I've ever expected, and at this point I find it difficult to pay for the server it runs on as
its resource needs grow. I would greatly appreciate if you consider pitching in a little bit to cover my recurring costs by checking out my
Ko-fi page: https://ko-fi.com/noithecat.

#### Support, Privacy and Security
The support server for my bots can be accessed via the given link: https://discord.gg/JCRyFk7. Further information in setting up the bot
can be found within it, as well as a small group of volunteers who are willing to answer questions.

This bot collects and stores only information necessary for its operation, including user, server, and role IDs. This data is not stored
indefinitely, but is removed after an extended period of lack of use. If an individual member of a server is not seen after 360 days, their
data is purged. If the bot has been removed from a server, all data (including of its associated users) is purged after 180 days.

Birthdays are not shared between servers that this same bot may be in. This is *by design*, for those preferring to share their birthdays
with only certain communities instead of automatically sharing it to all of them. Users must enter their birthday information onto every
server they share with the bot for the servers they wish for it to be known in.

Any questions and concerns regarding data and security may be sent to the bot author via the support server or GitHub.

#### Image credit
<a href="https://www.flaticon.com/free-icons/cake" title="cake icons">Cake icons created by Freepik - Flaticon</a>