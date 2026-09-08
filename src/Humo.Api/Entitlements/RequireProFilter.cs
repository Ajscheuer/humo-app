using Humo.Api.Auth;

namespace Humo.Api.Entitlements;

/// <summary>
/// Refuses a request from an account that is not Pro.
/// <para>
/// The mechanism <c>product-spec.md</c> §5.2 means by "entitlements are checked
/// server-side": a Pro-gated endpoint carries this and therefore cannot be
/// reached by editing a client. Slice 7's analytics endpoints are the first to
/// use it; it exists here because the entitlement it reads is built here, and
/// building the gate alongside the thing it reads is what stops the first gated
/// endpoint inventing its own check.
/// </para>
/// </summary>
public sealed class RequireProFilter : IEndpointFilter
{
    private readonly IAccountResolver _accounts;
    private readonly IEntitlementService _entitlements;

    public RequireProFilter(IAccountResolver accounts, IEntitlementService entitlements)
    {
        _accounts = accounts;
        _entitlements = entitlements;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (_accounts.Resolve(context.HttpContext.User) is not { } accountId)
        {
            return Results.Unauthorized();
        }

        var entitlement = await _entitlements
            .GetAsync(accountId, context.HttpContext.RequestAborted)
            .ConfigureAwait(false);

        if (!entitlement.IsPro)
        {
            // 402 rather than 403: the caller is who they say they are and the
            // request is well formed. What is missing is a subscription, and the
            // client shows a paywall rather than an error.
            return Results.Json(
                new { error = "This requires Humo Pro.", level = entitlement.Level.ToString() },
                statusCode: StatusCodes.Status402PaymentRequired);
        }

        return await next(context).ConfigureAwait(false);
    }
}

public static class RequireProExtensions
{
    /// <summary>Gates an endpoint behind a Pro entitlement, checked server-side.</summary>
    public static TBuilder RequirePro<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        // Authorization first: "not signed in" and "signed in but not Pro" are
        // different answers, and the filter can only tell them apart if the
        // principal has already been established.
        builder.RequireAuthorization();
        builder.AddEndpointFilter<TBuilder, RequireProFilter>();
        return builder;
    }
}
