using HarmonyLib;
using RustServerMetrics.HarmonyPatches.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

// ReSharper disable once InconsistentNaming

namespace RustServerMetrics.HarmonyPatches.Delayed;

[DelayedHarmonyPatch]
[HarmonyPatch]
internal static class InvokeHandlerBase_DoTick_Patch
{
    #region Members

    private static readonly Stopwatch Stopwatch = new();

    private static readonly CodeMatch[] NeedleSequenceToFind =
    {
        CodeMatch.LoadsField(AccessTools.Field(typeof(InvokeAction), nameof(InvokeAction.action))),
        CodeMatch.Calls(AccessTools.Method(typeof(Action), nameof(Action.Invoke)))
    };

    private static readonly CodeInstruction[] SequenceToInject =
    {
        new(OpCodes.Call, AccessTools.Method(typeof(InvokeHandlerBase_DoTick_Patch), nameof(InvokeWrapper)))
    };

    #endregion

    #region Patching
        
    [HarmonyPrepare]
    public static bool Prepare()
    {
        // ReSharper disable once InvertIf
        if (!RustServerMetricsLoader.__serverStarted)
        {
            UnityEngine.Debug.Log("Note: Cannot patch InvokeHandlerBase_DoTick_Patch yet. We will patch it upon server start.");
            return false;
        }

        return true;
    }

    [HarmonyTargetMethods]
    public static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.DeclaredMethod(typeof(InvokeHandlerBase<InvokeHandler>), nameof(InvokeHandlerBase<InvokeHandler>.DoTick));
    }

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> originalInstructions)
    {
        var instructionsList = originalInstructions.ToList();
        
        try
        {
            var codeMatcher = new CodeMatcher(instructionsList);

            codeMatcher.MatchStartForward(NeedleSequenceToFind)
                       .ThrowIfInvalid("Unable to find the expected injection point")
                       .RemoveInstructions(2)
                       .InsertAndAdvance(SequenceToInject);

            return codeMatcher.Instructions();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[ServerMetrics] {nameof(InvokeHandlerBase_DoTick_Patch)}: " + e.Message);
            return instructionsList;
        }
    }
        
    #endregion

    #region Handler
        
    private static void InvokeWrapper(InvokeAction invokeAction)
    {
        try
        {
            Stopwatch.Restart();
            invokeAction.action.Invoke();
        }
        finally
        {
            Stopwatch.Stop();
            MetricsLogger.Instance?.ServerInvokes.LogTime(invokeAction.action.Method, Stopwatch.Elapsed.TotalMilliseconds);
        }
    }
        
    #endregion
}