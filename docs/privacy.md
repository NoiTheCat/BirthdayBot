---
layout: default
title: Privacy
---
# Privacy
I've noticed that most small bots don't really spell out how they handle the data that users are entrusting to them. As a matter of principle and as one who tends to be concerned and interested regarding these sorts of things in general, I try to take security and transparency of data collected by my bots very seriously. The following hopefully explains in adequate detail how your information, should you choose to use this bot, is handled.

Any questions and concerns regarding data privacy, security, and retention may be sent to the bot author via the support server or email.

### Data policy
This bot attempts to follow data minimization practices as strictly as possible.

Any personally identifiable information retained by the bot in the long term (that is, a time period exceeding 72 hours) is limited to Discord-assigned server and user IDs and used only associate relevant pairings of Discord servers and users with information provided by said users or moderators. Any further identifiable information such as usernames, nicknames, roles, etc. are obtained via Discord using these IDs and used only upon generating any sort of output. This additional identifiable information may be held for a period up to 72 hours before being discarded and is only used to improve the bot's responsiveness in case a particular user makes multiple requests in a short time period.

### Retention
In the event that a bot leaves a server for any reason, server-wide configuration and user data is retained for a period of time\* until it is automatically discarded. Once removed, there are no guarantees that the data can be recovered afterwards.<br />
<small>\* Such is the intent - however, an issue that arose after version 4.0.0 has temporarily put all of this on hold. This page shall be updated when the issue is resolved.</small>

### User privacy
Birthday information is not shared between servers *by design*, for those preferring to be selective about where they want their information known. Users must set their birthday settings in each individual server.

### Security
The database is stored in a containerized environment in a private subnet with no direct internet access. The software is frequently updated to keep up mitigations against potential new security vulnerabilities. Access policies adhere to the principle of least privilege. Backups are automatically generated every few hours and placed in a location with equivalent policies, encrypted, and retained for several weeks.
