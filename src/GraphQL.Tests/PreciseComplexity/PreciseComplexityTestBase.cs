using GraphQL.Validation.PreciseComplexity;
using GraphQL.Execution;
using GraphQL.Language.AST;
using System.Linq;

namespace GraphQL.Tests.PreciseComplexity
{


    public abstract class PreciseComplexityTestBase
    {
        protected ComplexityResult Analyze(
            string query,
            string variables = null,
            int defaultCollectionChildrenCount = 10,
            double? maxComplexity = null,
            int? maxDepth = null)
        {
            var schema = new PreciseComplexitySchema();
            schema.Initialize();
            var configuration = new PreciseComplexityConfiguration
                                    {
                                        DefaultCollectionChildrenCount =
                                            defaultCollectionChildrenCount,
                                        MaxComplexity = maxComplexity,
                                        MaxDepth = maxDepth
            };

            var documentBuilder = new GraphQLDocumentBuilder();
            var document = documentBuilder.Build(query);

           var operation = document.Operations.FirstOrDefault();

            Variables variablesObject = null;
            if (variables != null)
            {
                variablesObject = new DocumentExecuter().GetVariableValues(
                    document,
                    schema,
                    operation.Variables,
                    variables.ToInputs());
            }

            var analyzer = new PreciseComplexityAnalyser();
            return analyzer.Analyze(document, schema, configuration, variablesObject);
        }
    }
}
