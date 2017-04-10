using GraphQL.Language.AST;
using GraphQL.Types;
using System.Collections.Generic;
using System;

namespace GraphQL.Validation.PreciseComplexity
{
    public class PreciseComplexityContext
    {
        public PreciseComplexityConfiguration Configuration { get; set; }

        public Document Document { get; set; }

        public ISchema Schema { get; set; }

        public Variables Variables { get; set; }

        public TypeInfo TypeInfo { get; set; }

        public ComplexityResult Result { get; set; }

        public Stack<List<Func<ComplexityResult>>> Stack { get; set; }

        public Dictionary<string, Func<ComplexityResult>> FragmentsComplexity { get; set; }
    }
}
