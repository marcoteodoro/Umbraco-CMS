using System;
using System.Web;

namespace Umbraco.Web
{
    public static class HttpContextAccessorExtensions
    {
        public static HttpContextBase GetRequiredHttpContext(this IHttpContextAccessor httpContextAccessor)
        {
            var httpContext = httpContextAccessor.HttpContext;

            if(httpContext is null) throw new InvalidOperationException("HttpContext is null");

            return httpContext;
        }
    }
}
