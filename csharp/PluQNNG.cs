/*
PluQ IPC - C# nng + FlatBuffers Implementation
Copyright (C) 2024 QuakeSpasm/Ironwail developers

Three-channel IPC using nng for transport and FlatBuffers for serialization.

License: GPL v2 or later
*/

using System;
using nng;
using nng.Native;
using Google.FlatBuffers;

namespace PluQ.NNG
{
    public class PluQNNG : IDisposable
    {
        // Channel URLs
        public const string URL_RESOURCES = "ipc:///tmp/quake_pluq_resources";
        public const string URL_GAMEPLAY = "ipc:///tmp/quake_pluq_gameplay";
        public const string URL_INPUT = "ipc:///tmp/quake_pluq_input";

        // Sockets
        private IRepSocket _resourcesRep;     // Backend: Reply to resource requests
        private IReqSocket _resourcesReq;     // Frontend: Request resources
        private IPubSocket _gameplayPub;      // Backend: Publish game frames
        private ISubSocket _gameplaySub;      // Frontend: Subscribe to frames
        private IPullSocket _inputPull;       // Backend: Pull input commands
        private IPushSocket _inputPush;       // Frontend: Push input commands

        private readonly bool _isBackend;
        private bool _initialized;

        public PluQNNG(bool isBackend)
        {
            _isBackend = isBackend;
        }

