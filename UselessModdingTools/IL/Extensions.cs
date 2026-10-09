namespace UselessModdingTools.IL;

internal static class Extensions
{
    extension(Params pram)
    {
        public Params Reduce(int amount)
        {
            return Set(pram, pram.RemainingParams - amount);
        }

        public Params Set(int newAmount)
        {
            if (newAmount < 0)
                throw new InvalidOperationException($"Cannot set a negative amount".AsErrorMsg());

            if (newAmount == 0)
                return new(pram.Indent, 0, false, false, -1, pram.SourceInstruction);
            else
                return pram with { RemainingParams = newAmount };
        }
    }
}
