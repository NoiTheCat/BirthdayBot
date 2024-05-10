# Birthday Bot
An automated way to recognize birthdays in your community!

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/J3J65TW2E)

#### Documentation, help, resources
* [Main website, user documentation](https://noithecat.dev/bots/BirthdayBot)
* [Official server](https://discord.gg/JCRyFk7)

#### Running your own instance
You need:
* .NET 8 (https://dotnet.microsoft.com/en-us/)
* PostgreSQL (https://www.postgresql.org/)
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
$ dotnet tool restore
$ dotnet ef database update -- -c path/to/config.json
```

And finally, to run the bot:
```sh
$ dotnet run -c Release -- -c path/to/config.json
```