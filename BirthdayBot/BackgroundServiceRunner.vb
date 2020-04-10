Imports System.Threading

''' <summary>
''' Handles the execution of periodic background tasks.
''' </summary>
Class BackgroundServiceRunner
    Const Interval = 8 * 60 ' Tick interval in seconds. Adjust as needed.

    Private ReadOnly Property Workers As List(Of BackgroundService)
    Private ReadOnly Property WorkerCancel As New CancellationTokenSource
    Friend ReadOnly Property BirthdayUpdater As BirthdayRoleUpdate

    Private _workerTask As Task

    Sub New(instance As BirthdayBot)
        BirthdayUpdater = New BirthdayRoleUpdate(instance)
        Workers = New List(Of BackgroundService) From {
            {New GuildStatistics(instance)},
            {New Heartbeat(instance)},
            {BirthdayUpdater},
            {New StaleDataCleaner(instance)}
        }
    End Sub

    Public Sub Start()
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
                    tasks.Add(service.OnTick())
                Next
                Await Task.WhenAll(tasks)
            Catch ex As TaskCanceledException
                Return
            Catch ex As Exception
                Log("Background task", "Unhandled exception in background task thread:")
                Log("Background task", ex.ToString())
            End Try
        End While
    End Function
End Class
