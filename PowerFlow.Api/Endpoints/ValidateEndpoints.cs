using PowerFlow.Api.Dtos;
using PowerFlow.Api.Mapping;
using PowerFlow.Core.Validation;

namespace PowerFlow.Api.Endpoints;

internal static class ValidateEndpoints
{
    internal static RouteGroupBuilder MapValidateEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost(
            "validate",
            (NetworkDto request) =>
            {
                // PowerNetwork constructor throws on duplicate bus IDs — catch it
                // and surface it as a validation error rather than a 500.
                PowerFlow.Core.Models.PowerNetwork network;
                try
                {
                    network = request.ToNetwork();
                }
                catch (ArgumentException ex)
                {
                    var constructionError = new ValidationErrorDto(
                        "DUPLICATE_BUS_ID",
                        ex.Message,
                        "Error"
                    );
                    return Results.Ok(new ValidationResultDto(false, [constructionError]));
                }

                return Results.Ok(NetworkValidator.Validate(network).ToDto());
            }
        );

        return group;
    }
}
