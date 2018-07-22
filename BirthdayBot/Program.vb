Option Strict On
Option Explicit On
Imports Discord
Imports Discord.WebSocket

Module Program
    Private _bot As BirthdayBot

    Sub Main(args As String())
        Dim cfg As New Configuration()

        Dim dc As New DiscordSocketConfig()
        With dc
            .AlwaysDownloadUsers = True
            .DefaultRetryMode = Discord.RetryMode.RetryRatelimit
            .MessageCacheSize = 0
        End With

        Dim client As New DiscordSocketClient(dc)
        AddHandler client.Log, AddressOf DNetLog

        _bot = New BirthdayBot(cfg, client)

        AddHandler Console.CancelKeyPress, AddressOf OnCancelKeyPressed

        _bot.Start().Wait()
    End Sub

    ''' <summary>
    ''' Sends a formatted message to console.
    ''' </summary>
    Sub Log(source As String, message As String)
        ' Add file logging later?
        Dim ts = DateTime.UtcNow
        Dim ls = {vbCrLf, vbLf}
        For Each item In message.Split(ls, StringSplitOptions.None)
            Console.WriteLine($"{ts:u} [{source}] {item}")
        Next
    End Sub

    Private Function DNetLog(arg As LogMessage) As Task
        If arg.Severity <= LogSeverity.Info Then
            Log("Discord.Net", $"{arg.Severity}: {arg.Message}")
        End If
        Return Task.CompletedTask
    End Function

    Private Sub OnCancelKeyPressed(sender As Object, e As ConsoleCancelEventArgs)
        e.Cancel = True
        Log("Shutdown", "Caught cancel key. Will shut down...")
        Dim hang = Not _bot.Shutdown().Wait(10000)
        If hang Then
            Log("Shutdown", "Normal shutdown has not concluded after 10 seconds. Will force quit.")
        End If
        Environment.Exit(0)
    End Sub

End Module
