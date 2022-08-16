// This file contains global symbols that are used for code completion by IDE's.
// At runtime, any #load directives referencing the .meta folder are ignored

#nullable enable

using System;
using System.Collections.Generic;

public static IModContext ModContext => throw Meta();

private static System.NotImplementedException Meta() =>
    new System.NotImplementedException("Meta declaration not implemented at runtime");

//
// Main API
//

public interface IModContext : IServiceProvider
{
    public string ModId { get; }

    public string ModVersionText { get; }

    public string ApiVersionText { get; }

    public T? GetService<T>();

    public T GetRequiredService<T>();
}

// Base for all mod services
public interface IModService
{
}

// Base for mod services which may affect the world. The world may control access to the API.
public interface IWorldModService : IModService
{
    // Gets whether usage of this service is allowed by the current world
    bool IsAllowed { get; }
}

// Generic event handler for mutable events. Use value types for event args.
public delegate void RefEventHandler<T>(ref T args);

// Generic event handler for immutable events. Use value types for event args.
public delegate void InEventHandler<T>(in T args);

//
// Debug console API
//

public struct ConsoleEventArgs
{
    public ConsoleEventArgs(string command)
    {
        Command = command;
    }

    public string Command { get; }

    public bool IsHandled { get; set; }
}

public interface IConsoleService : IModService
{
    string Write(string text);

    string WriteLine(string text);

    IDisposable AddCommandHandler(RefEventHandler<ConsoleEventArgs> handler);
}

//
// Player position API
//

public struct Vector3
{
    public float X;
    public float Y;
    public float Z;
}

public interface IPlayerPositionService : IWorldModService
{
    // Gets the current player position
    Vector3 Position { get; }

    bool IsFlightEnabled { get; }

    // Try to set the player position. The current world may reject the new position.
    bool TrySetPosition(in Vector3 position, bool allowClipping);

    // Try to set whether flight is enabled. The current world may reject the feature.
    bool TrySetFlightEnabled(bool enabled);

    // Get a list of areas where position setting is restricted
    IList<(Vector3 start, Vector3 end)> GetRestrictedAreas();
}

//
// Event handling API
//

public readonly struct TimedEventArgs
{
    public TimedEventArgs(TimeSpan delta)
    {
        Delta = delta;
    }

    public readonly TimeSpan Delta;
}

public readonly struct WorldLoadedEventArgs
{
    public WorldLoadedEventArgs(string worldId, string worldName)
    {
        WorldId = worldId;
        WorldName = worldName;
    }

    public string WorldId { get; }

    public string WorldName { get; }
}

public interface IGameEventService : IModService
{
    IDisposable AddFrameStartHandler(InEventHandler<TimedEventArgs> handler);

    IDisposable AddFrameEndHandler(InEventHandler<TimedEventArgs> handler);

    IDisposable Schedule(InEventHandler<TimedEventArgs> handler, TimeSpan delay, TimeSpan interval);

    IDisposable AddWorldLoadedHandler(InEventHandler<WorldLoadedEventArgs> handler);
}

//
// World API
//

public interface IPlayer { }

public interface IGameWorldService : IWorldModService
{
    string WorldId { get; }

    string WorldName { get; }

    IEnumerable<IPlayer> Players { get; }
}
