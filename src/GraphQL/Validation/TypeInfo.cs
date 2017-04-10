using System.Collections.Generic;
using System.Linq;
using GraphQL.Introspection;
using GraphQL.Language.AST;
using GraphQL.Types;

namespace GraphQL.Validation
{
    using System;

    public class TypeInfo : INodeVisitor
    {
        private readonly ISchema _schema;
        private readonly Stack<IGraphType> _typeStack = new Stack<IGraphType>();
        private readonly Stack<IGraphType> _inputTypeStack = new Stack<IGraphType>();
        private readonly Stack<IGraphType> _parentTypeStack = new Stack<IGraphType>();
        private readonly Stack<FieldType> _fieldDefStack = new Stack<FieldType>();
        private readonly Stack<INode> _ancestorStack = new Stack<INode>();
        private DirectiveGraphType _directive;
        private QueryArgument _argument;

        public TypeInfo(ISchema schema)
        {
            _schema = schema;
        }

        public INode[] GetAncestors()
        {
            return _ancestorStack.Select(x => x).Skip(1).Reverse().ToArray();
        }

        public IGraphType GetLastType()
        {
            return _typeStack.Any() ? _typeStack.Peek() : null;
        }

        public IGraphType GetInputType()
        {
            return _inputTypeStack.Any() ? _inputTypeStack.Peek() : null;
        }

        public IGraphType GetParentType()
        {
            return _parentTypeStack.Any() ? _parentTypeStack.Peek() : null;
        }

        public FieldType GetFieldDef()
        {
            return _fieldDefStack.Any() ? _fieldDefStack.Peek() : null;
        }

        public DirectiveGraphType GetDirective()
        {
            return _directive;
        }

        public QueryArgument GetArgument()
        {
            return _argument;
        }

        public T GetFieldArgumentValue<T>(Variables variables, string argumentName, T defaultValue = default(T))
        {
            var fieldType = this.GetFieldDef();
            var field = this._ancestorStack.Peek() as Field;
            
            if (fieldType == null || field == null)
            {
                return defaultValue;
            }

            if (fieldType.Arguments == null || !fieldType.Arguments.Any())
            {
                return defaultValue;
            }

            var arg = fieldType.Arguments.FirstOrDefault(a => a.Name == argumentName);
            if (arg == null)
            {
                return defaultValue;
            }

            var value = field.Arguments.ValueFor(argumentName);
            var type = arg.ResolvedType;

            var resolvedValue = CoerceValue(type, value, variables);
            return resolvedValue != null ? (T)resolvedValue : defaultValue;
        }

        public void Enter(INode node)
        {
            _ancestorStack.Push(node);

            if (node is SelectionSet)
            {
                _parentTypeStack.Push(GetLastType());
                return;
            }

            if (node is Field)
            {
                var field = (Field) node;
                var parentType = _parentTypeStack.Peek().GetNamedType();
                var fieldType = GetFieldDef(_schema, parentType, field);
                _fieldDefStack.Push(fieldType);
                var targetType = fieldType?.ResolvedType;
                _typeStack.Push(targetType);
                return;
            }

            if (node is Directive)
            {
                var directive = (Directive) node;
                _directive = _schema.Directives.SingleOrDefault(x => x.Name == directive.Name);
            }

            if (node is Operation)
            {
                var op = (Operation) node;
                IGraphType type = null;
                if (op.OperationType == OperationType.Query)
                {
                    type = _schema.Query;
                }
                else if (op.OperationType == OperationType.Mutation)
                {
                    type = _schema.Mutation;
                }
                else if (op.OperationType == OperationType.Subscription)
                {
                    type = _schema.Subscription;
                }
                _typeStack.Push(type);
                return;
            }

            if (node is FragmentDefinition)
            {
                var def = (FragmentDefinition) node;
                var type = _schema.FindType(def.Type.Name);
                _typeStack.Push(type);
                return;
            }

            if (node is InlineFragment)
            {
                var def = (InlineFragment) node;
                var type = def.Type != null ? _schema.FindType(def.Type.Name) : GetLastType();
                _typeStack.Push(type);
                return;
            }

            if (node is VariableDefinition)
            {
                var varDef = (VariableDefinition) node;
                var inputType = varDef.Type.GraphTypeFromType(_schema);
                _inputTypeStack.Push(inputType);
                return;
            }

            if (node is Argument)
            {
                var argAst = (Argument) node;
                QueryArgument argDef = null;
                IGraphType argType = null;

                var args = GetDirective() != null ? GetDirective()?.Arguments : GetFieldDef()?.Arguments;

                if (args != null)
                {
                    argDef = args.Find(argAst.Name);
                    argType = argDef?.ResolvedType;
                }

                _argument = argDef;
                _inputTypeStack.Push(argType);
            }

            if (node is ListValue)
            {
                var type = GetInputType().GetNamedType();
                _inputTypeStack.Push(type);
            }

            if (node is ObjectField)
            {
                var objectType = GetInputType().GetNamedType();
                IGraphType fieldType = null;

                if (objectType is InputObjectGraphType)
                {
                    var complexType = objectType as IComplexGraphType;
                    var inputField = complexType.Fields.FirstOrDefault(x => x.Name == ((ObjectField) node).Name);
                    fieldType = inputField?.ResolvedType;
                }

                _inputTypeStack.Push(fieldType);
            }
        }

