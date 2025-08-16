using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using ChronoSave.Core;

namespace ChronoSave.Patches
{
    /// <summary>
    /// Harmony patch to inject the ChronoSaveGameComponent into the game.
    /// </summary>
    [HarmonyPatch(typeof(Game), "FillComponents")]
    public static class GameComponentInjectionPatch
    {
        /// <summary>
        /// Postfix patch that adds ChronoSaveGameComponent after the game fills its components.
        /// </summary>
        /// <param name="__instance">The Game instance being patched.</param>
        public static void Postfix(Game __instance)
        {
            // Check if our component already exists
            if (__instance.GetComponent<ChronoSaveGameComponent>() != null)
            {
                return;
            }
            
            try
            {
                // Create and add our component
                ChronoSaveGameComponent component = new ChronoSaveGameComponent(__instance);
                __instance.components.Add(component);
                
                Log.Message("[Chrono Save] Successfully injected ChronoSaveGameComponent.");
            }
            catch (Exception ex)
            {
                Log.Error($"[Chrono Save] Failed to inject game component: {ex}");
            }
        }
    }
}
