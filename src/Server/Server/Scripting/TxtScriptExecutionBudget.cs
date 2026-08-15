namespace Server.Scripting
{
    public sealed class TxtScriptExecutionBudget
    {
        public int MaximumSteps { get; }
        public int ConsumedSteps { get; private set; }

        public TxtScriptExecutionBudget(int maximumSteps)
        {
            MaximumSteps = Math.Max(1, maximumSteps);
        }

        public bool TryConsume()
        {
            if (ConsumedSteps >= MaximumSteps) return false;
            ConsumedSteps++;
            return true;
        }
    }
}
