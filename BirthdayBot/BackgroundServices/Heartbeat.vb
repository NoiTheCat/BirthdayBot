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

        Return Task.CompletedTask
    End Function
End Class
