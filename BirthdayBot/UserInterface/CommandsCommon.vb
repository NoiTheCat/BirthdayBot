Option Strict On
Option Explicit On
Imports System.Text.RegularExpressions
Imports Discord.WebSocket
Imports NodaTime

''' <summary>
''' Common base class for common constants and variables.
''' </summary>
Friend MustInherit Class CommandsCommon
    Public Const CommandPrefix = "bb."
    Public Const GenericError = ":x: Invalid usage. Consult the help command."
    Public Const ExpectedNoParametersError = ":x: This command does not take parameters. Did you mean to use another?"

    Delegate Function CommandHandler(param As String(), reqChannel As SocketTextChannel, reqUser As SocketGuildUser) As Task

    Protected Shared ReadOnly Property TzNameMap As Dictionary(Of String, String)
        Get
            If _tzNameMap Is Nothing Then
                ' Because IDateTimeZoneProvider.GetZoneOrNull is not case sensitive:
                ' Getting every existing zone name and mapping it onto a dictionary. Now a case-insensitive
                ' search can be made with the accepted value retrieved as a result.
                _tzNameMap = New Dictionary(Of String, String)(StringComparer.InvariantCultureIgnoreCase)
                For Each name In DateTimeZoneProviders.Tzdb.Ids
                    _tzNameMap.Add(name, name)
                Next
            End If
            Return _tzNameMap
        End Get
    End Property
    Protected Shared ReadOnly ChannelMention As New Regex("<#(\d+)>")
    Protected Shared ReadOnly UserMention As New Regex("<@\!?(\d+)>")
    Private Shared _tzNameMap As Dictionary(Of String, String) ' Value set by getter property on first read

    Protected ReadOnly Instance As BirthdayBot
    Protected ReadOnly BotConfig As Configuration
    Protected ReadOnly Discord As DiscordSocketClient

    Sub New(inst As BirthdayBot, db As Configuration)
        Instance = inst
        BotConfig = db
        Discord = inst.DiscordClient
    End Sub

    ''' <summary>
    ''' Checks given time zone input. Returns a valid string for use with NodaTime.
    ''' </summary>
    ''' <param name="tzinput"></param>
    ''' <returns></returns>
    Protected Function ParseTimeZone(tzinput As String) As String
        Dim tz As String = Nothing
        If tzinput IsNot Nothing Then
            ' Just check if the input exists in the map. Get the "true" value, or reject it altogether.
            If Not TzNameMap.TryGetValue(tzinput, tz) Then
                Throw New FormatException(":x: Unknown or invalid time zone name.")
            End If
        End If
        Return tz
    End Function

    ''' <summary>
    ''' On command dispatcher initialization, it will retrieve all available commands through here.
    ''' </summary>
    Public MustOverride ReadOnly Property Commands As IEnumerable(Of (String, CommandHandler))
End Class
