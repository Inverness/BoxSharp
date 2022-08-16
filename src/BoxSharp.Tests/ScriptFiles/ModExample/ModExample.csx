#load ".meta/globals.csx"

var console = ModContext.GetRequiredService<IConsoleService>();
var gameEvent = ModContext.GetRequiredService<IGameEventService>();
var playerPosition = ModContext.GetRequiredService<IPlayerPositionService>();

console.WriteLine("Starting my mod!");

// Setup an event to enable flight whenever a world is loaded 

gameEvent.AddWorldLoadedHandler(OnWorldLoaded);

void OnWorldLoaded(in WorldLoadedEventArgs args)
{
    if (!playerPosition.IsAllowed)
    {
        console.WriteLine("Player position API not allowed in this world");
        return;
    }

    if (!playerPosition.TrySetFlightEnabled(true))
    {
        console.WriteLine("Unable to enable flight");
        return;
    }

    console.WriteLine("Enabled flight!");
}
