# PluQ IPC - C# Implementation

A C# port of the PluQ shared memory-based Inter-Process Communication system from PluQuake/Ironwail.

## Overview

PluQ enables frontend/backend separation in game engines by broadcasting game state from a backend (simulation) process to one or more frontend (rendering) processes via shared memory.

## IPC Mechanism: Shared Memory vs ZeroMQ

### What IPC Mechanism Does PluQ Use?

PluQ uses **platform-specific shared memory**:

- **Windows**: `MemoryMappedFile` (.NET) / `CreateFileMapping` (Win32 API)
- **Linux/Unix**: `MemoryMappedFile` (.NET) / `shm_open` + `mmap` (POSIX)

### Is PluQ as Versatile as ZeroMQ?

**Short answer: No.** PluQ is purpose-built for a specific use case, while ZeroMQ is a general-purpose messaging library.

### Detailed Comparison

| Feature | PluQ | ZeroMQ |
|---------|------|---------|
| **Transport** | Shared memory only (local machine) | TCP, IPC, inproc, PGM/EPGM multicast |
| **Network Support** | No - local only | Yes - works across networks |
| **Latency** | Ultra-low (~1-10 μs) | Low (~50-500 μs local, more over network) |
| **Messaging Pattern** | State broadcasting + input receiving | Pub/Sub, Req/Rep, Push/Pull, Pair, etc. |
| **Message Queuing** | No - single shared state | Yes - message queues with buffering |
| **Data Model** | Fixed binary structure | Arbitrary byte arrays |
| **Synchronization** | Lock-free (atomic flags) | Internal (various patterns) |
| **Multiple Clients** | Possible (read-only for frontends) | Native support (many-to-many) |
| **Versioning** | Manual (structure compatibility) | Manual (application-level) |
| **Language Support** | C, C#, any with shared memory API | 40+ languages (bindings) |
| **Use Case** | Local game engine IPC | Distributed systems, microservices |
| **Overhead** | Minimal (direct memory access) | Higher (serialization, sockets) |
| **Reliability** | No guarantees (overwrite model) | Configurable (HWM, reconnection) |

### When to Use PluQ

✅ **Use PluQ when:**
- You need **ultra-low latency** (microseconds)
- Communication is **local only** (same machine)
- You have a **state broadcasting** pattern (one writer, multiple readers)
- You want **zero-copy** transfer of large state
- You control both ends and can maintain **binary compatibility**
- You need **minimal dependencies** (OS-level primitives only)

### When to Use ZeroMQ

✅ **Use ZeroMQ when:**
- You need **network communication** (different machines)
- You want **flexible messaging patterns** (pub/sub, req/rep, etc.)
- You need **message queuing** and buffering
- You want **language-agnostic** communication
- You need **dynamic topology** (services coming and going)
- You want **built-in patterns** (load balancing, broker, etc.)
- You need **reliability features** (reconnection, HWM, etc.)

### Architecture Differences

#### PluQ Architecture
```
Backend Process                Frontend Process(es)
┌─────────────────┐           ┌──────────────────┐
│  Game Logic     │           │  Rendering       │
│  Physics        │           │  Input Handling  │
│  AI             │           │  Audio           │
│                 │           │                  │
│  Write State ───┼──────────>│  Read State      │
│  Read Input  <──┼───────────│  Write Input     │
└─────────────────┘           └──────────────────┘
         │                             │
         └──────── Shared Memory ──────┘
            (single memory region)
```

**Characteristics:**
- Single shared memory region (~24MB with max entities)
- Backend writes state, frontends read
- Frontends write input, backend reads
- Overwrite model (latest state only)
- No message history

#### ZeroMQ Architecture
```
Backend Service            Frontend Service(s)
┌─────────────────┐       ┌──────────────────┐
│  Publisher      │       │  Subscriber      │
│                 │       │                  │
│  Send Msgs ─────┼──────>│  Receive Msgs    │
│                 │       │                  │
└─────────────────┘       └──────────────────┘
         │                         │
         └──── Socket (TCP/IPC) ───┘
           (message queue)
```

**Characteristics:**
- Socket-based (TCP, IPC, etc.)
- Message queue with configurable buffering
- Multiple messaging patterns
- Can work across network
- Message history (up to HWM)

## Technical Specifications

### Shared Memory Layout

```
┌───────────────────────────────────────┐
│ Synchronization                       │
├───────────────────────────────────────┤
│ frame_sequence (uint32)               │  Atomic counter
│ write_in_progress (uint32)            │  Lock flag
├───────────────────────────────────────┤
│ Frame Header                          │
├───────────────────────────────────────┤
│ frame_number, timestamp               │
│ player state (position, health, etc.) │
│ game state (map, paused, etc.)        │
├───────────────────────────────────────┤
│ Entities Array                        │
├───────────────────────────────────────┤
│ entities[0..8191]                     │
│ (positions, models, effects)          │
├───────────────────────────────────────┤
│ Dynamic Lights Array                  │
├───────────────────────────────────────┤
│ dlights[0..31]                        │
│ num_dlights                           │
├───────────────────────────────────────┤
│ Input Command                         │
├───────────────────────────────────────┤
│ movement, view angles, buttons        │
│ input_ready (uint32)                  │
└───────────────────────────────────────┘

Total Size: ~24 MB (with 8192 max entities)
```

