using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RustServerMetrics;

public static class PatchHelper
{
    public static bool InjectBeforeSequence(
        this List<CodeInstruction> haystack,
        IEnumerable<CodeInstruction> needle,
        IEnumerable<CodeInstruction> injection)
    {
        var needleIndex = haystack.FindSequenceIndex(needle);
        if (needleIndex == -1)
        {
            return false;
        }

        haystack.InsertRange(needleIndex, injection);

        return true;
    }

    public static bool InjectAfterSequence(
        this List<CodeInstruction> haystack,
        IEnumerable<CodeInstruction> needle,
        IEnumerable<CodeInstruction> injection)
    {
        var needleList = needle.ToList();

        var startIndex = haystack.FindSequenceIndex(needleList);
        if (startIndex == -1)
        {
            return false;
        }

        haystack.InsertRange(startIndex + needleList.Count + 1, injection);

        return true;
    }

    public static bool ReplaceSequence(
        this List<CodeInstruction> haystack,
        List<CodeInstruction> needle,
        IEnumerable<CodeInstruction> replacement)
    {
        var needleIndex = haystack.FindSequenceIndex(needle);
        if (needleIndex == -1)
        {
            return false;
        }

        haystack.RemoveRange(needleIndex, needle.Count);
        haystack.InsertRange(needleIndex, replacement);

        return true;
    }

    public static bool InjectBeforeCall(
        this List<CodeInstruction> haystack,
        MethodInfo needle,
        IEnumerable<CodeInstruction> injection)
    {
        var injectionIndex = FindIndexOfCall(haystack, needle);
        if (injectionIndex == -1)
        {
            return false;
        }

        haystack.InsertRange(injectionIndex, injection);

        return true;
    }

    public static bool InjectBeforeEveryCall(
        this List<CodeInstruction> haystack,
        MethodInfo needle,
        IEnumerable<CodeInstruction> injection)
    {
        var injectionList = injection.ToList();
        var found = false;
        var result = new List<CodeInstruction>();

        foreach (var i in haystack)
        {
            if (!i.Calls(needle))
            {
                result.Add(i);
                continue;
            }

            found = true;
            result.AddRange(injectionList);
            result.Add(i);
        }

        haystack.Clear();
        haystack.AddRange(result);

        return found;
    }

    public static bool InjectAfterCall(
        this List<CodeInstruction> haystack,
        MethodInfo needle,
        IEnumerable<CodeInstruction> injection)
    {
        var injectionIndex = FindIndexOfCall(haystack, needle);
        if (injectionIndex == -1)
        {
            return false;
        }

        haystack.InsertRange(injectionIndex + 1, injection);

        return true;
    }

    private static int FindSequenceIndex(
        this IEnumerable<CodeInstruction> codeInstructions,
        IEnumerable<CodeInstruction> sequenceToFind)
    {
        var needle = sequenceToFind.ToList();
        var haystack = codeInstructions.ToList();

        if (needle.IsEmpty() || haystack.IsEmpty())
        {
            return -1;
        }

        var needleFirstInstruction = needle.ElementAt(0);

        for (var i = 0; i < haystack.Count - needle.Count; i++)
        {
            var currentCodeInstruction = haystack[i];

            if (!OpCodesMatch(currentCodeInstruction.opcode, needleFirstInstruction.opcode) ||
                !OperandsMatch(currentCodeInstruction.operand, needleFirstInstruction.operand))
            {
                continue;
            }

            var isMatch = true;
            for (var j = 1; j < needle.Count; j++)
            {
                var offsetCodeInstruction = haystack[i + j];
                var currentSequenceElement = needle[j];

                if (OpCodesMatch(offsetCodeInstruction.opcode, currentSequenceElement.opcode) &&
                    OperandsMatch(offsetCodeInstruction.operand, currentSequenceElement.operand))
                {
                    continue;
                }

                isMatch = false;
                break;
            }

            if (isMatch)
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindIndexOfCall(this List<CodeInstruction> haystack, MethodInfo? needle)
    {
        if (haystack.IsEmpty() || needle == null)
        {
            return -1;
        }

        for (var i = 0; i < haystack.Count; i++)
        {
            if (haystack[i].Calls(needle))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool OpCodesMatch(OpCode a, OpCode b)
    {
        return a.Name == b.Name;
    }

    private static bool OperandsMatch(object? source, object? target)
    {
        // If both are null, we'll consider equal.
        if (source is null && target is null)
        {
            return true;
        }

        // If only source is null, they can't be equal.
        if (source == null)
        {
            return false;
        }

        // are objects equal?
        if (source.Equals(target))
        {
            return true;
        }

        // Hack for labels, cbf adding lookup for these.
        if (source is Label && target == null)
        {
            return true;
        }

        if (target == null)
        {
            return false;
        }

        if (source is MethodBase sourceMethod && target is MethodBase targetMethod)
        {
            return sourceMethod.Equals(targetMethod);
        }

        return false;
    }
}