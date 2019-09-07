''' <summary>
''' Basic heartbeat function - indicates that the background task is still functioning.
''' </summary>
Class Heartbeat
    Inherits BackgroundService

    Public Sub New(instance As BirthdayBot)
        MyBase.New(instance)
    End Sub

    Public Overrides Function OnTick(tick As Integer) As Task
        ' Print a message roughly every 15 minutes (assuming 45s per tick).
        If tick Mod 20 = 0 Then
            Dim uptime = DateTimeOffset.UtcNow - Program.BotStartTime
            Log($"Tick {tick:00000} - Bot uptime: {BotUptime()}")
        End If

        ' Disconnection warn
        For Each shard In BotInstance.DiscordClient.Shards
            If shard.ConnectionState = Discord.ConnectionState.Disconnected Then
                Log($"Shard {shard.ShardId} is disconnected! Restart the app if this persists.")
                ' The library alone cannot be restarted as it is in an unknown state. It was not designed to be restarted.
                ' This is the part where we'd signal something to restart us if we were fancy.
            End If
        Next

        Return Task.CompletedTask
    End Function
End Class
