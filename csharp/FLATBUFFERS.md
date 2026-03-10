# FlatBuffers Code Generation for C#

This document explains how to generate C# code from the PluQ FlatBuffers schema.

## Prerequisites

Install the FlatBuffers compiler (`flatc`):

**Ubuntu/Debian:**
```bash
sudo apt-get install flatbuffers-compiler
```

**macOS:**
```bash
brew install flatbuffers
```

**Windows:**
Download from: https://github.com/google/flatbuffers/releases

**Manual Build:**
```bash
git clone https://github.com/google/flatbuffers.git
cd flatbuffers
cmake -G "Unix Makefiles"
make
sudo make install
```

## Generate C# Code

```bash
# Generate C# code from schema
flatc --csharp --gen-object-api -o Generated ../c/pluq.fbs
```

This creates `Generated/PluQ/` with:
- `ResourceType.cs` - Enum for resource types
- `Vec3.cs` - Vector3 struct
- `ResourceRequest.cs` - Resource request message
- `MapStart.cs` - Map start message
- `Texture.cs` - Texture data message
- `Model.cs` - Model data message
- `BSPVertices.cs`, `BSPFaces.cs` - BSP geometry
- `Lightmap.cs` - Lightmap data
- `ResourceMessage.cs` - Resource channel message
- `GameplayFrame.cs` - Gameplay channel message
- `Entity.cs` - Entity data
- `InputCommand.cs` - Input channel message

## Options Explained

- `--csharp` - Generate C# code
- `--gen-object-api` - Generate convenient object API (mutable classes)
- `-o Generated` - Output to `Generated/` directory

## Using Generated Code

### Building Messages (Backend)

```csharp
using FlatBuffers;
using PluQ;

// Create GameplayFrame
var fbb = new FlatBufferBuilder(1024);

// Build entities array
var entities = new List<Offset<Entity>>();
for (int i = 0; i < 10; i++)
{
    var entity = Entity.CreateEntity(fbb,
        new Vec3(i * 50, 0, 0),        // origin
        new Vec3(0, 0, 0),             // angles
        1,                              // model_id
        0,                              // frame
        0,                              // colormap
        0,                              // skin
        0,                              // effects
        1.0f                            // alpha
    );
    entities.Add(entity);
}

var entitiesVector = GameplayFrame.CreateEntitiesVector(fbb, entities.ToArray());

// Build frame
var frame = GameplayFrame.CreateGameplayFrame(fbb,
    frameNumber: 123,
    timestamp: DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond,
    viewOrigin: new Vec3(0, 0, 100),
    viewAngles: new Vec3(0, 90, 0),
    health: 100,
    armor: 50,
    weapon: 1,
    ammo: 50,
    paused: false,
    inGame: true,
    entitiesOffset: entitiesVector
);

fbb.Finish(frame.Value);

// Get buffer
byte[] buffer = fbb.SizedByteArray();

// Send via nng
pluqNng.PublishFrame(buffer);
```

### Reading Messages (Frontend)

```csharp
using FlatBuffers;
using PluQ;

// Receive frame
byte[] buffer = pluqNng.ReceiveFrame();
if (buffer != null)
{
    // Parse FlatBuffer
    var bb = new ByteBuffer(buffer);
    var frame = GameplayFrame.GetRootAsGameplayFrame(bb);

    // Read data
    uint frameNum = frame.FrameNumber;
    var viewOrigin = frame.ViewOrigin;
    var viewAngles = frame.ViewAngles;
    short health = frame.Health;

    // Read entities
    for (int i = 0; i < frame.EntitiesLength; i++)
    {
        var entity = frame.Entities(i);
        var origin = entity.Value.Origin;
        var angles = entity.Value.Angles;
        ushort modelId = entity.Value.ModelId;

        // Render entity...
    }
}
```

### Using Object API (Mutable Classes)

With `--gen-object-api`, you can use mutable classes:

```csharp
// Create GameplayFrame (object API)
var frame = new GameplayFrameT
{
    FrameNumber = 123,
    Timestamp = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond,
    ViewOrigin = new Vec3(0, 0, 100),
    ViewAngles = new Vec3(0, 90, 0),
    Health = 100,
    Armor = 50,
    Weapon = 1,
    Ammo = 50,
    Paused = false,
    InGame = true,
    Entities = new List<EntityT>
    {
        new EntityT
        {
            Origin = new Vec3(0, 0, 0),
            Angles = new Vec3(0, 0, 0),
            ModelId = 1,
            Frame = 0,
            Alpha = 1.0f
        }
    }
};

// Serialize
var fbb = new FlatBufferBuilder(1024);
var offset = GameplayFrame.Pack(fbb, frame);
fbb.Finish(offset.Value);
byte[] buffer = fbb.SizedByteArray();

// Deserialize
var bb = new ByteBuffer(buffer);
var receivedFrame = GameplayFrame.GetRootAsGameplayFrame(bb).UnPack();

// Access as normal C# objects
uint frameNum = receivedFrame.FrameNumber;
foreach (var entity in receivedFrame.Entities)
{
    Console.WriteLine($"Entity at {entity.Origin.X}, {entity.Origin.Y}, {entity.Origin.Z}");
}
```

## NuGet Packages

Add to your .csproj:

```xml
<ItemGroup>
  <PackageReference Include="Google.FlatBuffers" Version="24.3.25" />
  <PackageReference Include="nng.NETCore" Version="1.4.2" />
  <PackageReference Include="Subor.nng.NETCore" Version="1.4.2" />
</ItemGroup>
```

## Build Script

Create `generate_flatbuffers.sh`:

```bash
#!/bin/bash
flatc --csharp --gen-object-api -o Generated ../c/pluq.fbs
echo "FlatBuffers C# code generated in Generated/"
```

Make executable:
```bash
chmod +x generate_flatbuffers.sh
```

## Integration with Project

Update `.csproj` to include generated files:

```xml
<ItemGroup>
  <Compile Include="Generated/PluQ/**/*.cs" />
</ItemGroup>
```

Or manually add:
```xml
<ItemGroup>
  <Compile Include="Generated/PluQ/ResourceType.cs" />
  <Compile Include="Generated/PluQ/Vec3.cs" />
  <Compile Include="Generated/PluQ/GameplayFrame.cs" />
  <Compile Include="Generated/PluQ/Entity.cs" />
  <Compile Include="Generated/PluQ/InputCommand.cs" />
  <Compile Include="Generated/PluQ/ResourceMessage.cs" />
  <!-- ... add all generated files ... -->
</ItemGroup>
```

## Verification

Verify generation worked:

```bash
ls -lh Generated/PluQ/
```

Expected output:
```
ResourceType.cs
Vec3.cs
Entity.cs
GameplayFrame.cs
InputCommand.cs
ResourceMessage.cs
MapStart.cs
Texture.cs
Model.cs
...
```

## Troubleshooting

**Error: "flatc: command not found"**
- Install FlatBuffers compiler (see Prerequisites)

**Error: "No such file or directory: pluq.fbs"**
- Check path to schema file: `../c/pluq.fbs`

**Error: "unable to locate include file: ..."**
- FlatBuffers doesn't support includes - all types must be in one file

**Warning: "file exists, not overwriting"**
- Delete `Generated/` directory and regenerate

## Resources

- FlatBuffers Documentation: https://google.github.io/flatbuffers/
- C# API Reference: https://google.github.io/flatbuffers/flatbuffers_guide_use_c-sharp.html
- nng.NETCore: https://github.com/subor/nng.NETCore
