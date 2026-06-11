using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class BindingAnalyzer
{
    private const int DefaultBindingTraversalNodeLimit = 512;
    private const int DefaultBindingResultLimit = 200;
    private const int DefaultLiveBindingTraversalNodeLimit = 4096;
    private const int DefaultLiveBindingErrorLimit = 200;

    private sealed class BindingScanBudget
    {
        private readonly string _traversalLimitReason;
        private readonly string _resultLimitReason;
        private readonly HashSet<string> _reasons = new(StringComparer.Ordinal);

        public BindingScanBudget(
            int maxTraversalNodes,
            int maxResults,
            string traversalLimitReason,
            string resultLimitReason)
        {
            MaxTraversalNodes = maxTraversalNodes;
            MaxResults = maxResults;
            _traversalLimitReason = traversalLimitReason;
            _resultLimitReason = resultLimitReason;
        }

        public int MaxTraversalNodes { get; }

        public int MaxResults { get; }

        public int TraversalNodeCount { get; private set; }

        public int TotalResultCount { get; private set; }

        public int ReturnedResultCount { get; private set; }

        public bool Truncated => _reasons.Count > 0;

        public bool ResultLimitReached => ReturnedResultCount >= MaxResults;

        public IReadOnlyList<string> Reasons => _reasons.ToArray();

        public bool TryTakeTraversalNode()
        {
            if (TraversalNodeCount >= MaxTraversalNodes)
            {
                _reasons.Add(_traversalLimitReason);
                return false;
            }

            TraversalNodeCount++;
            return true;
        }

        public bool TryTakeResult()
        {
            TotalResultCount++;
            if (ReturnedResultCount >= MaxResults)
            {
                _reasons.Add(_resultLimitReason);
                return false;
            }

            ReturnedResultCount++;
            return true;
        }

        public void MarkTraversalTruncated() => _reasons.Add(_traversalLimitReason);

        public void MarkResultTruncated() => _reasons.Add(_resultLimitReason);

        public object ToContract(int returnedResultCount) => new
        {
            reasons = Reasons,
            maxTraversalNodes = MaxTraversalNodes,
            traversalNodeCount = TraversalNodeCount,
            maxResults = MaxResults,
            totalResultCount = TotalResultCount,
            returnedResultCount
        };
    }

    private static bool TryValidatePositiveLimit(int? value, string argumentName, out object? error)
    {
        if (value is > 0 or null)
        {
            error = null;
            return true;
        }

        error = ToolErrorFactory.InvalidArgument(
            $"{argumentName} must be a positive integer when provided",
            $"Provide {argumentName} > 0 or omit it to use the default budget.");
        return false;
    }

    private static int ResolveLimit(int? requested, int defaultValue) => requested ?? defaultValue;
}