### Performance Characteristics

| Metric | Value |
|--------|-------|
| **Max Entities** | 8,192 |
| **Max Dynamic Lights** | 32 |
| **Shared Memory Size** | ~24 MB |
| **Typical Latency** | 1-10 μs (local) |
| **Throughput** | 60+ state updates/sec |
| **Synchronization** | Lock-free (volatile flags) |

## Usage

### C# Backend Example

```csharp
using PluQ;

// Initialize backend
var pluq = new PluQIPC();
pluq.Initialize(PluQMode.Backend);

// Game loop
while (true)
{
    // Create frame data
    var header = CreateFrameHeader();
    var entities = GetEntities();
    var dlights = GetDynamicLights();

    // Broadcast state
    pluq.BroadcastWorldState(header, entities, dlights);

    // Process input
    if (pluq.HasPendingInput())
    {
        pluq.ReceiveInput(out var input);
        ProcessPlayerInput(input);
    }

    // 60 FPS
    Thread.Sleep(16);
}
```

### C# Frontend Example

```csharp
using PluQ;

// Initialize frontend
var pluq = new PluQIPC();
pluq.Initialize(PluQMode.Frontend);

// Render loop
while (true)
{
    // Receive state
    if (pluq.ReceiveWorldState(out var header, out var entities, out var dlights))
    {
        RenderScene(header, entities, dlights);
    }

    // Send input
    var input = GetPlayerInput();
    pluq.SendInput(input);

    // 60 FPS
    Thread.Sleep(16);
}
```

## Building

### C Implementation

```bash
cd c/
# Build with your Quake/Ironwail Makefile
make -f Makefile.pluq_frontend
```

### C# Implementation

```bash
cd csharp/
# Using .NET CLI
dotnet build

# Or with MSBuild
msbuild
```

### Running Examples

**Terminal 1 (Backend):**
```bash
dotnet run --project PluQBackendExample.csproj
```

**Terminal 2 (Frontend):**
```bash
dotnet run --project PluQFrontendExample.csproj
```

## Key Features

### 1. Zero-Copy Transfer
Direct memory access eliminates serialization overhead:
- Backend writes directly to shared memory
- Frontend reads directly from shared memory
- No marshaling or copying (except initial structure read)

### 2. Lock-Free Synchronization
Uses atomic operations instead of mutexes:
- `write_in_progress` flag prevents reading during writes
- `frame_sequence` counter signals new data
- `input_ready` flag signals input availability

### 3. Platform Abstraction
Same high-level API on Windows and Linux:
- Windows: File mapping API
- Linux: POSIX shared memory
- .NET: `MemoryMappedFile` abstraction

### 4. Performance Tracking
Built-in statistics:
- Frames sent/received
- Average/min/max latency
- Entity counts

## Protocol Compatibility

The C# implementation is **binary-compatible** with the C implementation:
- Same structure layout (`StructLayout.Sequential, Pack=1`)
- Same shared memory name (`quake_pluq_ironwail`)
- Same synchronization protocol
- Cross-language communication possible (C backend, C# frontend, or vice versa)

## Limitations

1. **Local Only**: Cannot communicate across network
2. **Single Writer**: Only one backend can write state
3. **No Message Queue**: Latest state overwrites previous
4. **Fixed Structure**: Binary compatibility required
5. **Platform-Specific**: Requires OS shared memory support

## Comparison to Other IPC Mechanisms

### vs Named Pipes
- **PluQ Advantage**: Much lower latency, zero-copy
- **Pipes Advantage**: Message-oriented, works across network (on Windows)

### vs Unix Domain Sockets
- **PluQ Advantage**: Lower latency, zero-copy
- **Sockets Advantage**: Message-oriented, more flexible

### vs ZeroMQ (detailed)
- **PluQ Advantage**: Ultra-low latency, simpler for local state broadcasting
- **ZeroMQ Advantage**: Network support, flexible patterns, message queuing, language bindings

### vs gRPC
- **PluQ Advantage**: Much lower latency, no serialization
- **gRPC Advantage**: Network support, schema evolution, many languages

## When to Choose PluQ

Choose PluQ if you need:
- ✅ Ultra-low latency (microseconds)
- ✅ Large state transfers (thousands of entities)
- ✅ Local-only communication
- ✅ Simple state broadcasting pattern
- ✅ Minimal dependencies

Choose something else (ZeroMQ, gRPC, etc.) if you need:
- ❌ Network communication
- ❌ Message queuing
- ❌ Multiple messaging patterns
- ❌ Dynamic service discovery
- ❌ Language-agnostic protocols

## License

GPL v2 or later (same as PluQuake/Ironwail)

## Credits

- Original C implementation: QuakeSpasm/Ironwail developers
- C# port: Based on the C implementation from PluQuake

## See Also

- [Original C Implementation](../c/PLUQ_FRONTEND_README.md)
- [ZeroMQ Documentation](https://zeromq.org/)
- [.NET MemoryMappedFile](https://docs.microsoft.com/en-us/dotnet/api/system.io.memorymappedfiles.memorymappedfile)
