''' <summary>
''' Base class for background services.
''' Background services are called periodically by another class.
''' </summary>
MustInherit Class BackgroundService
    Protected ReadOnly Property BotInstance As BirthdayBot

    Sub New(instance As BirthdayBot)
        BotInstance = instance
    End Sub

    Sub Log(message As String)
        Program.Log(Me.GetType().Name, message)
    End Sub

    MustOverride Function OnTick(tick As Integer) As Task
End Class
