using SendGrid;
public interface ISendGridFactory
{
  //  SqidsEncoder<long> CreateEncoder();
    SendGridClient CreateClient();
}
