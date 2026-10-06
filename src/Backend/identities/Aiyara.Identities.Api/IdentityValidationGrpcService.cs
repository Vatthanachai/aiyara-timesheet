using Aiyara.Timesheet.Contracts.Identity.V1;
using Grpc.Core;

internal sealed class IdentityValidationGrpcService
    : IdentityValidationService.IdentityValidationServiceBase
{
    public override Task<ValidateAccessTokenResponse> ValidateAccessToken(
        ValidateAccessTokenRequest request, ServerCallContext context)
        => Task.FromResult(new ValidateAccessTokenResponse
        {
            IsValid = false,
            FailureReason = "token_validation_not_available"
        });

    public override Task<LookupProfileResponse> LookupProfile(
        LookupProfileRequest request, ServerCallContext context)
        => Task.FromResult(new LookupProfileResponse { Found = false });
}
