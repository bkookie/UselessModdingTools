using System.Reflection;
using System.Text;

namespace UselessModdingTools.IL;

//internal readonly record struct StackEntry(int DepthAfter, Instruction Instruction);

internal readonly record struct Item(int Depth, int Pos, int Indent, string Text, bool IsBranchTarget, bool IsUnconditionalBranchWithStack);
internal readonly record struct Params(int Indent, int RemainingParams); // RemainingParams is dual purpose; when negative, instead is the mapkey of a branchTarget channel
#warning Indent property seems redundant. It is identical to its index in the pramsToConsume list

public static class ILStackVisualizer
{
    public static string ReadMethod(MethodInfo baseMethod, VisualizerMode mode)
    {
        if (baseMethod.DeclaringType?.Namespace?.StartsWith("Godot") == true)
            return "";

        string debugMethod = "UselessModdingTools.IL.BasicILScanner.GetOperand";

        if ($"{baseMethod.DeclaringType}.{baseMethod.Name}".StartsWith(debugMethod))
            ;

        if (!BasicILScanner.TryReadIL(baseMethod, out List<Instruction>? instructions, out Dictionary<int, TargetInfo>? branchTargets))
            return $"Error: Could not read method '{baseMethod.Name}'.\n";

        //Ignore branching for now
        if (!instructions.Select(i => i.OpCode).Any(op => BasicILScanner.IsBranch(op)))
            return "";

        //if (instructions.Count > 10)
        //    return "";

        //List<StackEntry> entries = [];
        //int depth = 0;
        int maxDepth = instructions.Max(i => i.Depth);

        //for (int i = 0; i < instructions.Count; i++)
        //{
        //    Instruction instruction = instructions[i];

        //    int depthBefore = depth;

        //    if (branchTargets.TryGetValue(instruction.Position, out int branchDepth))
        //    {
        //        //int pos = instruction.Position;
        //        //int max = instructions.Where((instr, i) => instr.Branch.HasValue && (instr.Branch.Value.Position == pos || (instr.Branch.Value.SwitchTargets?.Contains(pos) ?? false))).Max(instr => instr.Depth);
        //        //depth += max;
        //        depth += branchDepth;
        //    }
        //    else
        //    {
        //    }
        //    depth += BasicILScanner.NetStackDepth(baseMethod, instruction);

        //    entries.Add(new(instruction.Depth, instruction));
        //    if (depth > maxDepth)
        //        maxDepth = depth;
        //}

        StringBuilder sb = new();
        string methodName = $"{baseMethod.DeclaringType}.{baseMethod.Name}";
        string sig = baseMethod.ToString()!;
        int len = Math.Max(methodName.Length, sig.Length);
        sb.Append("┌─").Append(new string('─', len)).AppendLine("─┐");
        sb.Append("│ ").Append(methodName).Append(new string(' ', len - methodName.Length + 1)).AppendLine("│");
        sb.Append("├─").Append(new string('─', len)).AppendLine("─┤"); ;
        sb.Append("│ ").Append(sig).Append(new string(' ', len - sig.Length + 1)).AppendLine("│");
        sb.Append("└─").Append(new string('─', len)).AppendLine("─┘"); ;

        if (mode == VisualizerMode.StackDepth)
        {
            //for (int i = 0; i < entries.Count; i++)
            //{
            //    StackEntry entry = entries[i];
            //    depth = entry.Depth;
            //    sb.AppendLine($"[{depth}]{depth.Tab(entries, i - 1)}{InstructionToString(baseMethod, entry.Instruction)}");
            //}
        }
        else
        {
            if (methodName.StartsWith(debugMethod))
                ;

            int maxIndent = 0;
            int channelId = 0; // Provides a unique id for channel mapping
            List<Params> pramsToConsume = [];
            List<Item> items = [];
            List<int> dupPipingIndexes = []; // used for opcodes that push multiple values (eg dup)
            Dictionary<int, int> branchMaps = []; // Key = TargetPos, Value = ChannelId
            for (int i = instructions.Count - 1; i >= 0; i--)
            {
                Params? topStack = pramsToConsume.LastOrDefault();

                //StackEntry entry = entries[i];
                Instruction instruction = instructions[i];
                int pos = instruction.Position;
                object? operand = BasicILScanner.GetOperand(baseMethod, instruction);
                MethodBase? calledMethod = operand as MethodBase;
                int pushed = BasicILScanner.PushDepth(instruction.OpCode, baseMethod, calledMethod);
                int popped = BasicILScanner.PopDepth(instruction.OpCode, baseMethod, calledMethod);

                int indent = pramsToConsume.Count;
                int remainingPrams = topStack.HasValue ? topStack.Value.RemainingParams : -1;
                bool nonContributor = false;

                bool isBranch = false;
                int branchChannel = 0;
                int branchChannelIndexToRemove = -1;
                if (BasicILScanner.IsBranch(instruction.OpCode) && instruction.Branch != null)
                {
                    // Need to route the pipes to the target
                    //pushed = instruction.SwitchTargets?.Length ?? (instruction.Target.HasValue ? 1 : 0);

                    int targetPos = instruction.Branch.Value.TargetInfo.Target;

                    if (instruction.Branch.Value.SwitchTargets != null)
                    {
                        // Find the left-most channel this inline switch will link with
                        for (int n = 0; n < pramsToConsume.Count - 1; n++)
                        {
                            Params pram = pramsToConsume[n];
                            foreach (TargetInfo inf in instruction.Branch.Value)
                            {
                                if (pram.RemainingParams == branchMaps[inf.Target])
                                {
                                    targetPos = inf.Target;
                                    n = pramsToConsume.Count; // break for;
                                    break;
                                }
                            }
                        }
                    }

                    if (branchTargets.TryGetValue(targetPos, out TargetInfo? info))
                    {
                        isBranch = true;
                        branchChannel = GetChannel(branchMaps, targetPos, ref channelId);
                        int numBranchPathsToThisTarget;

                        if (instruction.Branch.Value.SwitchTargets != null)
                        {
                            numBranchPathsToThisTarget = instruction.Branch.Value.SwitchTargets.Count(b => b.Target == targetPos); // InlineSwitch may have multiple branches for the same target
                        }
                        else
                        {
                            numBranchPathsToThisTarget = 1;
                        }

                        info.Count -= numBranchPathsToThisTarget;

                        if (targetPos < pos)
                        {
                            info.BackBranches += numBranchPathsToThisTarget;

                            int branchChannelIndex = pramsToConsume.FindLastIndex(p => p.RemainingParams < 0) + 1;
                            pramsToConsume.Insert(branchChannelIndex, new(branchChannelIndex + 1, branchChannel));

                            if (branchChannelIndex == indent)
                                indent++;
                        }
                        else if (info.Count == 0)
                        {
                            PrepareToRemoveChannel(pramsToConsume, branchChannel, out branchChannelIndexToRemove);
                        }
                    }
                }

                static void PrepareToRemoveChannel(List<Params> pramsToConsume, int channel, out int channelIndexToRemove)
                {
                    channelIndexToRemove = pramsToConsume.FindLastIndex(p => p.RemainingParams == channel);
                }

                int reach = 0;
                if (pushed > 0 && pramsToConsume.Count > 0
                    && (i == pramsToConsume.Count - 1 || !BasicILScanner.IsUnconditionalNonPoppingBranch(instructions[i + 1].OpCode))) // Cannot push onto unconditional branch because they do not pop (aside from inline switches)
                {
                    int lastIndex = pramsToConsume.Count - 1;
                    for (int j = 0; j < pushed; j++)
                    {
                        int k = pramsToConsume.FindLastIndex(lastIndex, p => p.RemainingParams > 0);
                        if (k >= 0)
                        {
                            if (pushed == 1)
                                reach = lastIndex - k; // How many indent levels the pushed value needs to stretch across (usually 0)

                            if (j > 0)
                            {
                                for (int l = k; l <= lastIndex; l++)
                                {
                                    dupPipingIndexes.Add(l);
                                }
                                lastIndex = k;
                            }
                            Params tempPrams = pramsToConsume[k];
                            pramsToConsume[k] = tempPrams with { RemainingParams = tempPrams.RemainingParams - 1 };
                        }
                    }
                }
                else if (pramsToConsume.Count > 0 && pramsToConsume.LastOrDefault().Indent == pramsToConsume.Count)
                {
                    // Not one of the params (not directly anyway). Ends this block
                    nonContributor = true;
                }

                bool isBranchTarget = false;
                int branchTargetChannelIndex = -1;
                int branchTargetChannelIndexToRemove = -1;
                bool insertChannel = false; // if false, an empty channel was found
                Params? newBranchParams = null;
                if (branchTargets.TryGetValue(pos, out TargetInfo? targetInfo))
                {
                    isBranchTarget = true;
                    int branchTargetChannel = GetChannel(branchMaps, pos, ref channelId); // channels are negative

                    if (targetInfo.Count <= 0)
                    {
                        // Hit by a backwards branch, and there are no forward branches
                        PrepareToRemoveChannel(pramsToConsume, branchTargetChannel, out branchTargetChannelIndexToRemove);
                    }
                    else
                    {
                        branchTargetChannelIndex = pramsToConsume.FindLastIndex(p => p.RemainingParams == branchTargetChannel);

                        if (branchTargetChannelIndex != -1)
                        {
                            //throw new InvalidOperationException("This target already mapped???");

                            // This may be hit if an instruction is targeted by both forward and back branches
#warning Unlikely to be handled properly

                            targetInfo.Count--;
                            if (targetInfo.Count == 0)
                            {
                                newBranchParams = pramsToConsume[branchTargetChannelIndex] with { RemainingParams = 0 };
                            }
                        }
                        else
                        {
                            branchTargetChannelIndex = pramsToConsume.FindLastIndex(p => p.RemainingParams == 0);

                            if (branchTargetChannelIndex != -1)
                            {
                                newBranchParams = pramsToConsume[branchTargetChannelIndex] with { Indent = branchTargetChannelIndex + 1, RemainingParams = branchTargetChannel };
                            }
                            else
                            {
                                insertChannel = true;
                                branchTargetChannelIndex = pramsToConsume.FindLastIndex(p => p.RemainingParams < 0) + 1;
                                newBranchParams = new Params(branchTargetChannelIndex + 1, branchTargetChannel); // with Indent = indent?
                            }

                            if (branchTargetChannelIndex == indent)
                                indent++;
                        }
                    }
                }

                StringBuilder sb2 = new();
                sb2.Append("    ");
                PrintPipes(sb2, pos, pramsToConsume, dupPipingIndexes, branchMaps, nonContributor, instruction.Branch, isBranch, branchChannel, indent, isUnconditionalBranchWithStack: false, reach, isBranchTarget: false, null);
                sb2.Append(InstructionToString(baseMethod, instruction));
                int extraIndent = indent - pramsToConsume.Count;

                static void PrintPipes(StringBuilder sb2, int pos, List<Params> pramsToConsume, List<int> dupPipingIndexes, Dictionary<int, int> branchMaps, bool nonContributor, Branch? branchInfo, bool isBranch, int branchChannel, int branchIndent, bool isUnconditionalBranchWithStack, int reach, bool isBranchTarget, TargetInfo? branchTargetInfo)
                {
                    Dictionary<int, TargetInfo>? map = null;
                    if (branchInfo.HasValue)
                    {
                        map = [];
                        foreach (TargetInfo info in branchInfo.Value)
                        {
                            // only used for switches?
                            map[branchMaps[info.Target]] = info;
                        }
                        //map = branchInfo.Value.ToDictionary(data => branchMaps[data.Target], data => data);
                    }

                    bool linkedWithChannel = false;
                    bool reached = false;
                    for (int j = 0; j < pramsToConsume.Count; j++)
                    {
                        Params prams = pramsToConsume[j];
                        if (prams.Indent == j + 1)
                        {
                            if (j == pramsToConsume.Count - 1 && !nonContributor)
                            {
                                if (prams.RemainingParams == 0)
                                {
                                    if (dupPipingIndexes.Contains(j))
                                    {
                                        sb2.Append("──┬─");
                                    }
                                    else
                                    {
                                        sb2.Append("  ┌─");
                                    }
                                }
                                else
                                {
                                    if (dupPipingIndexes.Contains(j))
                                    {
                                        sb2.Append("──┼─"); // Impossible?
                                    }
                                    else if (prams.RemainingParams > 0)
                                    {
                                        sb2.Append("  ├─");
                                    }
                                    else if (isBranchTarget && branchMaps.TryGetValue(pos, out int channel) && channel == prams.RemainingParams)
                                    {
                                        sb2.Append("└───");
                                    }
                                    else if (reached && prams.RemainingParams < 0)
                                    {
                                        sb2.Append("│───");
                                    }
                                    else
                                    {
                                        sb2.Append("────");
                                    }
                                }
                            }
                            else if (dupPipingIndexes.Contains(j))
                            {
                                if (prams.RemainingParams == 0)
                                {
                                    if (dupPipingIndexes.Contains(j - 1))
                                    {
                                        sb2.Append("────");
                                    }
                                    else
                                    {
                                        sb2.Append("  ┌─");
                                    }
                                }
                                else
                                {
                                    sb2.Append("  ├─");
                                }
                            }
                            else if (isBranch && branchChannel == prams.RemainingParams)
                            {
                                if (branchInfo == null)
                                    throw new InvalidOperationException($"{nameof(branchInfo)} should not be null");

                                linkedWithChannel = true;
                                if (map != null && map.TryGetValue(prams.RemainingParams, out TargetInfo? targetInfo))
                                {
                                    if (targetInfo.Target < branchInfo.Value.Position)
                                        sb2.Append("└───");
                                    else if (targetInfo.Count == 0)
                                        sb2.Append("┌───");
                                    else
                                        sb2.Append("├───");
                                }
                                else if (branchInfo.Value.TargetInfo.Target < branchInfo.Value.Position)
                                    sb2.Append("└───");
                                else if (branchInfo.Value.TargetInfo.Count == 0)
                                    sb2.Append("┌───");
                                else
                                    sb2.Append("├───");
                            }
                            else if (prams.RemainingParams == 0)
                            {
                                if (linkedWithChannel && (isBranch || isBranchTarget))
                                    sb2.Append("────");
                                else
                                    sb2.Append("    ");
                            }
                            else if (prams.RemainingParams > 0)
                            {
                                if (isBranch && linkedWithChannel)
                                {
                                    sb2.Append("──│─");
                                }
                                else if (isBranchTarget)
                                {
                                    if (branchIndent == prams.Indent)
                                        sb2.Append("─┐│");
                                    else if (linkedWithChannel)
                                        sb2.Append("──│─");
                                    else
                                        sb2.Append("  │ ");
                                }
                                else if (reach > 0 && prams.Indent == pramsToConsume.Count - reach)
                                {
                                    reached = true;
                                    sb2.Append("  ├─");
                                }
                                else
                                {
                                    sb2.Append("  │ ");
                                }
                            }
                            else if (linkedWithChannel && (isBranch || isBranchTarget))
                            {
                                if (map != null && map.TryGetValue(prams.RemainingParams, out TargetInfo? targetInfo))
                                {
                                    // This should only trigger on inlineswitch
                                    // Only the left-most channel will have been processed; do the remainder here
                                    if (branchInfo != null && branchInfo.Value.SwitchTargets != null)
                                    {
                                        targetInfo.Count -= branchInfo.Value.SwitchTargets.Count(b => b.Target == targetInfo.Target); // InlineSwitch may have multiple branches for the same target
                                    }
                                    else
                                    {
                                        targetInfo.Count--;
                                    }

                                    if (targetInfo.Count == 0)
                                    {
                                        pramsToConsume[j] = pramsToConsume[j] with { RemainingParams = 0 };
                                        sb2.Append("┬───");
                                    }
                                    else
                                    {
                                        sb2.Append("┼───");
                                    }
                                }
                                else
                                {
                                    sb2.Append("│───");
                                }
                            }
                            else if (isBranchTarget && branchMaps.TryGetValue(pos, out int channel) && channel == prams.RemainingParams)
                            {
                                linkedWithChannel = true;
                                if (branchTargetInfo != null && branchTargetInfo.BackBranches > 0)
                                {
                                    if (branchTargetInfo.Count == 0)
                                        sb2.Append("┌───");
                                    else
                                        sb2.Append("├───");
                                }
                                else
                                    sb2.Append("└───");
                            }
                            else if (reached && prams.RemainingParams < 0)
                            {
                                sb2.Append("│───");
                            }
                            else
                            {
                                sb2.Append("│   ");
                            }
                        }
                        else
                        {
                            sb2.Append("    ");
                        }
                    }

                    // For ret in particular. Need to indent the instruction when it is sitting drectly in the channel of the incoming branch pipe (the pipe needs room to do a little dog leg)
                    int remainingBranchTargetIndent = branchIndent - pramsToConsume.Count;
                    for (int i = 0; i < remainingBranchTargetIndent; i++)
                    {
                        if (isBranch || isBranchTarget)
                        {
                            // No more channels on the stack, so need to manually pipe across to the label
                            if (isBranchTarget && i == remainingBranchTargetIndent - 1)
                            {
                                if (isUnconditionalBranchWithStack)
                                    sb2.Append("─┐│");
                                else
                                    sb2.Append("─┐");
                            }
                            else
                            {
                                sb2.Append("────");
                            }
                        }
                        else if (isUnconditionalBranchWithStack && i == remainingBranchTargetIndent - 1)
                        {
                            sb2.Append('│');
                        }
                        else
                        {
                            sb2.Append("    ");
                        }
                    }
                }
                dupPipingIndexes.Clear();

                if (popped > 0 && !instruction.StackReset && !instruction.IsExceptionHandlerLead) // The instruction leading an exeption handler consumes a value pushed by the CLR externally to the method (dont try to trace it)
                {
                    pramsToConsume.Add(new(pramsToConsume.Count + 1 + extraIndent, popped));
                    if (pramsToConsume.Count > maxIndent)
                        maxIndent++;
                }
                items.Add(new(instruction.Depth, instruction.Position, indent, sb2.ToString(), false, false));

                if (isBranch && branchChannelIndexToRemove >= 0)
                {
                    pramsToConsume[branchChannelIndexToRemove] = pramsToConsume[branchChannelIndexToRemove] with { RemainingParams = 0 };
                }

                bool isUnconditionalBranchWithStack = BasicILScanner.IsUnconditionalNonPoppingBranch(instruction.OpCode) && instruction.Depth > 0;
                if (isBranchTarget || isUnconditionalBranchWithStack)
                {
                    if (newBranchParams != null && branchTargetChannelIndex >= 0)
                    {
                        if (insertChannel)
                            pramsToConsume.Insert(branchTargetChannelIndex, newBranchParams.Value);
                        else
                            pramsToConsume[branchTargetChannelIndex] = newBranchParams.Value;
                    }
                    StringBuilder sb3 = new();
                    sb3.Append("    ");
                    PrintPipes(sb3, pos, pramsToConsume, dupPipingIndexes, branchMaps, true, null, isBranch: false, branchChannel: 0, indent + 1, isUnconditionalBranchWithStack, reach: 0, isBranchTarget, targetInfo);
                    //sb3.Append("─┐");
                    items.Add(new(0, 0, branchTargetChannelIndex + 1, sb3.ToString(), isBranchTarget, isUnconditionalBranchWithStack)); // Add this after the main entry

                    if (branchTargetChannelIndexToRemove >= 0)
                    {
                        pramsToConsume[branchTargetChannelIndexToRemove] = pramsToConsume[branchTargetChannelIndexToRemove] with { RemainingParams = 0 };
                    }
                }

                while (pramsToConsume.Count > 0 && pramsToConsume.LastOrDefault().RemainingParams == 0)
                {
                    // Must be done last to preserve indentation
                    pramsToConsume.RemoveLast();
                }

                static int GetChannel(Dictionary<int, int> map, int targetPos, ref int channelId)
                {
                    if (map.TryGetValue(targetPos, out int channel))
                        return channel;

                    map[targetPos] = --channelId; // Do not store zero here
                    return channelId;
                }
            }

            int maxDigits = Math.Max(0, maxDepth.ToString().Length);
            //int maxPosDigits = entries.Count > 0 ? Math.Max(0, entries[^1].Instruction.Position.ToString().Length) : 0;

            for (int i = items.Count - 1; i >= 0; i--)
            {
                var (dep, pos, indent, str, isBranchTarget, isUnconditionalBranchWithStack) = items[i];

                if (isBranchTarget || isUnconditionalBranchWithStack)
                {
                    sb.AppendLine($"{new string(' ', 2 + maxDigits + 1 + 2 + 4)}{str}");
                }
                else
                {
                    string depfth = dep.ToString().PadLeft(maxDigits);
                    sb.AppendLine($"[{depfth}] <{pos.ToString(),4}>{str}");
                }
            }
        }

        return sb.ToString();
    }

