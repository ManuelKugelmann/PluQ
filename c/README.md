# PluQ IPC - C Implementation

Two IPC implementations for game engine frontend/backend separation:

1. **Shared Memory** (`pluq.c/h`) - Ultra-low latency, local only
2. **nng + FlatBuffers** (`pluq_nng.c/h`) - Network-capable, flexible

## Quick Comparison

| Feature | Shared Memory | nng + FlatBuffers |
|---------|--------------|-------------------|
| **Transport** | POSIX shared memory | nng (IPC/TCP/WebSocket) |
| **Latency** | ~1-10 μs | ~50-500 μs (local) |
| **Network** | Local only | Yes (TCP mode) |
| **Serialization** | None (raw structs) | FlatBuffers |
| **Messaging** | State overwrite | Pub/Sub, Req/Rep, Push/Pull |
| **Resource Streaming** | Fixed buffer | Hybrid (local + remote) |
| **Dependencies** | OS only (no libs) | libnng, flatcc |
| **Use Case** | Local game IPC | Distributed, web, mobile |

## Architecture

### Shared Memory Version

```
Backend ←→ Shared Memory ←→ Frontend
         (24 MB region)
```

- Single shared memory region
- Backend writes state, frontend reads
- Frontend writes input, backend reads
- Lock-free synchronization (atomic flags)

### nng + FlatBuffers Version

```
Backend ←── Resources ──→ Frontend  (REQ/REP)
        ──→ Gameplay ───→            (PUB/SUB)
        ←── Input ──────              (PUSH/PULL)
```

**Three Channels:**

1. **Resources** (REQ/REP): Frontend requests textures/models, backend replies
2. **Gameplay** (PUB/SUB): Backend publishes entity state, frontend subscribes
3. **Input** (PUSH/PULL): Frontend pushes input, backend pulls

## Building

### Dependencies

**Ubuntu/Debian:**
```bash
sudo apt-get install libnng-dev flatcc
```

**macOS:**
```bash
brew install nng flatcc
```

**Manual:**
- nng: https://github.com/nanomsg/nng
- flatcc: https://github.com/dvidelabs/flatcc

### Build

```bash
# Generate FlatBuffers code and build
./build.sh

# Or manually with CMake
mkdir build && cd build
cmake ..
make
```

### Generate FlatBuffers Code Only

```bash
flatcc -a -o generated pluq.fbs
```

This creates:
- `generated/pluq_builder.h` - For building messages
- `generated/pluq_reader.h` - For reading messages
- `generated/pluq_verifier.h` - For verifying messages

## Usage

### Shared Memory Version

```c
#include "pluq.h"

// Backend
PluQ_Initialize(PLUQ_MODE_BACKEND);

// Game loop
while (running) {
    // Broadcast state
    PluQ_BroadcastWorldState();

    // Process input
    if (PluQ_HasPendingInput()) {
        PluQ_ProcessInputCommands();
    }
}

PluQ_Shutdown();
```

```c
// Frontend
PluQ_Initialize(PLUQ_MODE_FRONTEND);

// Render loop
while (running) {
    // Receive state
    if (PluQ_ReceiveWorldState()) {
        PluQ_ApplyReceivedState();
        RenderScene();
    }

    // Send input
    PluQ_SendInput(&cmd);
}

PluQ_Shutdown();
```

### nng + FlatBuffers Version

```c
#include "pluq_nng.h"
#include "generated/pluq_builder.h"
#include "generated/pluq_reader.h"

// Backend
PluQ_NNG_Init(true);  // true = backend

// Game loop
while (running) {
    // Build FlatBuffer GameplayFrame
    flatcc_builder_t builder;
    flatcc_builder_init(&builder);

    PluQ_GameplayFrame_start(&builder);
    PluQ_GameplayFrame_frame_number_add(&builder, frame_num);
    // ... add more fields ...
    PluQ_GameplayFrame_end(&builder);

    void *buf = flatcc_builder_finalize_buffer(&builder, &size);

    // Publish frame
    PluQ_NNG_Backend_PublishFrame(buf, size);

    free(buf);
    flatcc_builder_clear(&builder);

    // Check for input
    void *input_buf;
    size_t input_size;
    if (PluQ_NNG_Backend_ReceiveInput(&input_buf, &input_size)) {
        // Parse FlatBuffer InputCommand
        PluQ_InputCommand_table_t input = PluQ_InputCommand_as_root(input_buf);
        float forward = PluQ_InputCommand_forward_move(input);
        // ... process input ...
        nng_free(input_buf, input_size);
    }
}

PluQ_NNG_Shutdown();
```

