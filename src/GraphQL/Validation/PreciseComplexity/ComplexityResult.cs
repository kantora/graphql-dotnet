namespace GraphQL.Validation.PreciseComplexity
{
    public class ComplexityResult
    {
        /// <inheritdoc />
        public ComplexityResult(double complexity, int maxDepth)
        {
            this.Complexity = complexity;
            this.MaxDepth = maxDepth;
        }

        public double Complexity { get; set; }

        public int MaxDepth { get; set; }
    }
}