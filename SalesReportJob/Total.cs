using System;
using System.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mail;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using static System.Runtime.InteropServices.JavaScript.JSType;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace SalesReportJob
{
    public class Total
    {
        private readonly ILogger _logger;

        public Total(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<Total>();
        }

        [Function("Total")]
        public async Task Run([TimerTrigger("* 0 0 * * *"
#if DEBUG
            , RunOnStartup=true
#endif
            )]TimerInfo myTimer)
        {
            _logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            
            if (myTimer.ScheduleStatus is not null)
            {
                _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}");
                // Get connection string of the sales SQL database
                var connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
                var yesterday = DateTime.UtcNow.Date.AddDays(-1); // For the previous day's sales

                // SQL Queries

                // Get total sales of the previous day
                string queryTotalSales = @"
            SELECT SUM(Amount) AS TotalSales
            FROM Sales
            WHERE SaleDate >= @StartDate AND SaleDate < @EndDate";

                string insertReport = @"
            INSERT INTO DailyReports (ReportDate, TotalSales)
            VALUES (@ReportDate, @TotalSales)";

                try
                {
                    // Initiate the database connection
                    await using (var connection = new SqlConnection(connectionString))
                    {
                        await connection.OpenAsync();

                        // Calculate total sales for the previous day
                        await using (var cmdTotalSales = new SqlCommand(queryTotalSales, connection))
                        {
                            cmdTotalSales.Parameters.AddWithValue("@StartDate", yesterday);
                            cmdTotalSales.Parameters.AddWithValue("@EndDate", yesterday.AddDays(1));

                            var result = await cmdTotalSales.ExecuteScalarAsync();
                            decimal totalSales = result != null && result != DBNull.Value ? (decimal)result : 0;

                            // Insert the total into the DailyReports table
                            await using (var cmdInsertReport = new SqlCommand(insertReport, connection))
                            {
                                cmdInsertReport.Parameters.AddWithValue("@ReportDate", yesterday);
                                cmdInsertReport.Parameters.AddWithValue("@TotalSales", totalSales);

                                await cmdInsertReport.ExecuteNonQueryAsync();
                            }
                            //send notification email
                            await sendEmail(totalSales, yesterday);

                        }
                    }
                    _logger.LogInformation("Total saved successfully!");

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.Message);
                }
            }
        }

        public async Task sendEmail (decimal total, DateTime date)
        {
            try
            {
                string toEmail = "add_your_email";
                string subject = "Daily sales report";
                string template = "<h1>Sales Report for: {0:yyyy-MM-dd}</h1><p><b>Total:</b> {1:C}</p>";
                string message = string.Format(template, date, total);

                var apiKey = Environment.GetEnvironmentVariable("SENDGIRD_API_KEY");
                var client = new SendGridClient(apiKey);
                var from = new EmailAddress("add_sendgird_verified_email", "Sales Report");
                var to = new EmailAddress(toEmail);
                var msg = MailHelper.CreateSingleEmail(from, to, subject, message, message);
                await client.SendEmailAsync(msg);
            } catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }
        }
    }
}
