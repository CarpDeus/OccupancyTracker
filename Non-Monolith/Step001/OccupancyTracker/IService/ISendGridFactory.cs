using SendGrid;
/// <summary>
/// Factory for creating SendGridClient
/// </summary>
public interface ISendGridFactory
{
    /// <summary>
    /// Create a SendGridClient
    /// </summary>
    /// <returns>SendGridClient</returns>
    SendGridClient CreateClient();
}
