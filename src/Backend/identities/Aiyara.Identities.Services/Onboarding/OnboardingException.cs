namespace Aiyara.Identities.Services.Onboarding;

public enum OnboardingFailure { InvalidInput, Conflict, InvalidInvitation, Unauthorized }

public sealed class OnboardingException(OnboardingFailure failure, string message) : Exception(message)
{
    public OnboardingFailure Failure { get; } = failure;
}
