namespace Shink.Services;

/// <summary>Enrols consenting new mobile free accounts in the shared welcome series.</summary>
public sealed class MobileSignupWelcomeEmailService(
    ISubscriptionLedgerService ledger,
    IGratisSubscriberEmailSequenceService emails,
    ILogger<MobileSignupWelcomeEmailService> logger)
{
    public async Task TryStartAsync(
        string email,
        bool marketingConsent,
        bool isNewGratisAccount,
        string? firstName,
        string? lastName,
        CancellationToken cancellationToken = default)
    {
        if (!marketingConsent || !isNewGratisAccount)
        {
            return;
        }

        try
        {
            if (await ledger.HasActivePaidSubscriptionAsync(email, cancellationToken))
            {
                return;
            }

            var result = await emails.TryStartAsync(email, firstName, lastName, cancellationToken);
            if (result == GratisSubscriberSequenceStartResult.Failed)
            {
                logger.LogWarning("Mobile welcome email enrollment failed; authentication remains successful.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Mobile welcome email enrollment failed; authentication remains successful.");
        }
    }
}
