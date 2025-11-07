#!/bin/bash
# Generate C# code from PluQ FlatBuffers schema

set -e

echo "========================================="
echo "PluQ FlatBuffers C# Code Generation"
echo "========================================="

# Check for flatc
if ! command -v flatc &> /dev/null; then
    echo "ERROR: flatc (FlatBuffers compiler) not found!"
    echo ""
    echo "Install with:"
    echo "  Ubuntu/Debian: sudo apt-get install flatbuffers-compiler"
    echo "  macOS: brew install flatbuffers"
    echo "  Manual: https://github.com/google/flatbuffers/releases"
    exit 1
fi

# Check for schema file
SCHEMA_FILE="../c/pluq.fbs"
if [ ! -f "$SCHEMA_FILE" ]; then
    echo "ERROR: Schema file not found: $SCHEMA_FILE"
    exit 1
fi

# Create output directory
mkdir -p Generated

# Generate C# code
echo "Generating C# code from $SCHEMA_FILE..."
flatc --csharp --gen-object-api -o Generated "$SCHEMA_FILE"

echo ""
echo "========================================="
echo "Generation complete!"
echo "========================================="
echo "Generated files:"
ls -lh Generated/PluQ/ 2>/dev/null || ls -lh Generated/

echo ""
echo "Files generated in: Generated/PluQ/"
echo ""
echo "Add these to your .csproj:"
echo '  <Compile Include="Generated/PluQ/**/*.cs" />'
