Imports System.Threading

''' <summary>
''' Handles the execution of periodic background tasks.
''' </summary>
Class BackgroundServiceRunner
    Const Interval = 45 ' Tick interval in seconds. Adjust as needed.

    Private ReadOnly Property Workers As List(Of BackgroundService)
    Private ReadOnly Property WorkerCancel As New CancellationTokenSource

    Private _workerTask As Task
    Private _tickCount As Integer

    Sub New(instance As BirthdayBot)
        Workers = New List(Of BackgroundService) From {
            {New GuildStatistics(instance)},
            {New Heartbeat(instance)},
            {New BirthdayRoleUpdate(instance)}
        }
    End Sub

    Public Sub Start()
        _tickCount = 0
        _workerTask = Task.Factory.StartNew(AddressOf WorkerLoop, WorkerCancel.Token,
                                            TaskCreationOptions.LongRunning, TaskScheduler.Default)
    End Sub

    Public Async Function Cancel() As Task
        WorkerCancel.Cancel()
        Await _workerTask
    End Function

    ''' <summary>
    ''' *The* background task. Executes service tasks and handles errors.
    ''' </summary>
    Private Async Function WorkerLoop() As Task
        While Not WorkerCancel.IsCancellationRequested
            Try
                ' Delay a bit before we start (or continue) work.
                Await Task.Delay(Interval * 1000, WorkerCancel.Token)

                ' Execute background tasks.
                Dim tasks As New List(Of Task)
                For Each service In Workers
                    tasks.Add(service.OnTick(_tickCount))
                Next
                Await Task.WhenAll(tasks)
            Catch ex As TaskCanceledException
                Return
            Catch ex As Exception
                Log("Background task", "Unhandled exception in background task thread:")
                Log("Background task", ex.ToString())
            End Try
            _tickCount += 1
        End While
    End Function
End Class
