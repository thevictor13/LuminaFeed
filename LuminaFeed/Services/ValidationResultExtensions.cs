using ErrorOr;
using FluentValidation.Results;

namespace LuminaFeed.Services;

/// <summary>Bridges FluentValidation results into the <see cref="ErrorOr"/> error model used by services.</summary>
internal static class ValidationResultExtensions
{
    /// <summary>One <see cref="ErrorType.Validation"/> error per failure, coded by property name.</summary>
    public static List<Error> ToErrors(this ValidationResult result) =>
        result.Errors.ConvertAll(failure => Error.Validation(failure.PropertyName, failure.ErrorMessage));
}
