using System;
using DynamicDialogues.Framework;
using Microsoft.Xna.Framework;
using StardewValley;

namespace DynamicDialogues.Patches;

internal class EventPatches
{
    //for AddScene
    internal static bool PrefixTryGetCommandH(Event __instance, GameLocation location, GameTime time, string[] args) =>
        PrefixTryGetCommand(__instance, location, time, args);

    private static bool PrefixTryGetCommand(Event __instance, GameLocation location, GameTime time, string[] split)
    {
        if (split.Length <= 1) //scene has optional parameters, so its 2 OR more
        {
            return true;
        }
        else if (split[0].Equals(ModEntry.AddScene, StringComparison.Ordinal))
        {
            EventScene.Add(__instance, location, time, split);
            return false;
        }
        else if (split[0].Equals(ModEntry.RemoveScene, StringComparison.Ordinal))
        {
            EventScene.Remove(__instance, location, time, split);
            return false;
        }
        else if(split[0].Equals(ModEntry.PlayerFind, StringComparison.Ordinal))
        {
            Finder.ObjectHunt(__instance, location, time, split);
            return false;
        }
        return true;
    }
}