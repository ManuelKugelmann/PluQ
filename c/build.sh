#!/bin/bash
# Build script for PluQ IPC library

set -e

echo "========================================="
echo "PluQ Build Script"
echo "========================================="

# Check for dependencies
echo "Checking dependencies..."

# Check for flatcc
if ! command -v flatcc &> /dev/null; then
    echo "WARNING: flatcc not found!"
    echo "Install with:"
    echo "  Ubuntu/Debian: sudo apt-get install flatcc"
    echo "  macOS: brew install flatcc"
    echo "  Manual: https://github.com/dvidelabs/flatcc"
    echo ""
fi

# Check for nng
if ! pkg-config --exists nng 2>/dev/null; then
    echo "WARNING: nng library not found!"
    echo "Install with:"
    echo "  Ubuntu/Debian: sudo apt-get install libnng-dev"
    echo "  macOS: brew install nng"
    echo "  Manual: https://github.com/nanomsg/nng"
    echo ""
fi

# Generate FlatBuffers code
echo "========================================="
echo "Generating FlatBuffers C code..."
echo "========================================="

if command -v flatcc &> /dev/null; then
    mkdir -p generated
    flatcc -a -o generated pluq.fbs
    echo "FlatBuffers code generated in generated/"
    ls -lh generated/
else
    echo "Skipping FlatBuffers generation (flatcc not found)"
fi

# Build with CMake
echo ""
echo "========================================="
echo "Building with CMake..."
echo "========================================="

mkdir -p build
cd build
cmake ..
make -j$(nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo 4)

echo ""
echo "========================================="
echo "Build complete!"
echo "========================================="
echo "Libraries:"
ls -lh libpluq*.a 2>/dev/null || true
echo ""
echo "Examples:"
ls -lh *example 2>/dev/null || true
