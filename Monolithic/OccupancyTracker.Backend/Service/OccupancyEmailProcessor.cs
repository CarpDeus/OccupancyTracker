using OccupancyTracker.IRepository;
using OccupancyTracker.Models;
using SendGrid.Helpers.Mail;
using System.Text.Json;
using System.Timers;

namespace OccupancyTracker.Service
{
    /// <summary>
    /// Background service for processing occupancy-related emails.
    /// </summary>
    public class OccupancyEmailProcessor : BackgroundService
    {
        private readonly IRepositorySessionFactory _contextFactory;
        private readonly ISendGridFactory _sendGridFactory;
        private PeriodicTimer _timer = new PeriodicTimer(new TimeSpan(0, 0, 30));

        /// <summary>
        /// Initializes a new instance of the <see cref="OccupancyEmailProcessor"/> class.
        /// </summary>
        /// <param name="contextFactory">The factory to create repository sessions.</param>
        /// <param name="sendGridFactory">The factory to create SendGrid client instances.</param>
        public OccupancyEmailProcessor(IRepositorySessionFactory contextFactory, ISendGridFactory sendGridFactory)
        {
            _contextFactory = contextFactory;
            _sendGridFactory = sendGridFactory;
        }

        /// <summary>
        /// Executes the background email processing task.
        /// </summary>
        /// <param name="stoppingToken">Token to signal the task to stop.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (await _timer.WaitForNextTickAsync(stoppingToken)
                && !stoppingToken.IsCancellationRequested)
            {
                while (await ProcessEmailsAsync(1, stoppingToken)) ;
            }
        }

        /// <summary>
        /// Processes the emails in the queue.
        /// </summary>
        /// <param name="emailProcessorPointerId">The ID of the email processor pointer.</param>
        /// <param name="stoppingToken">Token to signal the task to stop.</param>
        /// <returns>True if there are more emails to process; otherwise, false.</returns>
        private async Task<bool> ProcessEmailsAsync(int emailProcessorPointerId, CancellationToken stoppingToken)
        {
            bool continueProcessing = true;
            while (!stoppingToken.IsCancellationRequested && continueProcessing)
            {
                using var context = _contextFactory.Create();
                var emailProcessorPointer = await context.EmailProcessorPointers.FirstOrDefaultAsync(x => x.EmailProcessorPointersId == emailProcessorPointerId, stoppingToken);
                if (emailProcessorPointer == null)
                {
                    continueProcessing = false;
                    break;
                }

                var emailProcessorQueue = await context.EmailProcessorQueue.FirstOrDefaultAsync(x => x.EmailProcessorQueueId > emailProcessorPointer.EmailProcessorQueueId, stoppingToken);
                if (emailProcessorQueue == null || string.IsNullOrEmpty(emailProcessorQueue.EmailProcessorData))
                {
                    continueProcessing = false;
                    break;
                }

                var sendGridData = JsonSerializer.Deserialize<SendGridData>(emailProcessorQueue.EmailProcessorData);
                var response = await _sendGridFactory.CreateClient().SendEmailAsync(sendGridData.GenerateSingleMessage(), stoppingToken);

                if (response == null)
                {
                    continueProcessing = false;
                    break;
                }

                emailProcessorPointer.EmailProcessorQueueId = emailProcessorQueue.EmailProcessorQueueId;
                context.EmailProcessorPointers.Update(emailProcessorPointer);
                await context.SaveChangesAsync(stoppingToken);

                var emailProcessorHistory = new EmailProcessorHistory
                {
                    EmailProcessorQueueId = emailProcessorQueue.EmailProcessorQueueId,
                    EmailProcessorPointersId = emailProcessorPointerId,
                    CurrentStatus = response.IsSuccessStatusCode ? (short)0 : (short)-1,
                    CreatedDate = DateTime.Now,
                    OtherInformation = response.IsSuccessStatusCode ? "Email sent" : $"{response.StatusCode}: {response.Body}"
                };

                context.EmailProcessorHistory.Add(emailProcessorHistory);
                await context.SaveChangesAsync(stoppingToken);
            }
            return continueProcessing;
        }
    }
}
