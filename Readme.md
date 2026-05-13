# Birthday Bot
An automated way to recognize birthdays in your community!

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/J3J65TW2E)

### Documentation, help, resources
* [Main website, user documentation](https://noithecat.dev/bots/BirthdayBot)
* [Official server](https://discord.gg/JCRyFk7)

### Running your own instance
You need:
* .NET 10 (https://dotnet.microsoft.com/en-us/)
* PostgreSQL (https://www.postgresql.org/)
* A Discord bot token (https://discord.com/developers/applications)

#### Cloning
Be sure to pull this repo's submodules, or else the build will fail. Either clone with:
```sh
$ git clone --recurse-submodules https://...
```
Or, if already cloned:
```sh
$ git submodule update --recursive
```

#### Configuration
Get your bot token and set up your database user and schema, then create a JSON file containing the following:
```jsonc
{
    "BotToken": "your bot token here",
    "Database": {
        "Host": "localhost", // optional
        "Database": "birthdaybot", // optional
        "Username": "birthdaybot", // required
        "Password": "birthdaybot" // required; no other authentication methods are currently supported
    }
}
```

#### Usage
To run the bot:
```sh
$ dotnet run -p src/BirthdayBot -c Release -- -c path/to/config.json
```
