using System;
using System.Threading;
using System.IO;
using System.Collections;
using System.Text;
using CortexAccess;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace MotionLogger
{
    class Program
    {
        const string OutFilePath = @"MotionLogger.csv";
        const string licenseID = ""; // Do not need license id when subscribe motion
        const int maxGyroDPS = 500; // Max_Gyro = 500 dps
        const int maxAcc = 4; // Max_Acc  =  4 g
        const int maxMag = 4; // Max_Mag  = 4 gauss
        const int noiseThreshold = 20;
        const int baselineThreshold = 1000;

        private static GyrometerType currentGyroType = GyrometerType.Unknown;
        private static RollingSums rollingSums;
        private static int gyroXIndex = -1;
        private static int gyroYIndex = -1;
        private static int gyroZIndex = -1;
        private static int gyroWIndex = -1;

        private static FileStream OutFileStream;

        // Reference: https://emotiv.gitbook.io/epoc-user-manual/introduction-1/technical_specifications
        public enum GyrometerType
        {
            G, // +- 8g: Epoc v1.0. Uses GYROX, GYROY.
            DPS, // +- 500 d/s: Epoc+ v1.1. Uses GYROX, GYROY, GYROZ.
            Quaternion, // 4D Quaternions: Epoc X, Epoc+ v1.1A. Uses Q0, Q1, Q2, Q3.
            Unknown, // Default case of unknown Gyrometer
        }

        public struct RollingSums
        {
            public double x, y, z;
        }
        public struct Quaternion
        {
            public double w, x, y, z;
        };

        public struct EulerAngles
        {
            public double roll, pitch, yaw;
        };

        static void Main(string[] args)
        {
            Console.WriteLine("Motion LOGGER");
            Console.WriteLine("Please wear Headset with good signal!!!");

            // Delete Output file if existed
            if (File.Exists(OutFilePath))
            {
                File.Delete(OutFilePath);
            }
            OutFileStream = new FileStream(OutFilePath, FileMode.Append, FileAccess.Write);
            rollingSums = rollingSums = new RollingSums
            {
                x = 0,
                y = 0,
                z = 0
            };
            currentGyroType = GyrometerType.Unknown;

            DataStreamExample dse = new DataStreamExample();
            dse.AddStreams("mot");
            dse.OnSubscribed += SubscribedOK;
            dse.OnMotionDataReceived += OnMotionDataReceived;
            dse.Start(licenseID);

            Console.WriteLine("Press Esc to flush data to file and exit");
            while (Console.ReadKey().Key != ConsoleKey.Escape) { }

            // Unsubcribe stream
            dse.UnSubscribe();
            Thread.Sleep(5000);

            // Close Session
            dse.CloseSession();
            Thread.Sleep(5000);
            // Close Out Stream
            OutFileStream.Dispose();
        }

        private static void SubscribedOK(object sender, Dictionary<string, JArray> e)
        {
            foreach (string key in e.Keys)
            {
                if (key == "mot")
                {
                    // print header
                    ArrayList header = e[key].ToObject<ArrayList>();

                    //add timeStamp to header
                    header.Insert(0, "Timestamp");
                    header.Add("Action1");
                    header.Add("Action2");
                    header.Add("Action3");
                    header.Add("Action4");
                    WriteDataToFile(header);

                    // Determine what type of gyrometer we have available
                    if (header.Contains("GYROX") && header.Contains("GYROY") && header.Contains("GYROZ"))
                    {
                        Console.WriteLine("Detected Epoc+ v1.1 model headset, which uses dps.");
                        currentGyroType = GyrometerType.DPS;
                        gyroXIndex = header.IndexOf("GYROX");
                        gyroYIndex = header.IndexOf("GYROY");
                        gyroZIndex = header.IndexOf("GYROZ");
                    } else if (header.Contains("GYROX") && header.Contains("GYROY")) {
                        Console.WriteLine("Detected Epoc v1.0 model headset, which uses g.");
                        currentGyroType = GyrometerType.G;
                        gyroXIndex = header.IndexOf("GYROX");
                        gyroYIndex = header.IndexOf("GYROY");
                    } else if (header.Contains("Q0") && header.Contains("Q1") && header.Contains("Q2") && header.Contains("Q3"))
                    {
                        Console.WriteLine("Detected Epoc X or Epoc+ v1.1A model headset, which uses quaternions.");
                        currentGyroType = GyrometerType.Quaternion;
                        gyroXIndex = header.IndexOf("Q0");
                        gyroYIndex = header.IndexOf("Q1");
                        gyroZIndex = header.IndexOf("Q2");
                        gyroWIndex = header.IndexOf("Q3");
                    }
                    else
                    {
                        Console.WriteLine("This is a type of headset we've not yet seen before. See the contained headers: " + header.ToString());
                        currentGyroType = GyrometerType.Unknown;
                    }
                }
            }
        }

        // Write Header and Data to File
        private static void WriteDataToFile(ArrayList data)
        {
            int i = 0;
            for (; i < data.Count - 1; i++)
            {
                byte[] val = Encoding.UTF8.GetBytes(data[i].ToString() + ", ");

                if (OutFileStream != null)
                    OutFileStream.Write(val, 0, val.Length);
                else
                    break;
            }
            // Last element
            byte[] lastVal = Encoding.UTF8.GetBytes(data[i].ToString() + "\n");
            if (OutFileStream != null)
                OutFileStream.Write(lastVal, 0, lastVal.Length);
        }

        private static void OnMotionDataReceived(object sender, ArrayList motData)
        {
            
            switch (currentGyroType)
            {
                case GyrometerType.DPS:
                    float rawGyroX = (float) motData[gyroXIndex];
                    float rawGyroY = (float) motData[gyroYIndex];
                    float rawGyroZ = (float) motData[gyroZIndex];

                    float convertedX = ConvertDegreesPerSecond(rawGyroX);
                    float convertedY = ConvertDegreesPerSecond(rawGyroY);
                    float convertedZ = ConvertDegreesPerSecond(rawGyroZ);

                    float filteredX = Math.Abs(convertedX) < noiseThreshold ? 0 : convertedX;
                    float filteredY = Math.Abs(convertedY) < noiseThreshold ? 0 : convertedY;
                    float filteredZ = Math.Abs(convertedZ) < noiseThreshold ? 0 : convertedZ;

                    rollingSums.x += filteredX;
                    rollingSums.y += filteredY;
                    rollingSums.z += filteredZ;

                    double xAxisActionValue = Math.Abs(rollingSums.x) < baselineThreshold ? 0 : rollingSums.x;
                    double yAxisActionValue = Math.Abs(rollingSums.y) < baselineThreshold ? 0 : rollingSums.y;
                    double zAxisActionValue = Math.Abs(rollingSums.z) < baselineThreshold ? 0 : rollingSums.z;

                    // For testing, let's just have this be an on/off. We can figure out scaling later, but for now let's use a fixed movement speed when this action is on

                    // yAxisActionValue is negative when the user is looking down (Action1).
                    float action1Value = yAxisActionValue >= 0 ? 0f : 1f;

                    // yAxisActionValue is positive when the user is looking up (Action2)
                    float action2Value = yAxisActionValue < 0 ? 0f : 1f;

                    // zAxisActionValue is negative when the user is tilting their head left (Action3)
                    float action3Value = zAxisActionValue >= 0 ? 0f : 1f;

                    // zActionActionValue is positive when the user is tilting their head right (Action4)
                    float action4Value = zAxisActionValue < 0 ? 0f : 1f;

                    Console.WriteLine($"Action 1: {action1Value}. Action 2: {action2Value}. Action 3: {action3Value}. Action 4: {action4Value}.");

                    motData.Add(action1Value);
                    motData.Add(action2Value);
                    motData.Add(action3Value);
                    motData.Add(action4Value);

                    break;
                case GyrometerType.G:
                case GyrometerType.Quaternion:
                case GyrometerType.Unknown:
                    Console.WriteLine($"Processing for {currentGyroType.ToString()} is not supported [ yet :) ]");
                    break;
                default: // *Should* never reach this case
                    Console.WriteLine($"Undefined Gyrometer Type");
                    break;
            }

            WriteDataToFile(motData);

        }

        // Convert G to a digestible form (Untested, provided by Emotiv)
        // Acc (g)    = (X * 4 * Max_Acc *2 ) / 2^16 - Max_Acc
        private static float ConvertG(float g)
        {
            return (g * 4 * maxAcc * 2) / (float)Math.Pow(2, 16) - maxAcc;
        }

        // Convert Degrees Per Second from ADC scaled to a digestible form (physical DPS)
        // Gyro (dps) = (X * 4 * Max_Gyro *2 ) / 2^16 - Max_Gyro
        private static float ConvertDegreesPerSecond(float dps)
        {
            return (dps * 4 * maxGyroDPS * 2) / (float)Math.Pow(2, 16) - maxGyroDPS;
        }

        // Convert Quaternion to Euler Angles (Untested, provided by Emotiv)
        // Reference: https://en.wikipedia.org/wiki/Conversion_between_quaternions_and_Euler_angles
        private static EulerAngles ConvertQuaternions(Quaternion q)
        {
            // roll (x-axis rotation) variables
            double sinr_cosp = 2 * (q.w * q.x + q.y * q.z);
            double cosr_cosp = 1 - 2 * (Math.Pow(q.x, 2) + Math.Pow(q.y, 2));

            // pitch (y-axis rotation)
            double sinp = 2 * (q.w * q.y - q.z * q.x);

            // yaw (z-axis rotation)
            double siny_cosp = 2 * (q.w * q.z + q.x * q.y);
            double cosy_cosp = 1 - 2 * (Math.Pow(q.y, 2) + Math.Pow(q.z, 2));

            return new EulerAngles
            {
                roll = Math.Atan2(sinr_cosp, cosr_cosp),
                pitch = Math.Abs(sinp) >= 1 ? CopySign(Math.PI / 2, sinp) : Math.Asin(sinp),
                yaw = Math.Atan2(siny_cosp, cosy_cosp)
            }
            ;
        }

        // Convert gauss to a digestible form (Untested, provided by Emotiv)
        // Mag(gauss)  = (X * 4 * Max_Mag *2 ) / 2^16 - Max_Mag
        private static float ConvertGauss(float gauss)
        {
            return (gauss * 4 * maxMag * 2) / (float) Math.Pow(2, 16) - maxMag;
        }

        // CopySign does not exist in System v4's Math, so we manually define it here.
        // Returns: a value with a magnitude of x and the sign of y.
        // I.E. copysign ( 10.0,-1.0) = -10.0, copysign (-10.0,-1.0) = -10.0, copysign (-10.0, 1.0) = 10.0
        private static double CopySign(double x, double y)
        {
            bool isNegative = Math.Sign(y) == -1; // Math.sign returns -1 if < 0, 0 if == 0, 1 if > 0
            int toMultiplyBy = isNegative ? -1 : 1;
            return Math.Abs(x) * toMultiplyBy;
        }
    }
}
