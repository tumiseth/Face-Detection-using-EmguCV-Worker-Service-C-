using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.Structure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using System.Data.SqlClient;
using faceTimer.Model;

namespace faceTimer
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _config;
        private VideoCapture _videoCapture;
        private DateTime _firstFaceDetectionTime;
        private DateTime _lastFaceDetectionTime;


        public Worker(ILogger<Worker> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            string durationInHours = _config["DurationInHours"];
            
            #region DATBASE CONNECTION
            SqlConnection conn = new SqlConnection(
"Server=Tumi-pc;Database=FaceTimerDB; User ID=user; Password=12345;TrustServerCertificate=True;Encrypt=False;Trusted_Connection=True");

            conn.Open(); // opens the database connection
            String sql = "Select * from SittingRecord";
            SqlCommand selectCommand = new SqlCommand(sql, conn);
            //selectCommand.Connection.Open();
            SqlDataReader sqlReader;
            try
            {
                sqlReader = selectCommand.ExecuteReader();                                

                if (sqlReader.Read())
                {
                    SittingRecord record = new SittingRecord();
                    record.Id = sqlReader.GetInt32(0);
                    record.SittingDuration = sqlReader.GetInt32(1);                
                }
            }
            catch
            {
                Console.WriteLine("Error occurred while attempting SELECT.");
            }
            selectCommand.Connection.Close();
            #endregion




            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

                // Create a VideoCapture object to capture video frames
                using (_videoCapture = new VideoCapture())
                {
                    _videoCapture.Start();

                    // Capture a frame from the video stream
                    using (var frame = _videoCapture.QueryFrame())
                    {
                        // Save the captured frame as an image file
                        var imagePath = SaveFrame(frame);

                        // Check if a face is detected in the saved image
                        bool isFaceDetected = await Task.Run(() => IsFaceDetected(imagePath));

                        if (isFaceDetected)
                        {
                            // Update the last face detection time
                            _lastFaceDetectionTime = DateTime.Now;

                            // Update the first face detection time if it's not set
                            if (_firstFaceDetectionTime == default)
                            {
                                _firstFaceDetectionTime = _lastFaceDetectionTime;
                            }

                    // Toast Notification Logic
                    string message1 = "FaceTimer";
                    string message = "You've sat for too Long, take a walk!";
                    _logger.LogInformation(message);
                    _logger.LogInformation(message1);

                            new ToastContentBuilder()
                            .AddArgument("action", "view")
                            .AddArgument("conversationId", 9813)


                        // Buttons
                        .AddButton(new ToastButton()
                            .SetContent("Open")
                            .AddArgument("action", "Open")
                            .SetBackgroundActivation())

                        .AddButton(new ToastButton()
                            .SetContent("Dismiss")
                            .AddArgument("action", "Dismiss")
                            .SetBackgroundActivation())

                        // Logo
                        .AddAppLogoOverride(new Uri
                        ("C:\\Users\\user\\Downloads\\faceTimer\\faceTimer\\faceTimer" +
                        "\\faceTimer\\Notipics\\R.jpeg"),
                        ToastGenericAppLogoCrop.Circle)
                        .AddText(message1)
                        .AddText(message)

                        .Show();

                        }
                        else
                        {
                            // Log information when no face is detected
                            _logger.LogInformation("Face is Not Detected in the Image");
                            _logger.LogInformation("Ensure there is enough light around you.");
                        }
                    }

                    // Stop the video capture
                    _videoCapture.Stop();
                }

                // Delay for 15 seconds before capturing the next frame
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }

            // Calculate the duration between the first and last face detection times
            TimeSpan duration = _lastFaceDetectionTime - _firstFaceDetectionTime;
            string durationMessage = $"Time duration from first face" +
                $" detection to last face detection: {duration}";

            new ToastContentBuilder()
                .AddArgument("action", "view")
                .AddArgument("conversationId", 9813)
                .AddText(durationMessage)
                .Show();

            _logger.LogInformation(durationMessage);
        }



        private const int MaxStoredImages = 10; // Define the maximum number of stored image

        private static void CleanupOldImages(string storagePath)
        {
            var imageFiles = Directory.GetFiles(storagePath, "picture_*.jpg")
                .OrderByDescending(f => f)
                .Skip(MaxStoredImages)
                .ToList();

            foreach (var file in imageFiles)
            {
                File.Delete(file);
            }
        }

        private static string SaveFrame(Mat frame)
        {
            // Generate a timestamp for the image file name
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var fileName = $"picture_{timestamp}.jpg";

            // Specify the storage path for the image file
            var storagePath = @"C:\Users\user\Downloads\faceTimer\faceTimer\faceTimer\faceTimer\Pictures";

            // Combine the storage path and file name to get the full path
            var fullPath = Path.Combine(storagePath, fileName);

            // Save the frame as a JPEG image file
            frame.Save(fullPath);

            // Clean up old images if the number exceeds the maximum
            CleanupOldImages(storagePath);

            return fullPath;
        }

        // Check if a face is detected in the given image file
        private static bool IsFaceDetected(string imagePath)
        {
            // Load the image from the file
            using var image = new Image<Bgr, byte>(imagePath);
            // Convert the image to grayscale for face detection
            using var grayImage = image.Convert<Gray, byte>();
            using var faceCascade = new CascadeClassifier("haarcascade_frontalface_default.xml");
            // Detect faces in the grayscale image using a Haar cascade classifier
            var faces = faceCascade.DetectMultiScale(grayImage, 1.1, 10, Size.Empty);

            // Return true if at least one face is detected, otherwise false
            return faces.Length > 0;
        }
    }
}