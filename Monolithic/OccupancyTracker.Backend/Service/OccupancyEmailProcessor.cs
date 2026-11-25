using Microsoft.EntityFrameworkCore;
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
        private readonly IDbContextFactory<OccupancyContext> _contextFactory;
        private readonly ISendGridFactory _sendGridFactory;
        private PeriodicTimer _timer = new PeriodicTimer(new TimeSpan(0, 0, 30));

        /// <summary>
        /// Initializes a new instance of the <see cref="OccupancyEmailProcessor"/> class.
        /// </summary>
        /// <param name="contextFactory">The factory to create database context instances.</param>
        /// <param name="sendGridFactory">The factory to create SendGrid client instances.</param>
        public OccupancyEmailProcessor(IDbContextFactory<OccupancyContext> contextFactory, ISendGridFactory sendGridFactory)
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
                while (ProcessEmails(1, stoppingToken)) ;
            }
        }

        /// <summary>
        /// Processes the emails in the queue.
        /// </summary>
        /// <param name="emailProcessorPointerId">The ID of the email processor pointer.</param>
        /// <param name="stoppingToken">Token to signal the task to stop.</param>
        /// <returns>True if there are more emails to process; otherwise, false.</returns>
        private bool ProcessEmails(int emailProcessorPointerId, CancellationToken stoppingToken)
        {
            bool continueProcessing = true;
            while (!stoppingToken.IsCancellationRequested && continueProcessing)
            {
                using var context = _contextFactory.CreateDbContext();
                var emailProcessorPointer = context.EmailProcessorPointers.FirstOrDefault(x => x.EmailProcessorPointersId == emailProcessorPointerId);
                if (emailProcessorPointer == null)
                {
                    continueProcessing = false;
                    break;
                }

                var emailProcessorQueue = context.EmailProcessorQueue.FirstOrDefault(x => x.EmailProcessorQueueId > emailProcessorPointer.EmailProcessorQueueId);
                if (emailProcessorQueue == null || string.IsNullOrEmpty(emailProcessorQueue.EmailProcessorData))
                {
                    continueProcessing = false;
                    break;
                }

                var sendGridData = JsonSerializer.Deserialize<SendGridData>(emailProcessorQueue.EmailProcessorData);
                var response = _sendGridFactory.CreateClient().SendEmailAsync(sendGridData.GenerateSingleMessage()).Result;

                if (response == null)
                {
                    continueProcessing = false;
                    break;
                }

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
            }
            return continueProcessing;
        }
    }
}