        /// <summary>
        /// Initialize PluQ nng IPC
        /// </summary>
        public bool Initialize()
        {
            try
            {
                var factory = nng.Latest.Factory;

                if (_isBackend)
                {
                    // Backend: Setup reply, publish, and pull sockets

                    // Resources channel: REP socket
                    _resourcesRep = factory.ReplierOpen().Unwrap();
                    _resourcesRep.Listen(URL_RESOURCES).Unwrap();

                    // Gameplay channel: PUB socket
                    _gameplayPub = factory.PublisherOpen().Unwrap();
                    _gameplayPub.Listen(URL_GAMEPLAY).Unwrap();

                    // Input channel: PULL socket
                    _inputPull = factory.PullerOpen().Unwrap();
                    _inputPull.Listen(URL_INPUT).Unwrap();

                    Console.WriteLine("PluQ_NNG: Backend initialized");
                    Console.WriteLine($"  Resources: {URL_RESOURCES} (REP)");
                    Console.WriteLine($"  Gameplay:  {URL_GAMEPLAY} (PUB)");
                    Console.WriteLine($"  Input:     {URL_INPUT} (PULL)");
                }
                else
                {
                    // Frontend: Setup request, subscribe, and push sockets

                    // Resources channel: REQ socket
                    _resourcesReq = factory.RequesterOpen().Unwrap();
                    _resourcesReq.Dial(URL_RESOURCES).Unwrap();

                    // Gameplay channel: SUB socket (subscribe to all)
                    _gameplaySub = factory.SubscriberOpen().Unwrap();
                    _gameplaySub.Subscribe().Unwrap();  // Subscribe to all topics
                    _gameplaySub.Dial(URL_GAMEPLAY).Unwrap();

                    // Input channel: PUSH socket
                    _inputPush = factory.PusherOpen().Unwrap();
                    _inputPush.Dial(URL_INPUT).Unwrap();

                    Console.WriteLine("PluQ_NNG: Frontend initialized");
                    Console.WriteLine($"  Resources: {URL_RESOURCES} (REQ)");
                    Console.WriteLine($"  Gameplay:  {URL_GAMEPLAY} (SUB)");
                    Console.WriteLine($"  Input:     {URL_INPUT} (PUSH)");
                }

                _initialized = true;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PluQ_NNG initialization failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Shutdown PluQ nng IPC
        /// </summary>
        public void Shutdown()
        {
            if (!_initialized)
                return;

            _resourcesRep?.Dispose();
            _resourcesReq?.Dispose();
            _gameplayPub?.Dispose();
            _gameplaySub?.Dispose();
            _inputPull?.Dispose();
            _inputPush?.Dispose();

            _initialized = false;
            Console.WriteLine("PluQ_NNG: Shutdown complete");
        }

        // ====================================================================
        // RESOURCES CHANNEL (REQ/REP)
        // ====================================================================

        /// <summary>
        /// Backend: Receive resource request
        /// </summary>
        public byte[] ReceiveResourceRequest(int timeoutMs = 0)
        {
            if (!_initialized || !_isBackend)
                return null;

            try
            {
                var msg = _resourcesRep.RecvMsg(timeoutMs >= 0 ?
                    nng.SendReceiveFlags.NONBLOCK : nng.SendReceiveFlags.NONE).Unwrap();
                return msg.AsSpan().ToArray();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Backend: Send resource response
        /// </summary>
        public bool SendResource(byte[] data)
        {
            if (!_initialized || !_isBackend)
                return false;

            try
            {
                _resourcesRep.Send(data).Unwrap();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PluQ_NNG: Failed to send resource: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Frontend: Request resource
        /// </summary>
        public bool RequestResource(byte[] requestData)
        {
            if (!_initialized || _isBackend)
                return false;

            try
            {
                _resourcesReq.Send(requestData).Unwrap();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PluQ_NNG: Failed to request resource: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Frontend: Receive resource response
        /// </summary>
        public byte[] ReceiveResource(int timeoutMs = -1)
        {
            if (!_initialized || _isBackend)
                return null;

            try
            {
                var msg = _resourcesReq.RecvMsg(timeoutMs >= 0 ?
                    nng.SendReceiveFlags.NONBLOCK : nng.SendReceiveFlags.NONE).Unwrap();
                return msg.AsSpan().ToArray();
            }
            catch
            {
                return null;
            }
        }

        // ====================================================================
        // GAMEPLAY CHANNEL (PUB/SUB)
        // ====================================================================

        /// <summary>
        /// Backend: Publish gameplay frame
        /// </summary>
        public bool PublishFrame(byte[] frameData)
        {
            if (!_initialized || !_isBackend)
                return false;

            try
            {
                _gameplayPub.Send(frameData, nng.SendReceiveFlags.NONBLOCK).Unwrap();
                return true;
            }
            catch (Exception ex)
            {
                // EAGAIN is normal for non-blocking pub
                if (!ex.Message.Contains("EAGAIN"))
                {
                    Console.WriteLine($"PluQ_NNG: Failed to publish frame: {ex.Message}");
                }
                return false;
            }
        }

        /// <summary>
        /// Frontend: Receive gameplay frame (non-blocking)
        /// </summary>
        public byte[] ReceiveFrame()
        {
            if (!_initialized || _isBackend)
                return null;

            try
            {
                var msg = _gameplaySub.RecvMsg(nng.SendReceiveFlags.NONBLOCK).Unwrap();
                return msg.AsSpan().ToArray();
            }
            catch
            {
                // No message available (normal for non-blocking)
                return null;
            }
        }

        // ====================================================================
        // INPUT CHANNEL (PUSH/PULL)
        // ====================================================================

        /// <summary>
        /// Frontend: Send input command
        /// </summary>
        public bool SendInput(byte[] inputData)
        {
            if (!_initialized || _isBackend)
                return false;

            try
            {
                _inputPush.Send(inputData, nng.SendReceiveFlags.NONBLOCK).Unwrap();
                return true;
            }
            catch (Exception ex)
            {
                // EAGAIN is normal for non-blocking push
                if (!ex.Message.Contains("EAGAIN"))
                {
                    Console.WriteLine($"PluQ_NNG: Failed to send input: {ex.Message}");
                }
                return false;
            }
        }

        /// <summary>
        /// Backend: Receive input command (non-blocking)
        /// </summary>
        public byte[] ReceiveInput()
        {
            if (!_initialized || !_isBackend)
                return null;

            try
            {
                var msg = _inputPull.RecvMsg(nng.SendReceiveFlags.NONBLOCK).Unwrap();
                return msg.AsSpan().ToArray();
            }
            catch
            {
                // No message available (normal for non-blocking)
                return null;
            }
        }

        // Properties
        public bool IsBackend => _isBackend;
        public bool IsFrontend => !_isBackend;
        public bool IsInitialized => _initialized;

        public void Dispose()
        {
            Shutdown();
        }
    }
}