        public void Leave(INode node)
        {
            _ancestorStack.Pop();

            if (node is SelectionSet)
            {
                _parentTypeStack.Pop();
                return;
            }

            if (node is Field)
            {
                _fieldDefStack.Pop();
                _typeStack.Pop();
                return;
            }

            if (node is Directive)
            {
                _directive = null;
                return;
            }

            if (node is Operation
                || node is FragmentDefinition
                || node is InlineFragment)
            {
                _typeStack.Pop();
                return;
            }

            if (node is VariableDefinition)
            {
                _inputTypeStack.Pop();
                return;
            }

            if (node is Argument)
            {
                _argument = null;
                _inputTypeStack.Pop();
                return;
            }

            if (node is ListValue || node is ObjectField)
            {
                _inputTypeStack.Pop();
                return;
            }
        }

        private FieldType GetFieldDef(ISchema schema, IGraphType parentType, Field field)
        {
            var name = field.Name;

            if (name == SchemaIntrospection.SchemaMeta.Name
                && Equals(schema.Query, parentType))
            {
                return SchemaIntrospection.SchemaMeta;
            }

            if (name == SchemaIntrospection.TypeMeta.Name
                && Equals(schema.Query, parentType))
            {
                return SchemaIntrospection.TypeMeta;
            }

            if (name == SchemaIntrospection.TypeNameMeta.Name && parentType.IsCompositeType())
            {
                return SchemaIntrospection.TypeNameMeta;
            }

            if (parentType is IObjectGraphType || parentType is IInterfaceGraphType)
            {
                var complexType = parentType as IComplexGraphType;
                return complexType.Fields.FirstOrDefault(x => x.Name == field.Name);
            }

            return null;
        }

        // todo: remove duplicate code from document executor
        public static object CoerceValue(IGraphType type, IValue input, Variables variables = null)
        {
            if (type is NonNullGraphType)
            {
                var nonNull = type as NonNullGraphType;
                return CoerceValue(nonNull.ResolvedType, input, variables);
            }

            if (input == null)
            {
                return null;
            }

            var variable = input as VariableReference;
            if (variable != null)
            {
                return variables != null
                           ? variables.ValueFor(variable.Name)
                           : null;
            }

            if (type is ListGraphType)
            {
                var listType = type as ListGraphType;
                var listItemType = listType.ResolvedType;
                var list = input as ListValue;
                return list != null
                           ? list.Values.Map(item => CoerceValue(listItemType, item, variables)).ToArray()
                           : new[] { CoerceValue(listItemType, input, variables) };
            }

            if (type is IObjectGraphType || type is InputObjectGraphType)
            {
                var complexType = type as IComplexGraphType;
                var obj = new Dictionary<string, object>();

                var objectValue = input as ObjectValue;
                if (objectValue == null)
                {
                    return null;
                }

                complexType.Fields.Apply(field =>
                    {
                        var objectField = objectValue.Field(field.Name);
                        if (objectField != null)
                        {
                            var fieldValue = CoerceValue(field.ResolvedType, objectField.Value, variables);
                            fieldValue = fieldValue ?? field.DefaultValue;

                            obj[field.Name] = fieldValue;
                        }
                    });

                return obj;
            }

            if (type is ScalarGraphType)
            {
                var scalarType = type as ScalarGraphType;
                return scalarType.ParseLiteral(input);
            }

            return null;
        }
    }
}
