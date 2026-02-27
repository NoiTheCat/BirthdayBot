---
layout: default
title: Documentation
---
# Birthday Bot

**An automated way to recognize birthdays in your community!**

Birthday Bot is a utility for your community which enables your users to share their birthdays, check birthdays of others, and mark and announce birthdays as they happen. Server moderators can additionally set up a default time zone with users having the option to set their own, ensuring all birthdays are recognized precisely on time.

### Getting started
Invite the bot via the link on the sidebar. Please note that several permissions are requested during the invite process which are required for the bot to function properly.

Once added, a small amount of initial set-up before it's ready for use:
* Create a dedicated birthday role to be used only by the bot. Ensure the new role is placed beneath the bot's own role.
* Instruct the bot to use the role: `/config role set-birthday-role`
> <p style="font-style: normal"><svg xmlns="http://www.w3.org/2000/svg" style="vertical-align: -0.125em;" width="1em" height="1em" viewBox="0 0 16 16"><g fill="currentColor"><path d="M4.54.146A.5.5 0 0 1 4.893 0h6.214a.5.5 0 0 1 .353.146l4.394 4.394a.5.5 0 0 1 .146.353v6.214a.5.5 0 0 1-.146.353l-4.394 4.394a.5.5 0 0 1-.353.146H4.893a.5.5 0 0 1-.353-.146L.146 11.46A.5.5 0 0 1 0 11.107V4.893a.5.5 0 0 1 .146-.353L4.54.146zM5.1 1L1 5.1v5.8L5.1 15h5.8l4.1-4.1V5.1L10.9 1H5.1z"/><path d="M7.002 11a1 1 0 1 1 2 0a1 1 0 0 1-2 0zM7.1 4.995a.905.905 0 1 1 1.8 0l-.35 3.507a.552.552 0 0 1-1.1 0L7.1 4.995z"/></g></svg> <strong>Do not use an existing role!</strong> This bot will take full and exclusive control over it. Users that have the role but are not having a birthday
  *will* be removed from it!</p>

Optional, but recommended:
* Customize the announcement message: See `/config announce help` for more information.
* Set the birthday announcement channel: `/config announce set-channel`
* Set a default time zone: `/config set-timezone`

### Supporting the bot
Birthday Bot is provided for free, period. No paywalled features, subscriptions, or monetization insentices. This is an independent hobby project done in my spare time, and all costs associated with it come out of my pocket. My only interest is to provide something that I hope others find as useful as I do.

That said, this bot has proven to be far more popular than I ever anticipated, and keeping things running has occasionally strained me both financially and time-wise. If you'd like, please consider pitching in a bit to cover my recurring costs by checking out my Ko-fi page on the sidebar.

### Privacy and Security
This bot collects and stores only information necessary for its operation, then associated to user, server, and role IDs. As little information is stored as possible and access to the database is strongly restricted through proper security practices.

Birthday information is not shared between servers *by design*, for those preferring to be selective about where they want their information known. Users must set their birthday settings in each individual server.

Any questions and concerns regarding data privacy, security, and retention may be sent to the bot author via the support server or by opening an issue on GitHub.

### Image credit
<a href="https://www.flaticon.com/free-icons/cake" title="cake icons">Cake icons created by Freepik - Flaticon</a>
