using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;

namespace MVCMediaShareAppNew.CustomAttributes
{
    public class RestrictAccessAttribute : Attribute, IAuthorizationFilter
    {
        private readonly string _message;

        public RestrictAccessAttribute(string message = "This API is temporarily disabled for maintenance.")
        {
            _message = message;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            context.Result = new ContentResult
            {
                Content = _message,
                StatusCode = 503
            };
        }
    }
}
