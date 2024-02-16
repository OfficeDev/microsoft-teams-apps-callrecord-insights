using Microsoft.Identity.Web;
using Microsoft.Kiota.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace CallRecordInsights.Extensions
{
    public static class GraphRequestBuilderAppOnlyExtensions
    {
        /// <summary>
        /// Specifies to use app only permissions for Graph.
        /// </summary>
        /// <param name="options">Options to modify.</param>
        /// <param name="appOnly">Should the permissions be app only or not.</param>
        /// <param name="tenant">Tenant ID or domain for which we want to make the call..</param>
        /// <returns></returns>
        public static IList<IRequestOption> AsAppForTenant(this IList<IRequestOption> options, string tenant = null)
        {
            var graphAuthenticationOptions = options.OfType<GraphAuthenticationOptions>().FirstOrDefault();
            if (graphAuthenticationOptions == null)
            {
                graphAuthenticationOptions = new GraphAuthenticationOptions();
                options.Add(graphAuthenticationOptions);
            }
            graphAuthenticationOptions.RequestAppToken = true;
            graphAuthenticationOptions.AcquireTokenOptions ??= new();

            graphAuthenticationOptions.AcquireTokenOptions.Tenant = tenant?.TryGetValidTenantIdGuid(out var tenantIdGuid) == true ? tenantIdGuid.ToString() : null;

            return options;
        }
    }
}
