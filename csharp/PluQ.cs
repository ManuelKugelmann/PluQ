/*
PluQ IPC - C# Implementation
Copyright (C) 2024 QuakeSpasm/Ironwail developers

Shared memory-based IPC for game state broadcasting and input reception.
Compatible with the C implementation from PluQuake/Ironwail.

License: GPL v2 or later
*/

using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

namespace PluQ
{
    public class PluQIPC : IDisposable
    {
        private PluQMode _mode = PluQMode.Disabled;
        private MemoryMappedFile _sharedMemory;
        private MemoryMappedViewAccessor _accessor;
        private long _sharedMemorySize;
        private bool _initialized = false;

        // Statistics
        private PluQStats _stats = new PluQStats();
        private DateTime _startTime;

        // Last known state
        private uint _lastFrameSequence = 0;

        public PluQIPC()
        {
            _sharedMemorySize = Marshal.SizeOf<PluQSharedMemory>();
        }

        /// <summary>
        /// Initialize PluQ IPC with specified mode
        /// </summary>
        public bool Initialize(PluQMode mode)
        {
            if (_initialized)
            {
                Console.WriteLine("PluQ already initialized");
                return false;
            }

            _mode = mode;

            if (_mode == PluQMode.Disabled)
                return true;

            try
            {
                // Create or open shared memory
                if (_mode == PluQMode.Backend)
                {
                    // Backend creates the shared memory
                    _sharedMemory = MemoryMappedFile.CreateOrOpen(
                        PluQConstants.SharedMemoryName,
                        _sharedMemorySize,
                        MemoryMappedFileAccess.ReadWrite);
                }
                else if (_mode == PluQMode.Frontend)
                {
                    // Frontend opens existing shared memory
                    _sharedMemory = MemoryMappedFile.OpenExisting(
                        PluQConstants.SharedMemoryName,
                        MemoryMappedFileRights.ReadWrite);
                }

                _accessor = _sharedMemory.CreateViewAccessor();
                _initialized = true;
                _startTime = DateTime.UtcNow;

                Console.WriteLine($"PluQ initialized in {_mode} mode");
                Console.WriteLine($"Shared memory size: {_sharedMemorySize} bytes");

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PluQ initialization failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Shutdown PluQ IPC
        /// </summary>
        public void Shutdown()
        {
            if (!_initialized)
                return;

            _accessor?.Dispose();
            _sharedMemory?.Dispose();
            _initialized = false;
            _mode = PluQMode.Disabled;

            Console.WriteLine("PluQ shutdown complete");
        }

        /// <summary>
        /// Broadcast world state (Backend only)
        /// </summary>
        public void BroadcastWorldState(PluQFrameHeader header, Entity[] entities, DLight[] dlights)
        {
            if (!_initialized || _mode != PluQMode.Backend)
                return;

            DateTime broadcastStart = DateTime.UtcNow;

            try
            {
                // Set write-in-progress flag
                _accessor.Write(4, (uint)1); // Offset 4 is WriteInProgress

                // Write frame header
                int headerOffset = 8; // After FrameSequence and WriteInProgress
                WriteStruct(headerOffset, header);

                // Write entities
                int entitiesOffset = headerOffset + Marshal.SizeOf<PluQFrameHeader>();
                for (int i = 0; i < header.NumEntities && i < PluQConstants.MaxEntities; i++)
                {
                    WriteStruct(entitiesOffset + i * Marshal.SizeOf<Entity>(), entities[i]);
                }

                // Write dynamic lights
                int dlightsOffset = entitiesOffset + PluQConstants.MaxEntities * Marshal.SizeOf<Entity>();
                ushort numDLights = (ushort)Math.Min(dlights.Length, PluQConstants.MaxDLights);

                for (int i = 0; i < numDLights; i++)
                {
                    WriteStruct(dlightsOffset + i * Marshal.SizeOf<DLight>(), dlights[i]);
                }

                // Write num_dlights
                _accessor.Write(dlightsOffset + PluQConstants.MaxDLights * Marshal.SizeOf<DLight>(), numDLights);

                // Increment frame sequence (signals new data available)
                uint currentSequence = _accessor.ReadUInt32(0);
                _accessor.Write(0, currentSequence + 1);

                // Clear write-in-progress flag
                _accessor.Write(4, (uint)0);

                // Update statistics
                double frameTime = (DateTime.UtcNow - broadcastStart).TotalMilliseconds;
                _stats.FramesSent++;
                _stats.TotalTime += frameTime;
                _stats.TotalEntities += header.NumEntities;

                if (_stats.FramesSent == 1 || frameTime > _stats.MaxFrameTime)
                    _stats.MaxFrameTime = frameTime;
                if (_stats.FramesSent == 1 || frameTime < _stats.MinFrameTime)
                    _stats.MinFrameTime = frameTime;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error broadcasting state: {ex.Message}");
            }
        }

        /// <summary>
        /// Receive world state (Frontend only)
        /// Returns true if new data available
        /// </summary>
        public bool ReceiveWorldState(out PluQFrameHeader header, out Entity[] entities, out DLight[] dlights)
        {
            header = new PluQFrameHeader();
            entities = new Entity[PluQConstants.MaxEntities];
            dlights = new DLight[PluQConstants.MaxDLights];

            if (!_initialized || _mode != PluQMode.Frontend)
                return false;

            try
            {
                // Check if write is in progress
                uint writeInProgress = _accessor.ReadUInt32(4);
                if (writeInProgress != 0)
                {
                    // Wait briefly for write to complete
                    Thread.SpinWait(100);
                    writeInProgress = _accessor.ReadUInt32(4);
                    if (writeInProgress != 0)
                        return false; // Still writing, skip this frame
                }

                // Check frame sequence
                uint currentSequence = _accessor.ReadUInt32(0);
                if (currentSequence == _lastFrameSequence)
                    return false; // No new data

                _lastFrameSequence = currentSequence;

                // Read frame header
                int headerOffset = 8;
                header = ReadStruct<PluQFrameHeader>(headerOffset);

                // Read entities
                int entitiesOffset = headerOffset + Marshal.SizeOf<PluQFrameHeader>();
                for (int i = 0; i < header.NumEntities && i < PluQConstants.MaxEntities; i++)
                {
                    entities[i] = ReadStruct<Entity>(entitiesOffset + i * Marshal.SizeOf<Entity>());
                }

                // Read dynamic lights
                int dlightsOffset = entitiesOffset + PluQConstants.MaxEntities * Marshal.SizeOf<Entity>();
                ushort numDLights = _accessor.ReadUInt16(dlightsOffset + PluQConstants.MaxDLights * Marshal.SizeOf<DLight>());

                for (int i = 0; i < numDLights && i < PluQConstants.MaxDLights; i++)
                {
                    dlights[i] = ReadStruct<DLight>(dlightsOffset + i * Marshal.SizeOf<DLight>());
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving state: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send input command (Frontend only)
        /// </summary>
        public void SendInput(PluQInputCmd inputCmd)
        {
            if (!_initialized || _mode != PluQMode.Frontend)
                return;

            try
            {
                // Calculate offset to input_cmd in shared memory
                int inputCmdOffset = 8 + // FrameSequence + WriteInProgress
                    Marshal.SizeOf<PluQFrameHeader>() +
                    PluQConstants.MaxEntities * Marshal.SizeOf<Entity>() +
                    PluQConstants.MaxDLights * Marshal.SizeOf<DLight>() +
                    2 + // num_dlights
                    2; // padding

                WriteStruct(inputCmdOffset, inputCmd);

                // Set input ready flag
                int inputReadyOffset = inputCmdOffset + Marshal.SizeOf<PluQInputCmd>();
                _accessor.Write(inputReadyOffset, (uint)1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending input: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if input is ready (Backend only)
        /// </summary>
        public bool HasPendingInput()
        {
            if (!_initialized || _mode != PluQMode.Backend)
                return false;

            try
            {
                int inputCmdOffset = 8 +
                    Marshal.SizeOf<PluQFrameHeader>() +
                    PluQConstants.MaxEntities * Marshal.SizeOf<Entity>() +
                    PluQConstants.MaxDLights * Marshal.SizeOf<DLight>() +
                    2 + 2;

                int inputReadyOffset = inputCmdOffset + Marshal.SizeOf<PluQInputCmd>();
                return _accessor.ReadUInt32(inputReadyOffset) != 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Receive input command (Backend only)
        /// </summary>
        public bool ReceiveInput(out PluQInputCmd inputCmd)
        {
            inputCmd = new PluQInputCmd();

            if (!_initialized || _mode != PluQMode.Backend)
                return false;

            try
            {
                int inputCmdOffset = 8 +
                    Marshal.SizeOf<PluQFrameHeader>() +
                    PluQConstants.MaxEntities * Marshal.SizeOf<Entity>() +
                    PluQConstants.MaxDLights * Marshal.SizeOf<DLight>() +
                    2 + 2;

                int inputReadyOffset = inputCmdOffset + Marshal.SizeOf<PluQInputCmd>();

                if (_accessor.ReadUInt32(inputReadyOffset) == 0)
                    return false;

                inputCmd = ReadStruct<PluQInputCmd>(inputCmdOffset);

                // Clear input ready flag
                _accessor.Write(inputReadyOffset, (uint)0);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving input: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get performance statistics
        /// </summary>
        public PluQStats GetStats()
        {
            return _stats;
        }

        /// <summary>
        /// Reset performance statistics
        /// </summary>
        public void ResetStats()
        {
            _stats = new PluQStats();
            _startTime = DateTime.UtcNow;
        }

        // Properties
        public PluQMode Mode => _mode;
        public bool IsEnabled => _initialized && _mode != PluQMode.Disabled;
        public bool IsBackend => _mode == PluQMode.Backend || _mode == PluQMode.Both;
        public bool IsFrontend => _mode == PluQMode.Frontend;

        // Helper methods for reading/writing structs
        private void WriteStruct<T>(int offset, T data) where T : struct
        {
            _accessor.Write(offset, ref data);
        }

        private T ReadStruct<T>(int offset) where T : struct
        {
            T result;
            _accessor.Read(offset, out result);
            return result;
        }

        public void Dispose()
        {
            Shutdown();
        }
    }
}