```c
// Frontend
PluQ_NNG_Init(false);  // false = frontend

// Render loop
while (running) {
    // Receive frame
    void *buf;
    size_t size;
    if (PluQ_NNG_Frontend_ReceiveFrame(&buf, &size)) {
        // Parse FlatBuffer GameplayFrame
        PluQ_GameplayFrame_table_t frame = PluQ_GameplayFrame_as_root(buf);
        uint32_t frame_num = PluQ_GameplayFrame_frame_number(frame);
        // ... render scene ...
        nng_free(buf, size);
    }

    // Send input
    flatcc_builder_t builder;
    flatcc_builder_init(&builder);

    PluQ_InputCommand_start(&builder);
    PluQ_InputCommand_forward_move_add(&builder, 200.0f);
    // ... add more fields ...
    PluQ_InputCommand_end(&builder);

    void *input_buf = flatcc_builder_finalize_buffer(&builder, &input_size);
    PluQ_NNG_Frontend_SendInput(input_buf, input_size);

    free(input_buf);
    flatcc_builder_clear(&builder);
}

PluQ_NNG_Shutdown();
```

## FlatBuffers Schema

The `pluq.fbs` schema defines three message types:

### 1. ResourceMessage (Resources Channel)

```protobuf
table ResourceMessage {
  type: ResourceType;
  data: ResourceData;  // Union: MapStart, Texture, Model, BSP, Lightmap
}
```

Used for hybrid resource loading - frontend can load locally or request from backend.

### 2. GameplayFrame (Gameplay Channel)

```protobuf
table GameplayFrame {
  frame_number: uint32;
  entities: [Entity];
  view_origin: Vec3;
  view_angles: Vec3;
  health: int16;
  // ...
}
```

Broadcast every frame with game state.

### 3. InputCommand (Input Channel)

```protobuf
table InputCommand {
  forward_move: float;
  side_move: float;
  view_angles: Vec3;
  buttons: uint32;
  // ...
}
```

Sent from frontend to backend with user input.

## Network Modes

nng supports multiple transports by changing URLs:

**IPC (local, fastest):**
```c
#define PLUQ_URL_GAMEPLAY "ipc:///tmp/quake_pluq_gameplay"
```

**TCP (network, LAN/WAN):**
```c
#define PLUQ_URL_GAMEPLAY "tcp://192.168.1.100:5555"
```

**WebSocket (browser frontends):**
```c
#define PLUQ_URL_GAMEPLAY "ws://localhost:5555/gameplay"
```

## Hybrid Resource Loading

The nng version supports **hybrid resource loading**:

1. **Backend sends resource list** (MapStart message)
2. **Frontend chooses** how to load:
   - Load from local pak files (fast)
   - Request from backend (portable)
   - Mix both (cache + on-demand)

Benefits:
- **99.98% bandwidth reduction** with local loading
- **Portable frontends** (no pak files needed)
- **Progressive optimization** (start remote, optimize to local)

See [PLUQ_HYBRID_RESOURCES.md](PLUQ_HYBRID_RESOURCES.md) for details.

## Performance

### Shared Memory

- **Latency**: 1-10 μs (local)
- **Throughput**: 60+ FPS with 8,192 entities
- **Memory**: ~24 MB shared region
- **CPU**: Minimal (no serialization, no syscalls)

### nng + FlatBuffers

- **Latency**:
  - IPC mode: 50-500 μs (local)
  - TCP mode: 1-10 ms (LAN), 50-200 ms (WAN)
- **Throughput**: 60+ FPS with ~10KB/frame
- **Memory**: Dynamic (message buffers)
- **CPU**: Low (efficient FlatBuffers serialization)

## When to Use Each

### Use Shared Memory When:

✅ Local machine only (same computer)
✅ Need ultra-low latency (<10 μs)
✅ Large state transfers (8K+ entities)
✅ Minimal dependencies
✅ Simple deployment

### Use nng + FlatBuffers When:

✅ Network communication (different machines)
✅ Web/mobile frontends
✅ Flexible messaging patterns
✅ Hybrid resource loading
✅ Multiple frontend types
✅ Browser-based clients (WebSocket)

## Files

```
c/
├── pluq.h                      # Shared memory API
├── pluq.c                      # Shared memory implementation
├── pluq_nng.h                  # nng + FlatBuffers API
├── pluq_nng.c                  # nng implementation
├── pluq.fbs                    # FlatBuffers schema
├── CMakeLists.txt              # Build configuration
├── build.sh                    # Build script
├── PLUQ_FRONTEND_README.md     # Original shared memory docs
├── PLUQ_HYBRID_RESOURCES.md    # Hybrid loading architecture
└── README.md                   # This file
```

## Examples

See the `examples/` directory for:
- `backend_example.c` - Shared memory backend
- `frontend_example.c` - Shared memory frontend
- `nng_backend_example.c` - nng backend
- `nng_frontend_example.c` - nng frontend

## Resources

- **nng Documentation**: https://nng.nanomsg.org/
- **FlatBuffers**: https://google.github.io/flatbuffers/
- **flatcc (C)**: https://github.com/dvidelabs/flatcc
- **Original PluQuake**: https://github.com/ManuelKugelmann/PluQuake

## License

GPL v2 or later (same as PluQuake/Ironwail)
