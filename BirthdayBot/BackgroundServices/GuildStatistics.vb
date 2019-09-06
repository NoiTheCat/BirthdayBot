Imports System.Net

Class GuildStatistics
    Inherits BackgroundService

    Private ReadOnly Property DBotsToken As String

    Public Sub New(instance As BirthdayBot)
        MyBase.New(instance)
        DBotsToken = instance.Config.DBotsToken
    End Sub

    Public Overrides Async Function OnTick(tick As Integer) As Task
        ' Activate roughly every 2 hours (interval: 45)
        If tick Mod 160 <> 2 Then Return

        Dim count = BotInstance.DiscordClient.Guilds.Count
        Log($"Currently in {count} guild(s).")

        Await SendExternalStatistics(count)
    End Function

    ''' <summary>
    ''' Send statistical information to external services.
    ''' </summary>
    ''' <remarks>
    ''' Only Discord Bots is currently supported. No plans to support others any time soon.
    ''' </remarks>
    Async Function SendExternalStatistics(guildCount As Integer) As Task
        Dim rptToken = BotInstance.Config.DBotsToken
        If rptToken Is Nothing Then Return

        Const apiUrl As String = "https://discord.bots.gg/api/v1/bots/{0}/stats"
        Using client As New WebClient()
            Dim uri = New Uri(String.Format(apiUrl, CType(BotInstance.DiscordClient.CurrentUser.Id, String)))
            Dim data = "{ ""guildCount"": " + CType(guildCount, String) + " }"
            client.Headers(HttpRequestHeader.Authorization) = rptToken
            client.Headers(HttpRequestHeader.ContentType) = "application/json"
            Try
                Await client.UploadStringTaskAsync(uri, data)
                Log("Discord Bots: Report sent successfully.")
            Catch ex As WebException
                Log("Discord Bots: Encountered an error. " + ex.Message)
            End Try
        End Using
    End Function
End Class