    private static string InstructionToString(MethodInfo baseMethod, Instruction instruction)
    {
        return $"{instruction.OpCode}{instruction.Text}{FormatOperand(BasicILScanner.GetOperand(baseMethod, instruction))}";
    }

    private static string? FormatOperand(object? operand)
    {
        if (operand == null)
            return null;

        if (operand is string)
            return $" \"{operand}\"".ReplaceLineEndings("\\n");

        if (operand is MethodBase method)
            return $" ---> {method}";

        if (operand is Branch branchTarget)
        {
            return $" >> {branchTarget}";
        }

        return $" {operand}";
    }
}

public enum VisualizerMode
{
    StackDepth,
    Parameters,
}

file static class Extensions
{
    extension(int depth)
    {
        public string Tab()
        {
            return new string(' ', (depth + 1) * 4);
        }

        public string Tab(List<Instruction> entries, int prevIndex)
        {
            StringBuilder sb = new();
            sb.Append("    ");

            for (int i = 0; i < depth; i++)
            {
                if (i == depth - 1 && prevIndex > 0)
                    sb.Append((entries[prevIndex].Depth < depth) ? "├───" : "│   ");
                else
                    sb.Append("│   ");
            }

            return sb.ToString();
        }
    }

    extension<T>(List<T> list)
    {
        public T Last => list[^1];

        public T RemoveLast()
        {
            T item = list[^1];
            list.RemoveAt(list.Count - 1);
            return item;
        }
    }
}
