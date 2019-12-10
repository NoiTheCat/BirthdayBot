''' <summary>
''' Basic heartbeat function - hints that the background task is still alive.
''' </summary>
Class Heartbeat
    Inherits BackgroundService

    Public Sub New(instance As BirthdayBot)
        MyBase.New(instance)
    End Sub

    Public Overrides Function OnTick() As Task
        Dim uptime = DateTimeOffset.UtcNow - Program.BotStartTime
        Log($"Bot uptime: {BotUptime()}")

        ' Disconnection warn
        For Each shard In BotInstance.DiscordClient.Shards
            If shard.ConnectionState = Discord.ConnectionState.Disconnected Then
                Log($"Shard {shard.ShardId} is disconnected! Restart the app if this persists.")
                ' The library alone cannot be restarted as it is in an unknown state. It was not designed to be restarted.
                ' TODO This is the part where we'd signal something to restart us if we were fancy.
            End If
        Next

        Return Task.CompletedTask
    End Function
End Class
