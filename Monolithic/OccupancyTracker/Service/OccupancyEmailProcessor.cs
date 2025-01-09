using Microsoft.EntityFrameworkCore;
using OccupancyTracker.Models;
using SendGrid.Helpers.Mail;
using System.Text.Json;
using System.Timers;

namespace OccupancyTracker.Service
{
    public class OccupancyEmailProcessor : IDisposable
    {
        private readonly IDbContextFactory<OccupancyContext> _contextFactory;
        private readonly ISendGridFactory _sendGridFactory;
        private System.Timers.Timer _timer;
        private bool _running;

        public OccupancyEmailProcessor(IDbContextFactory<OccupancyContext> contextFactory, ISendGridFactory sendGridFactory)
        {
            _contextFactory = contextFactory;
            _sendGridFactory = sendGridFactory;
        }

        public void StartExecuting()
        {
            if (!_running)
            {
                while (ProcessEmails(1)) ;
                _timer = new System.Timers.Timer(60_000); // every 1 min
                _timer.Elapsed += HandleTimer;
                _timer.AutoReset = true;
                _timer.Enabled = true;
                _running = true;
            }
        }

        private void HandleTimer(object source, ElapsedEventArgs e)
        {
            while (ProcessEmails(1)) ;
        }

        private bool ProcessEmails(int emailProcessorPointerId)
        {
            using var context = _contextFactory.CreateDbContext();
            var emailProcessorPointer = context.EmailProcessorPointers.FirstOrDefault(x => x.EmailProcessorPointersId == emailProcessorPointerId);
            if (emailProcessorPointer == null) return false;

            var emailProcessorQueue = context.EmailProcessorQueue.FirstOrDefault(x => x.EmailProcessorQueueId > emailProcessorPointer.EmailProcessorQueueId);
            if (emailProcessorQueue == null || string.IsNullOrEmpty(emailProcessorQueue.EmailProcessorData)) return false;

            var sendGridData = JsonSerializer.Deserialize<SendGridData>(emailProcessorQueue.EmailProcessorData);
            var response = _sendGridFactory.CreateClient().SendEmailAsync(sendGridData.GenerateSingleMessage()).Result;

            if (response == null) return false;

            emailProcessorPointer.EmailProcessorQueueId = emailProcessorQueue.EmailProcessorQueueId;
            context.EmailProcessorPointers.Update(emailProcessorPointer);
            context.SaveChanges();

            var emailProcessorHistory = new EmailProcessorHistory
            {
                EmailProcessorQueueId = emailProcessorQueue.EmailProcessorQueueId,
                EmailProcessorPointersId = emailProcessorPointerId,
                CurrentStatus = response.IsSuccessStatusCode ? (short)0 : (short)-1,
                CreatedDate = DateTime.Now,
                OtherInformation = response.IsSuccessStatusCode ? "Email sent" : $"{response.StatusCode}: {response.Body}"
            };

            context.EmailProcessorHistory.Add(emailProcessorHistory);
            context.SaveChanges();

            return true;
        }

        public void Dispose()
        {
            if (_running)
            {
                _running = false;
                _timer.Stop();
                _timer.Dispose();
            }
        }
    }
}
