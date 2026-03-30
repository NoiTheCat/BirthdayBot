# Localization

Or *Localisation*, if you'd like.

Translations are welcomed! As I'm still figuring things out with this project, there are currently no detailed guidelines to follow. Even partial translations would be beneficial. If you would like to contribute, please submit a pull request or discuss your contribution with the maintainer over Discord.

In short, it's strongly desirable that any contributed translations:

1. Remain brief, not adding any information not present in the original language.
2. Remain neutral in tone.
3. Use appropriately simple and accessible wording and grammar.

Contributors shall be credited in the text of the `/help` command if desired.

### Structure
Within this directory are `json` files with file names fitting the pattern of "`type`.`locale`.json". See the table below for all available locale codes.

The following descriptions are added for completeness but when in doubt, refer to the default localization files.

#### Commands
`Commands` contains all command names and descriptions that appear in the UI as a user searches for and types out a command. The nested structure in this file, and even the file names, must be strictly adhered to in order for command registration (handled by the library) to be processed properly.

Example structure for a hypothetical (broken) Latin translation:
```json
{
    "my-command": { // The root definition for a command. Must match the "name" value that's used in the default locale.
        "name": "mandatum-meum", // This changes the command from the user's perspective from "/my-command" to "/mandatum-meum".
        "description": "Facit ut automatum dicat \"meum\".", // The tooltip or help text that appears beside the command in the UI.
        "word": { // A parameter, originally labeled "word".
            "name": "verbum",
            "description": "Verbum loco utendum."
        }
    }
}
```
Each command may contain further nested definitions for each parameter, for which the name and description can also be altered.

#### Responses
`Responses` contains all remaining strings used when responding to user input. The structure is up to the project maintainer's discretion and an attempt is made to keep it roughly organized similarly to `Commands` but with a sensible layout. In some cases, there are formatting placeholders (resembling `{0}`, `{1}`, ...) in the original language that **must** be preserved in a translation.

Example:
```json
{
    "my-command": {
        "defaultReply": "Verbum!",
        "errBadWord": "Heus! {0} verbum improbum me dicere cogere conatus est."
    }
}
```

### Useful tools
Several tools exist out there that can simplify the process of editing localization files, including side-by-side views.

To do: Find some good ones to recommend here.

### Languages
All languages supported by Discord are shown on this table along with their supported status.

| Status | Locale | Local name | English name |
| - | - | - | - |
| | bg | български | Bulgarian |
| | cs | Čeština | Czech |
| | da | Dansk | Danish |
| | de | Deutsch | German |
| | el | Ελληνικά | Greek |
| | en-GB | UK English | UK English |
| default | en-US | US English | US English |
| | es-ES | Español de España | Spanish (Spain) |
| | es-419 | Español de América | Spanish (Latin America) |
| | fi | Suomi | Finnish |
| | fr | Français | French |
| | hi | हिन्दी | Hindi |
| | hr | Hrvatski | Croatian |
| | hu | Magyar | Hungarian |
| | ja | 日本語 | Japanese |
| | ko | 한국어 | Korean |
| | id | Bahasa Indonesia | Indonesian |
| | it | Italiano | Italian |
| | lt | Lietuviškai | Lithuanian |
| | nl | Nederlands | Dutch |
| | no | Norsk | Norwegian |
| | pl | Polski | Polish |
| | pt-BR | Português do Brasil | Brazilian Portuguese |
| | ro | Română | Romanian |
| [PR #36](https://github.com/NoiTheCat/BirthdayBot/issues/36) | ru | Pусский | Russian |
| | sv-SE | Svenska | Swedish |
| | th | ไทย | Thai |
| | tr | Türkçe | Turkish |
| | uk | Українська | Ukrainian |
| | vi | Tiếng Việt | Vietnamese |
| | zh-CN | 中文 | Chinese (Simplified) |
| | zh-TW | 繁體中文 | Chinese (Traditional) |
