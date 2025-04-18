﻿using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.UI;
using Direwolf.Contracts;
using Direwolf.Definitions;
using Direwolf.EventHandlers;
using Revit.Async;

namespace Direwolf;

/// <summary>
///     Data analysis core. Handles the queue of <see cref="IHowler" />, the <see cref="WolfpackDB" /> and the serializaion
///     of results.
/// </summary>
public class Direwolf
{
    /// <summary>
    ///     This is a proof of concept, not a production-ready solution. Please **CHANGE THIS** if you plan to deploy.
    /// </summary>
    private static readonly DbConnectionString _default = new("localhost", 5432, "wolf", "awoo", "direwolf");

    private readonly UIApplication? _app;

    private readonly string Desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    private readonly List<HowlId> PreviousHowls = [];

    /// <summary>
    ///     Instance of a Direwolf analyzer.
    /// </summary>
    /// <param name="app">A valid Revit UIApplication context</param>
    public Direwolf(UIApplication app)
    {
        Queries.DatabaseConnectedEventHandler += Queries_DatabaseConnectedEventHandler;
        _app = app;
    }

    /// <summary>
    ///     Instance of a Direwolf analyzer.
    /// </summary>
    /// <param name="howler">Dispatcher</param>
    /// <param name="app">A valid Revit UIApplication context</param>
    public Direwolf(IHowler howler, UIApplication app)
    {
        Howlers.Enqueue(howler);
        howler.HuntCompleted += OnHuntCompleted;
        Queries.DatabaseConnectedEventHandler += Queries_DatabaseConnectedEventHandler;
        _app = app;
    }

    /// <summary>
    ///     Instance of a Direwolf analyzer.
    /// </summary>
    /// <param name="howler">Dispatcher</param>
    /// <param name="instructions">Instructions</param>
    /// <param name="app">A valid Revit UIApplication context</param>
    public Direwolf(IHowler howler, IHowl instructions, UIApplication app)
    {
        howler.CreateWolf(new Wolf(), instructions);
        Howlers.Enqueue(howler);
        howler.HuntCompleted += OnHuntCompleted;
        Queries.DatabaseConnectedEventHandler += Queries_DatabaseConnectedEventHandler;
        _app = app;
    }

    public Direwolf(IHowler howler, IHowl instructions, IWolf wolf, UIApplication app)
    {
        howler.CreateWolf(wolf, instructions);
        Howlers.Enqueue(howler);
        howler.HuntCompleted += OnHuntCompleted;
        Queries.DatabaseConnectedEventHandler += Queries_DatabaseConnectedEventHandler;
        _app = app;
    }

    private Queue<IHowler> Howlers { get; } = [];
    [JsonExtensionData] private WolfpackDB Queries { get; } = new(_default);
    public event EventHandler? DatabaseConnectionEventHandler;
    public event EventHandler? AsyncHuntCompletedEventHandler;

    /// <summary>
    ///     Add a dispatch to the queue.
    /// </summary>
    /// <param name="howler">Dispatch</param>
    public void QueueHowler(IHowler howler)
    {
        ArgumentNullException.ThrowIfNull(howler);
        Howlers.Enqueue(howler);
        howler.HuntCompleted += OnHuntCompleted;
    }

    /// <summary>
    ///     Takes all the contents held inside <see cref="Queries" />, serializes the results to a JSON file in the Desktop
    ///     folder, and sends each <see cref="Wolfpack" /> to the connected database.
    /// </summary>
    public async void SendAllToDB()
    {
        try
        {
            foreach (var q in Queries)
            {
                var fileName = Path.Combine(Desktop, "Queries.json");
                File.WriteAllText(fileName, q.Results);
            }

            Debug.Print(Queries.Count.ToString());

            await Queries.Send();
        }
        catch (Exception e)
        {
            Debug.Print(e.Message);
        }
    }

    /// <summary>
    ///     Performs a synchronous query.
    /// </summary>
    /// <param name="testName">Name of the query</param>
    /// <exception cref="Exception">Thrown whenever the hunt process encounteres a failure</exception>
    public void Hunt(string testName)
    {
        try
        {
            foreach (var howler in Howlers)
            {
                Hunt(howler, out _, testName);
                var h = new HowlId
                {
                    HowlIdentifier = new Guid(),
                    Name = howler.GetType().Name
                };
                PreviousHowls.Add(h);
            }
        }
        catch
        {
            throw new Exception();
        }
    }

    /// <summary>
    ///     Performs a synchronous query.
    /// </summary>
    /// <param name="dispatch">Dispatch</param>
    /// <param name="result">The resulting Wolfpack from the given Dispatch</param>
    /// <param name="testName">Name of the query</param>
    /// <exception cref="Exception">Thrown whenever the hunt process encounteres a failure</exception>
    public void Hunt(IHowler dispatch, out Wolfpack result, string testName)
    {
        try
        {
            result = dispatch.Howl(testName);
            var h = new HowlId
            {
                HowlIdentifier = new Guid(),
                Name = dispatch.GetType().Name
            };
            PreviousHowls.Add(h);
            Queries.Push(result);
        }
        catch
        {
            throw new Exception();
        }
    }

    /// <summary>
    ///     Performs an asynchronous query.
    /// </summary>
    /// <param name="queryName">Name of the query.</param>
    public async void HuntAsync(string queryName = "query")
    {
        AsyncHuntCompletedEventHandler += Direwolf_AsyncHuntCompletedEventHandler;
        RevitTask.Initialize(_app);
        foreach (var howler in Howlers) await HuntTask(howler, queryName);
    }

    /// <summary>
    ///     Creates a valid Revit context, takes a dispatcher from the queue, and dispatches its workers. Resulting
    ///     <see cref="Wolfpack" /> are pushed to the <see cref="WolfpackDB" /> stack.
    /// </summary>
    /// <param name="howler">Dispatch</param>
    /// <param name="queryName">Name of the query</param>
    /// <returns>The execution of a query, resulting in a Wolfpack being pushed to the Query stack.</returns>
    private async Task HuntTask(IHowler howler, string queryName = "query")
    {
        try
        {
            RevitTask.Initialize(_app);
            await RevitTask.RunAsync(() =>
            {
                var results = howler.Howl(queryName);
                Queries.Push(results);
                var h = new HowlId
                {
                    HowlIdentifier = new Guid(),
                    Name = howler.GetType().Name
                };
                PreviousHowls.Add(h);
            });
            AsyncHuntCompletedEventHandler?.Invoke(this, new EventArgs());
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
        }
    }

    public static void WriteDataToJson(object data, string filename, string path)
    {
        var fileName = Path.Combine(path, $"{filename}.json");
        File.WriteAllText(fileName, JsonSerializer.Serialize(data));
    }

    public void WriteQueriesToJson()
    {
        var fileName = Path.Combine(Desktop, "Queries.json");
        File.WriteAllText(fileName, JsonSerializer.Serialize(Queries));
    }

    public string GetQueriesAsJson()
    {
        return JsonSerializer.Serialize(Queries);
    }

    private void OnHuntCompleted(object? sender, HuntCompletedEventArgs e)
    {
        Debug.Print("HuntSuccessful?: " + e.IsSuccessful);
    }

    private void Queries_DatabaseConnectedEventHandler(object? sender, EventArgs e)
    {
        Debug.Print("Database connected!");
    }

    private void Direwolf_AsyncHuntCompletedEventHandler(object? sender, EventArgs e)
    {
        SendAllToDB();
    }
}