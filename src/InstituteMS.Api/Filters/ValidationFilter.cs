using System.Diagnostics;
using FluentValidation;
using InstituteMS.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace InstituteMS.Api.Filters;

/// <summary>
/// Runs the FluentValidation validator registered for each action argument and
/// short-circuits with the standard envelope on failure, so validation errors
/// look exactly like every other API error (MIGRATION_PLAN §3.1).
/// </summary>
public class ValidationFilter : IAsyncActionFilter
{
    private readonly IServiceProvider _services;

    public ValidationFilter(IServiceProvider services) => _services = services;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new List<string>();

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (_services.GetService(validatorType) is not IValidator validator) continue;

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            if (!result.IsValid)
                errors.AddRange(result.Errors.Select(e => e.ErrorMessage));
        }

        if (errors.Count > 0)
        {
            var body = ApiResponse<object?>.Fail("Please correct the highlighted fields.", errors);
            body.TraceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

            context.Result = new BadRequestObjectResult(body);
            return;
        }

        await next();
    }
}
