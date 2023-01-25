# Birthday Bot
An automated way to recognize birthdays in your community!

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/J3J65TW2E)

#### Documentation, help, resources
* [Main website, user documentation](https://noithecat.dev/bots/BirthdayBot)
* [Official server](https://discord.gg/JCRyFk7)

#### Running your own instance
You need:
* .NET 6 (https://dotnet.microsoft.com/en-us/)
* PostgreSQL (https://www.postgresql.org/)
* EF Core tools (https://learn.microsoft.com/en-us/ef/core/get-started/overview/install#get-the-entity-framework-core-tools)
* A Discord bot token (https://discord.com/developers/applications)

Get your bot token and set up your database user and schema, then create a JSON file containing the following:
```jsonc
{
    "BotToken": "your bot token here",
    "SqlHost": "localhost", // optional
    "SqlDatabase": "birthdaybot", // optional
    "SqlUser": "birthdaybot", // required
    "SqlPassword": "birthdaybot" // required; no other authentication methods are currently supported
}
```

Then run the following commands:
```sh
$ dotnet restore
$ dotnet ef database update -- -c path/to/config.json
```

And finally, to run the bot:
```
$ dotnet run -c Release -- -c path/to/config.json
```