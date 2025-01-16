using SendGrid;
using SendGrid.Helpers.Mail;
namespace OccupancyTracker.Data.Models
{
    public class SendGridData
    {
        public string FromEmailAddress { get; set; }
        public string FromName { get; set; }
        public string Subject { get; set; }
        public string ToEmailAddress { get; set; }
        public string ToName { get; set; }
        public string PlainTextContent { get; set; }
        public string HtmlContent { get; set; }


        public SendGridMessage GenerateSingleMessage()
        {
            return MailHelper.CreateSingleEmail(new EmailAddress(FromEmailAddress), new EmailAddress(ToEmailAddress), Subject, PlainTextContent, HtmlContent);
        }
    }
}
