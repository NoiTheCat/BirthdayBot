Imports System.Reflection
Imports Newtonsoft.Json.Linq
Imports System.IO

''' <summary>
''' Loads and holds configuration values.
''' </summary>
Class Configuration
    Public ReadOnly Property BotToken As String
    Public ReadOnly Property DBotsToken As String
    Public ReadOnly Property DatabaseSettings As Database

    Sub New()
        ' Looks for settings.json in the executable directory.
        Dim confPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
        confPath += Path.DirectorySeparatorChar + "settings.json"

        If Not File.Exists(confPath) Then
            Throw New Exception("Settings file not found. " _
                                + "Create a file in the executable directory named 'settings.json'.")
        End If

        Dim jc = JObject.Parse(File.ReadAllText(confPath))
        BotToken = jc("BotToken").Value(Of String)()
        If String.IsNullOrWhiteSpace(BotToken) Then
            Throw New Exception("'BotToken' must be specified.")
        End If

        Dim dbj = jc("DBotsToken")
        If dbj IsNot Nothing Then
            DBotsToken = dbj.Value(Of String)()
        Else
            DBotsToken = Nothing
        End If

        Dim sqlcs = jc("SqlConnectionString").Value(Of String)()
        If String.IsNullOrWhiteSpace(sqlcs) Then
            Throw New Exception("'SqlConnectionString' must be specified.")
        End If
        DatabaseSettings = New Database(sqlcs)
    End Sub
End Class
