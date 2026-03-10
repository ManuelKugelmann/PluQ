/*
PluQ Frontend Example
Demonstrates how to use PluQ IPC in frontend mode to receive game state and send input
*/

using System;
using System.Threading;
using PluQ;

namespace PluQ.Examples
{
    class PluQFrontendExample
    {
        static void Main(string[] args)
        {
            Console.WriteLine("PluQ Frontend Example");
            Console.WriteLine("=====================\n");

            // Initialize PluQ in frontend mode
            using (var pluq = new PluQIPC())
            {
                Console.WriteLine("Waiting for backend to start...");

                // Try to connect to backend
                bool connected = false;
                for (int i = 0; i < 10; i++)
                {
                    if (pluq.Initialize(PluQMode.Frontend))
                    {
                        connected = true;
                        break;
                    }
                    Thread.Sleep(1000);
                }

                if (!connected)
                {
                    Console.WriteLine("Failed to connect to backend. Make sure backend is running.");
                    return;
                }

                Console.WriteLine("Frontend initialized. Receiving game state...");
                Console.WriteLine("Press Ctrl+C to exit\n");

                uint lastFrameNumber = 0;
                int framesReceived = 0;

                // Render loop
                while (true)
                {
                    // Receive state from backend
                    if (pluq.ReceiveWorldState(out PluQFrameHeader header, out Entity[] entities, out DLight[] dlights))
                    {
                        framesReceived++;

                        // Display state every 60 frames
                        if (framesReceived % 60 == 0)
                        {
                            DisplayGameState(header, entities, dlights);
                        }

                        lastFrameNumber = header.FrameNumber;
                    }

                    // Send input to backend
                    var inputCmd = CreateInputCommand(lastFrameNumber);
                    pluq.SendInput(inputCmd);

                    // Simulate 60 FPS
                    Thread.Sleep(16);
                }
            }
        }

        static void DisplayGameState(PluQFrameHeader header, Entity[] entities, DLight[] dlights)
        {
            string mapName = System.Text.Encoding.ASCII.GetString(header.MapName).TrimEnd('\0');

            Console.WriteLine($"\n=== Frame {header.FrameNumber} ===");
            Console.WriteLine($"Map: {mapName}");
            Console.WriteLine($"Player Position: ({header.PlayerOrigin.X:F1}, {header.PlayerOrigin.Y:F1}, {header.PlayerOrigin.Z:F1})");
            Console.WriteLine($"Player Angles: ({header.PlayerAngles.X:F1}, {header.PlayerAngles.Y:F1}, {header.PlayerAngles.Z:F1})");
            Console.WriteLine($"Health: {header.PlayerHealth:F0} | Armor: {header.PlayerArmor:F0}");
            Console.WriteLine($"Weapon: {header.PlayerWeapon} | Ammo: {header.PlayerAmmo}");
            Console.WriteLine($"Entities: {header.NumEntities}");
            Console.WriteLine($"Dynamic Lights: {dlights.Length}");

            if (header.Paused != 0)
                Console.WriteLine("** PAUSED **");
        }

        static PluQInputCmd CreateInputCommand(uint frameNumber)
        {
            var inputCmd = new PluQInputCmd();
            inputCmd.Sequence = frameNumber;
            inputCmd.Timestamp = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;

            // Simulate some input (moving forward and strafing)
            inputCmd.ForwardMove = 200.0f;
            inputCmd.SideMove = (float)Math.Sin(frameNumber * 0.1f) * 100;
            inputCmd.UpMove = 0;

            // View angles
            inputCmd.ViewAngles = new Vector3(0, frameNumber * 0.5f, 0);

            // Buttons (simulate attack every 30 frames)
            inputCmd.Buttons = (frameNumber % 30 == 0) ? 1u : 0u;

            inputCmd.Impulse = 0;
            inputCmd.Padding1 = new byte[3];
            inputCmd.CmdText = new byte[PluQConstants.CmdTextSize];

            return inputCmd;
        }
    }
}
