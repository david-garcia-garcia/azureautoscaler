using System.Linq.Dynamic.Core.CustomTypeProviders;
using poolautoscaler.metrics.Dto;
using poolautoscaler.strategies.Dto;

namespace poolautoscaler.utils
{
    /// <summary>Exposes metric DTO types to Dynamic LINQ.</summary>
    public class MyCustomTypeProvider : DefaultDynamicLinqCustomTypeProvider
    {
        /// <summary>Returns types allowed in expressions.</summary>
        /// <returns>Set of types allowed in Dynamic LINQ expressions.</returns>
        public override HashSet<Type> GetCustomTypes() =>
            new[]
            {
                typeof(MetricEvalDto),
                typeof(MetricEvalDtoResultValue),
                typeof(CustomMetricDataContext),
                typeof(CustomMetricHelpers),
                typeof(VmssMetricView),
                typeof(VmssSkuView),
            }.ToHashSet();
    }
}
