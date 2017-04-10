using System;
using System.Linq;

using GraphQL.Language.AST;
using GraphQL.Types;

using System.Collections.Generic;

namespace GraphQL.Validation.PreciseComplexity
{
    public class PreciseComplexityAnalyser
    {
        public delegate double GetComplexity(
            PreciseComplexityContext context,
            Func<string, object> getArgumentValue,
            double childComplexity);

        public ComplexityResult Analyze(
            Document document,
            ISchema schema,
            PreciseComplexityConfiguration configuration,
            Variables variables)
        {
            var context = new PreciseComplexityContext
                              {
                                  Schema = schema,
                                  Configuration = configuration,
                                  Document = document,
                                  Stack = new Stack<List<Func<ComplexityResult>>>(),
                                  FragmentsComplexity = new Dictionary<string, Func<ComplexityResult>>(),
                                  TypeInfo = new TypeInfo(schema),
                                  Variables = variables
                              };


            var complexityVisitor = new EnterLeaveListener(l => this.Listen(l, context));
            var visitors = new INodeVisitor[] { context.TypeInfo, complexityVisitor };
            var basic = new BasicVisitor(visitors);
            basic.Visit(document);
            return context.Result;
        }

        private void Listen(EnterLeaveListener listener, PreciseComplexityContext context)
        {
            listener.Match<Document>(
                d =>
                    {
                        context.Stack.Push(new List<Func<ComplexityResult>>());
                    },
                d =>
                    {
                        context.Result = SumComplexity(context.Stack.Pop());
                    });

            listener.Match<Field>(
                f => context.Stack.Push(new List<Func<ComplexityResult>>()),
                f =>
                    {
                        var childrenComplexity = context.Stack.Pop();
                        var fieldDefinition = context.TypeInfo.GetFieldDef();
                        if (fieldDefinition.GetComplexity == null)
                        {
                            if (fieldDefinition.ResolvedType is ListGraphType)
                            {
                                fieldDefinition.GetComplexity =
                                    (complexityContext, getArgument, complexity) 
                                        => 1d + (context.Configuration.DefaultCollectionChildrenCount * complexity);
                            }
                            else
                            {
                                fieldDefinition.GetComplexity =
                                    (complexityContext, getArgument, complexity) 
                                        => 1d + complexity;
                            }
                        }

                        Func<string, object> getArgumentFunc = argument => GetArgumentValue(argument, fieldDefinition, f, context.Variables);

                        Func<ComplexityResult> calculator = () =>
                            {
                                var calculatedChildrenComplexity = SumComplexity(childrenComplexity);
                                var myComplexity = fieldDefinition.GetComplexity(
                                    context,
                                    getArgumentFunc,
                                    calculatedChildrenComplexity.Complexity);

                                if (context.Configuration.MaxComplexity.HasValue
                                    && myComplexity > context.Configuration.MaxComplexity.Value)
                                {
                                    throw new InvalidOperationException("Query is too complex to execute.");
                                }

                                var myDepth = calculatedChildrenComplexity.MaxDepth + 1;
                                if (context.Configuration.MaxDepth.HasValue
                                    && myDepth > context.Configuration.MaxDepth.Value)
                                {
                                    throw new InvalidOperationException("Query is too nested to execute. ");
                                }

                                return new ComplexityResult(myComplexity, myDepth);
                            };

                        context.Stack.Peek().Add(calculator);
                    });

            listener.Match<FragmentSpread>(
                f => { },
                f =>
                    {
                        context.Stack.Peek().Add(
                            () =>
                                {
                                    Func<ComplexityResult> calculator;
                                    if (!context.FragmentsComplexity.TryGetValue(f.Name, out calculator))
                                    {
                                        throw new InvalidOperationException($"Fragment with name {f.Name} was not found");
                                    }

                                    // todo: possible stack overflow exception on circular referenced fragments
                                    return calculator();
                                });
                    });

            listener.Match<FragmentDefinition>(
                f => context.Stack.Push(new List<Func<ComplexityResult>>()),
                f =>
                    {
                        var children = context.Stack.Pop();
                        context.FragmentsComplexity[f.Name] = () => SumComplexity(children);
                    });
        }

        private static ComplexityResult SumComplexity(
            IEnumerable<Func<ComplexityResult>> childrenComplexity)
        {
            return childrenComplexity.Select(c => c()).Aggregate(
                new ComplexityResult(0d, 0),
                (s, r) => new ComplexityResult(
                    s.Complexity + r.Complexity,
                    Math.Max(s.MaxDepth, r.MaxDepth)));
        }

        private static object GetArgumentValue(
            string argumentName,
            FieldType fieldType,
            Field field,
            Variables variables)
        {
            if (fieldType.Arguments == null || !fieldType.Arguments.Any())
            {
                return null;
            }

            var arg = fieldType.Arguments.FirstOrDefault(a => a.Name == argumentName);
            if (arg == null)
            {
                return null;
            }

            var value = field.Arguments.ValueFor(argumentName);
            var type = arg.ResolvedType;

            var coercedValue = TypeInfo.CoerceValue(type, value, variables);
            return coercedValue ?? arg.DefaultValue;
        }
    }
}
