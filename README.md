# PluQ - Inter-Process Communication System

Extracted from [PluQuake/Ironwail](https://github.com/ManuelKugelmann/PluQuake/tree/PluQuakespasm), PluQ provides **two IPC implementations** for game engine frontend/backend separation:

1. **Shared Memory** - Ultra-low latency, local only
2. **nng + FlatBuffers** - Network-capable, flexible messaging

## Project Structure

```
PluQ/
├── c/                                    # C implementations
│   ├── pluq.h                            # Shared memory API
│   ├── pluq.c                            # Shared memory implementation (~794 lines)
│   ├── pluq_nng.h                        # nng + FlatBuffers API
│   ├── pluq_nng.c                        # nng implementation
│   ├── pluq.fbs                          # FlatBuffers schema
│   ├── CMakeLists.txt                    # Build configuration
│   ├── build.sh                          # Build script
│   ├── PLUQ_FRONTEND_README.md           # Shared memory docs
│   ├── PLUQ_HYBRID_RESOURCES.md          # Hybrid resource loading
│   ├── README.md                         # C implementation guide
│   └── (Quake integration files)         # Frontend entry points, stubs, etc.
│
├── csharp/                               # C# implementations
│   ├── PluQStructures.cs                 # Shared memory data structures
│   ├── PluQ.cs                           # Shared memory implementation
│   ├── PluQNNG.cs                        # nng + FlatBuffers implementation
│   ├── PluQ.csproj                       # Shared memory library
│   ├── PluQNNG.csproj                    # nng library (with dependencies)
│   ├── PluQBackendExample.cs             # Shared memory backend example
│   ├── PluQFrontendExample.cs            # Shared memory frontend example
│   ├── generate_flatbuffers.sh           # FlatBuffers code generation
│   ├── FLATBUFFERS.md                    # FlatBuffers guide
│   └── README.md                         # C# documentation & comparisons
│
└── README.md                             # This file
```

## What is PluQ?

PluQ is an **IPC system** for game engine frontend/backend separation:

- **Backend Process**: Runs game simulation (physics, AI, server logic)
- **Frontend Process(es)**: Handles rendering, input, and audio
- **Bidirectional Communication**: Backend broadcasts state, frontend sends input

## Two IPC Implementations

### 1. Shared Memory (Ultra-Low Latency)

**Technology:**
- **Windows**: `CreateFileMapping` / `MapViewOfFile` (Win32 API)
- **Linux/Unix**: `shm_open` + `mmap` (POSIX)
- **C#**: `MemoryMappedFile` (.NET API)

**Characteristics:**
- **Latency**: ~1-10 microseconds
- **Scope**: Local machine only
- **Serialization**: None (zero-copy)
- **Memory**: ~24 MB shared region

### 2. nng + FlatBuffers (Network-Capable)

**Technology:**
- **Transport**: nng (IPC, TCP, WebSocket)
- **Serialization**: FlatBuffers (efficient binary)
- **Patterns**: PUB/SUB, REQ/REP, PUSH/PULL

**Characteristics:**
- **Latency**: ~50-500 μs (IPC), 1-10 ms (TCP LAN)
- **Scope**: Local + network
- **Serialization**: FlatBuffers (low overhead)
- **Channels**: 3 separate (Resources, Gameplay, Input)

## Comparison Table

| Feature | Shared Memory | nng + FlatBuffers | ZeroMQ |
|---------|--------------|-------------------|---------|
| **Transport** | Shared memory | IPC/TCP/WebSocket | TCP/IPC/multicast |
| **Latency** | ~1-10 μs | ~50-500 μs (IPC) | ~50-500 μs |
| **Network** | No | Yes | Yes |
| **Serialization** | None (raw structs) | FlatBuffers | Manual |
| **Patterns** | State overwrite | Pub/Sub, Req/Rep, Push/Pull | Pub/Sub, Req/Rep, etc. |
| **Resource Loading** | Fixed buffer | Hybrid (local+remote) | Manual |
| **Dependencies** | OS only | libnng, flatcc | libzmq |
| **Use Case** | Local game IPC | Distributed, web, mobile | General distributed systems |

**Which to use?**
- **Shared Memory**: Local only, need ultra-low latency (<10 μs)
- **nng + FlatBuffers**: Network support, web/mobile frontends, hybrid resource loading
- **ZeroMQ**: General-purpose distributed messaging, many language bindings

See [c/README.md](c/README.md) and [csharp/README.md](csharp/README.md) for detailed comparisons.

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
