/*
PluQ IPC - C# Port
Copyright (C) 2024 QuakeSpasm/Ironwail developers

This is a C# port of the PluQ shared memory IPC system.
Compatible with the C implementation from PluQuake/Ironwail.

License: GPL v2 or later
*/

using System;
using System.Runtime.InteropServices;

namespace PluQ
{
    // Constants
    public static class PluQConstants
    {
        public const int MaxEntities = 8192;
        public const int MaxDLights = 32;
        public const string SharedMemoryName = "quake_pluq_ironwail";
        public const int CmdTextSize = 256;
        public const int MapNameSize = 64;
    }

    // PluQ operation modes
    public enum PluQMode
    {
        Disabled = 0,   // PluQ not active
        Backend = 1,    // Backend: run simulation, broadcast state, receive input
        Frontend = 2,   // Frontend: receive state from backend, send input
        Both = 1        // Same as Backend (legacy name)
    }

    // Vector3 structure (compatible with C vec3_t)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Vector3
    {
        public float X;
        public float Y;
        public float Z;

        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    // Entity structure (simplified, compatible with C entity_t)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Entity
    {
        public Vector3 Origin;
        public Vector3 Angles;
        public int ModelIndex;
        public int Frame;
        public int SkinNum;
        public int Effects;
        public float Alpha;

        // Padding to match C structure size (approximate)
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] Reserved;
    }

    // Dynamic light structure (compatible with C dlight_t)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DLight
    {
        public Vector3 Origin;
        public float Radius;
        public float Die;           // stop lighting after this time
        public float Decay;         // drop this each second
        public float MinLight;      // don't add when contributing less
        public int Key;
        public Vector3 Color;
    }

    // Input command structure
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PluQInputCmd
    {
        public uint Sequence;
        public double Timestamp;

        // Movement
        public float ForwardMove;
        public float SideMove;
        public float UpMove;

        // View
        public Vector3 ViewAngles;

        // Buttons (bitfield)
        // Bit 0: Attack
        // Bit 1: Jump
        // Bit 2: Use
        public uint Buttons;

        // Impulse command (weapon selection, etc)
        public byte Impulse;

        // Padding for alignment
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] Padding1;

        // Console commands (optional)
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = PluQConstants.CmdTextSize)]
        public byte[] CmdText;
    }

    // Frame header structure
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PluQFrameHeader
    {
        public uint FrameNumber;
        public double Timestamp;
        public ushort NumEntities;
        public ushort MaxEntities;

        // Player/view state
        public Vector3 PlayerOrigin;
        public Vector3 PlayerAngles;
        public float PlayerHealth;
        public float PlayerArmor;
        public int PlayerWeapon;
        public int PlayerAmmo;

        // Game state
        public byte Paused;         // qboolean
        public byte InGame;         // qboolean

        // Padding for alignment
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] Padding1;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = PluQConstants.MapNameSize)]
        public byte[] MapName;
    }

    // Shared memory layout
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PluQSharedMemory
    {
        // Synchronization (using atomic operations)
        public volatile uint FrameSequence;
        public volatile uint WriteInProgress;

        // Frame data
        public PluQFrameHeader Header;

        // Entity data
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = PluQConstants.MaxEntities)]
        public Entity[] Entities;

        // Dynamic lights
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = PluQConstants.MaxDLights)]
        public DLight[] DLights;
        public ushort NumDLights;

        // Padding for alignment
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] Padding1;

        // Input command (from frontend to backend)
        public PluQInputCmd InputCmd;
        public volatile uint InputReady;
    }

    // Performance statistics
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PluQStats
    {
        public ulong FramesSent;
        public double TotalTime;
        public ulong TotalEntities;
        public double MaxFrameTime;
        public double MinFrameTime;
    }
}
