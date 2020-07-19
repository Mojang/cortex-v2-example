﻿using System;
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
        const int noiseThreshold = 20;
        const int baselineThreshold = 1000;

        private static GyrometerType currentGyroType = GyrometerType.Unknown;
        private static float rollingSum = 0;
        private static int gyroXIndex = -1;
        private static int gyroYIndex = -1;
        private static int gyroZIndex = -1;
        private static int gyroWIndex = -1;

        private static FileStream OutFileStream;

        // Reference: https://emotiv.gitbook.io/epoc-user-manual/introduction-1/technical_specifications
        enum GyrometerType
        {
            G, // +- 8g: Epoc v1.0. Uses GYROX, GYROY.
            DPS, // +- 500 d/s: Epoc+ v1.1. Uses GYROX, GYROY, GYROZ.
            Quaternion, // 4D Quaternions: Epoc X, Epoc+ v1.1A. Uses Q0, Q1, Q2, Q3.
            Unknown, // Default case of unknown Gyrometer
        }

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
            rollingSum = 0;


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
            WriteDataToFile(motData);
            
        }

        private static float ConvertDegreesPerSecond(float dps)
        {
            return 0.0f;
        }
    }
}
