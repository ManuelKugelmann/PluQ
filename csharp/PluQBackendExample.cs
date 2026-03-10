/*
PluQ Backend Example
Demonstrates how to use PluQ IPC in backend mode to broadcast game state
*/

using System;
using System.Threading;
using PluQ;

namespace PluQ.Examples
{
    class PluQBackendExample
    {
        static void Main(string[] args)
        {
            Console.WriteLine("PluQ Backend Example");
            Console.WriteLine("====================\n");

            // Initialize PluQ in backend mode
            using (var pluq = new PluQIPC())
            {
                if (!pluq.Initialize(PluQMode.Backend))
                {
                    Console.WriteLine("Failed to initialize PluQ backend");
                    return;
                }

                Console.WriteLine("Backend initialized. Broadcasting game state...");
                Console.WriteLine("Press Ctrl+C to exit\n");

                uint frameNumber = 0;

                // Simulation loop
                while (true)
                {
                    // Simulate game state
                    var header = CreateFrameHeader(frameNumber);
                    var entities = CreateEntities(frameNumber);
                    var dlights = CreateDynamicLights(frameNumber);

                    // Broadcast state to frontend
                    pluq.BroadcastWorldState(header, entities, dlights);

                    // Check for input from frontend
                    if (pluq.HasPendingInput())
                    {
                        if (pluq.ReceiveInput(out PluQInputCmd inputCmd))
                        {
                            ProcessInput(inputCmd);
                        }
                    }

                    // Display statistics every 60 frames
                    if (frameNumber % 60 == 0 && frameNumber > 0)
                    {
                        var stats = pluq.GetStats();
                        Console.WriteLine($"Frame {frameNumber}: " +
                            $"Sent: {stats.FramesSent}, " +
                            $"Avg time: {stats.TotalTime / stats.FramesSent:F3}ms, " +
                            $"Min: {stats.MinFrameTime:F3}ms, " +
                            $"Max: {stats.MaxFrameTime:F3}ms");
                    }

                    frameNumber++;

                    // Simulate 60 FPS (16.67ms per frame)
                    Thread.Sleep(16);
                }
            }
        }

        static PluQFrameHeader CreateFrameHeader(uint frameNumber)
        {
            var header = new PluQFrameHeader();
            header.FrameNumber = frameNumber;
            header.Timestamp = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
            header.NumEntities = 10; // Example: 10 entities
            header.MaxEntities = PluQConstants.MaxEntities;

            // Simulate player moving in a circle
            float angle = frameNumber * 0.05f;
            header.PlayerOrigin = new Vector3(
                (float)Math.Cos(angle) * 100,
                (float)Math.Sin(angle) * 100,
                0
            );
            header.PlayerAngles = new Vector3(0, angle, 0);
            header.PlayerHealth = 100;
            header.PlayerArmor = 50;
            header.PlayerWeapon = 1;
            header.PlayerAmmo = 50;

            header.Paused = 0;
            header.InGame = 1;
            header.MapName = new byte[PluQConstants.MapNameSize];
            System.Text.Encoding.ASCII.GetBytes("e1m1").CopyTo(header.MapName, 0);

            return header;
        }

        static Entity[] CreateEntities(uint frameNumber)
        {
            var entities = new Entity[PluQConstants.MaxEntities];

            // Create some example entities
            for (int i = 0; i < 10; i++)
            {
                entities[i] = new Entity
                {
                    Origin = new Vector3(i * 50, 0, 0),
                    Angles = new Vector3(0, frameNumber * 0.1f, 0),
                    ModelIndex = 1,
                    Frame = (int)(frameNumber % 10),
                    SkinNum = 0,
                    Effects = 0,
                    Alpha = 1.0f,
                    Reserved = new byte[64]
                };
            }

            return entities;
        }

        static DLight[] CreateDynamicLights(uint frameNumber)
        {
            var dlights = new DLight[PluQConstants.MaxDLights];

            // Create a pulsing light
            dlights[0] = new DLight
            {
                Origin = new Vector3(0, 0, 100),
                Radius = 200 + (float)Math.Sin(frameNumber * 0.1f) * 50,
                Die = float.MaxValue,
                Decay = 0,
                MinLight = 0,
                Key = 1,
                Color = new Vector3(1.0f, 0.8f, 0.6f)
            };

            return dlights;
        }

        static void ProcessInput(PluQInputCmd inputCmd)
        {
            Console.WriteLine($"Received input - " +
                $"Forward: {inputCmd.ForwardMove:F2}, " +
                $"Side: {inputCmd.SideMove:F2}, " +
                $"Buttons: 0x{inputCmd.Buttons:X}");
        }
    }
}
