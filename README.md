# PluQ - Inter-Process Communication System

Extracted from [PluQuake/Ironwail](https://github.com/ManuelKugelmann/PluQuake/tree/PluQuakespasm), PluQ is a high-performance, shared memory-based IPC system designed for game engine frontend/backend separation.

## Project Structure

```
PluQ/
├── c/                              # C implementation (original)
│   ├── pluq.h                      # Core header with data structures
│   ├── pluq.c                      # Core IPC implementation (~794 lines)
│   ├── main_pluq_frontend.c        # Frontend entry point
│   ├── host_pluq_frontend.c        # Frontend host initialization
│   ├── stubs_pluq_frontend.c       # Frontend stub functions
│   ├── Makefile.pluq_frontend      # Build configuration
│   └── PLUQ_FRONTEND_README.md     # Original documentation
│
├── csharp/                         # C# implementation (compatible)
│   ├── PluQStructures.cs           # Data structures (binary-compatible)
│   ├── PluQ.cs                     # Core IPC implementation
│   ├── PluQBackendExample.cs       # Backend example
│   ├── PluQFrontendExample.cs      # Frontend example
│   ├── PluQ.csproj                 # Library project
│   ├── PluQBackendExample.csproj   # Backend example project
│   ├── PluQFrontendExample.csproj  # Frontend example project
│   └── README.md                   # C# documentation & ZeroMQ comparison
│
└── README.md                       # This file
```

## What is PluQ?

PluQ is a **shared memory-based IPC system** that enables:

- **Backend Process**: Runs game simulation (physics, AI, server logic)
- **Frontend Process(es)**: Handles rendering, input, and audio
- **Bidirectional Communication**: Backend broadcasts state, frontend sends input
- **Ultra-Low Latency**: ~1-10 microseconds (local machine)
- **Zero-Copy Transfer**: Direct memory access, no serialization

## IPC Mechanism

### What PluQ Uses

PluQ uses **platform-specific shared memory**:

- **Windows**: `CreateFileMapping` / `MapViewOfFile` (Win32 API)
- **Linux/Unix**: `shm_open` + `mmap` (POSIX)
- **C#**: `MemoryMappedFile` (.NET API)

### Is it Versatile Like ZeroMQ?

**No.** PluQ is purpose-built for a specific use case (local game engine IPC), while ZeroMQ is a general-purpose messaging library.

#### Quick Comparison

| Feature | PluQ | ZeroMQ |
|---------|------|---------|
| **Transport** | Shared memory (local only) | TCP, IPC, multicast, etc. |
| **Latency** | Ultra-low (~1-10 μs) | Low (~50-500 μs) |
| **Network** | No | Yes |
| **Patterns** | State broadcast + input | Pub/Sub, Req/Rep, Push/Pull, etc. |
| **Use Case** | Local game IPC | Distributed systems |

See [csharp/README.md](csharp/README.md) for detailed comparison.

## Key Features

### 1. Ultra-Low Latency
- Direct memory access (no sockets, no serialization)
- Lock-free synchronization (atomic flags)
- Zero-copy transfer

### 2. High Throughput
- Up to 8,192 entities per frame
- 60+ state updates per second
- ~24 MB shared memory region

### 3. Platform Support
- Linux/Unix (POSIX shared memory)
- Windows (File mapping)
- Cross-platform C# (.NET MemoryMappedFile)

### 4. Binary Compatibility
- C and C# implementations are compatible
- Can mix C backend with C# frontend (or vice versa)
- Same structure layout and synchronization protocol

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Backend Process                          │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Game Logic                                           │  │
│  │  • Physics simulation                                 │  │
│  │  • AI processing                                      │  │
│  │  • Server logic                                       │  │
│  │  • QuakeC VM                                          │  │
│  └─────────────┬─────────────────────────────────────────┘  │
│                │                                             │
│                │ PluQ_BroadcastWorldState()                  │
│                ▼                                             │
│  ┌─────────────────────────────────────────────────────┐    │
│  │         Shared Memory (~24 MB)                      │    │
│  │  ┌───────────────────────────────────────────────┐  │    │
│  │  │ • Frame sequence (atomic)                     │  │    │
│  │  │ • Frame header (player, game state)           │  │    │
│  │  │ • Entities array [0..8191]                    │  │    │
│  │  │ • Dynamic lights [0..31]                      │  │    │
│  │  │ • Input command (movement, view, buttons)     │  │    │
│  │  └───────────────────────────────────────────────┘  │    │
│  └───────────▲───────────────────────────────────────────┘  │
│              │                                               │
│              │ PluQ_ReceiveInput()                           │
└──────────────┼───────────────────────────────────────────────┘
               │
               │
┌──────────────┼───────────────────────────────────────────────┐
│              │                                               │
│              │ PluQ_SendInput()                              │
│  ┌───────────┴───────────────────────────────────────────┐  │
│  │         Frontend Process                              │  │
│  │  • Input handling (keyboard, mouse, gamepad)          │  │
│  │  • OpenGL rendering                                   │  │
│  │  • Audio output                                       │  │
│  │  • UI/Menu system                                     │  │
│  │  • No simulation (receives state from backend)        │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                              │
│                    Frontend Process                          │
└──────────────────────────────────────────────────────────────┘
```

## Quick Start

### C Implementation

```bash
cd c/
# Build frontend (requires full Quake/Ironwail source)
make -f Makefile.pluq_frontend

# Run backend
./ironwail +pluq_mode backend

# Run frontend (in another terminal)
./ironwail_frontend +pluq_mode frontend
```

### C# Implementation

```bash
cd csharp/

# Terminal 1: Run backend
dotnet run --project PluQBackendExample.csproj

# Terminal 2: Run frontend
dotnet run --project PluQFrontendExample.csproj
```

## Performance

Based on the original C implementation:

- **Latency**: 1-10 microseconds (local)
- **Throughput**: 60+ frames/second with 8,192 entities
- **Memory**: ~24 MB shared memory region
- **CPU**: Minimal overhead (no locks, no serialization)

## Use Cases

### ✅ Good For:

- Local game engine separation (simulation vs rendering)
- Headless server with separate rendering clients
- Development tools (game state visualization)
- Testing/debugging (record/replay game state)
- Performance profiling (isolate rendering from simulation)

### ❌ Not Good For:

- Network multiplayer (use sockets, ZeroMQ, etc.)
- Cross-machine communication
- Message queuing with history
- Dynamic service discovery
- General-purpose messaging

## Comparison to Other IPC

### vs ZeroMQ
- **PluQ**: Ultra-low latency, local only, state broadcasting
- **ZeroMQ**: Network support, flexible patterns, message queuing

### vs gRPC
- **PluQ**: No serialization, zero-copy, microsecond latency
- **gRPC**: Network support, schema evolution, many languages

### vs Named Pipes
- **PluQ**: Lower latency, zero-copy
- **Pipes**: Message-oriented, simpler API

See [csharp/README.md](csharp/README.md) for detailed comparisons.

## Technical Details

### Shared Memory Layout

```
Offset    Size      Field
------    ----      -----
0         4         frame_sequence (volatile)
4         4         write_in_progress (volatile)
8         ~120      frame_header
128       ~500KB    entities[8192]
~500KB    ~1KB      dlights[32]
~501KB    ~300      input_cmd
~501KB    4         input_ready (volatile)
```

### Synchronization Protocol

**Backend Write:**
1. Set `write_in_progress = 1`
2. Update frame header
3. Copy entities array
4. Copy dynamic lights
5. Increment `frame_sequence++`
6. Set `write_in_progress = 0`

**Frontend Read:**
1. Check `write_in_progress == 0`
2. Check `frame_sequence != last_sequence`
3. Read frame header
4. Read entities
5. Read dynamic lights
6. Update `last_sequence`

### Platform-Specific Details

**Linux/Unix (C):**
```c
shm_fd = shm_open("/quake_pluq_ironwail", O_CREAT | O_RDWR, 0666);
ftruncate(shm_fd, sizeof(pluq_shared_memory_t));
ptr = mmap(NULL, size, PROT_READ | PROT_WRITE, MAP_SHARED, shm_fd, 0);
```

**Windows (C):**
```c
hMapFile = CreateFileMapping(INVALID_HANDLE_VALUE, NULL,
    PAGE_READWRITE, 0, size, "Local\\quake_pluq_ironwail");
ptr = MapViewOfFile(hMapFile, FILE_MAP_ALL_ACCESS, 0, 0, size);
```

**C# (.NET):**
```csharp
_sharedMemory = MemoryMappedFile.CreateOrOpen(
    "quake_pluq_ironwail", size, MemoryMappedFileAccess.ReadWrite);
_accessor = _sharedMemory.CreateViewAccessor();
```

## Documentation

- [C Implementation Details](c/PLUQ_FRONTEND_README.md)
- [C# Implementation & ZeroMQ Comparison](csharp/README.md)
- [Original PluQuake Repository](https://github.com/ManuelKugelmann/PluQuake)

## License

GPL v2 or later (inherited from PluQuake/Ironwail)

## Credits

- **Original Implementation**: QuakeSpasm/Ironwail developers
- **Source**: [PluQuake](https://github.com/ManuelKugelmann/PluQuake/tree/PluQuakespasm) by ManuelKugelmann
- **C# Port**: Based on the C implementation

## See Also

- [ZeroMQ](https://zeromq.org/) - General-purpose messaging library
- [gRPC](https://grpc.io/) - High-performance RPC framework
- [Quake Engine](https://github.com/id-Software/Quake) - Original Quake source
- [QuakeSpasm](http://quakespasm.sourceforge.net/) - Quake engine port
- [Ironwail](https://github.com/andrei-drexler/ironwail) - QuakeSpasm fork
