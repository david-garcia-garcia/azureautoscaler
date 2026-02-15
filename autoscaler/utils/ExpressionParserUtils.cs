using System.Linq.Dynamic.Core;
using System.Linq.Expressions;

namespace poolautoscaler.utils
{
    /// <summary>Parses scaling expressions into delegates.</summary>
    public static class ExpressionParserUtils
    {
        /// <summary>Compiles an expression string to a delegate.</summary>
        /// <param name="expression">The expression string.</param>
        /// <param name="parameter">The parameter name used in the expression.</param>
        /// <param name="parameterType">The type of the parameter.</param>
        /// <param name="returnType">The return type of the delegate.</param>
        /// <param name="expectedParameterCount">Expected number of parameters.</param>
        /// <returns>Compiled delegate.</returns>
        public static Delegate ParseExpression(
            string expression,
            string parameter,
            Type parameterType,
            Type returnType,
            int expectedParameterCount)
        {
            // Hay que parsear la consulta...
            var parsingConfig = new ParsingConfig();
            parsingConfig.CustomTypeProvider = new MyCustomTypeProvider();
            parsingConfig.AllowNewToEvaluateAnyType = false;
            parsingConfig.IsCaseSensitive = true;
            parsingConfig.AllowEqualsAndToStringMethodsOnObject = true;

            // parsingConfig.ExpressionPromoter = new CustomExpressionPromoter();
            parsingConfig.DisableMemberAccessToIndexAccessorFallback = true;

            var p0 = Expression.Parameter(parameterType, parameter);

            LambdaExpression parsedExpression;

            try
            {
                parsedExpression = DynamicExpressionParser.ParseLambda(
                    parsingConfig,
                    new ParameterExpression[] { p0 },
                    returnType,
                    expression);
            }
            catch (System.Linq.Dynamic.Core.Exceptions.ParseException parseException)
            {
                throw new Exception($"({parseException.Message}) La consulta no se ha podido interpretar: " + expression, parseException);
            }

            if (parsedExpression.Parameters.Count != expectedParameterCount)
            {
                throw new Exception("Expression must have one and only one parameter.");
            }

            if (parsedExpression.ReturnType != returnType)
            {
                throw new Exception("Return type must be of type boolean");
            }

            return parsedExpression.Compile();
        }
    }
}
