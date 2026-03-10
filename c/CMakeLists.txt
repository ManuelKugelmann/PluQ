cmake_minimum_required(VERSION 3.10)
project(PluQ C)

set(CMAKE_C_STANDARD 99)

# ============================================================================
# Dependencies
# ============================================================================

# Find nng library
find_library(NNG_LIBRARY nng REQUIRED)
find_path(NNG_INCLUDE_DIR nng/nng.h REQUIRED)

# FlatCC (FlatBuffers C compiler and runtime)
find_program(FLATCC flatcc)
if(NOT FLATCC)
    message(WARNING "flatcc not found. You'll need to generate FlatBuffers code manually.")
endif()

# ============================================================================
# Generate FlatBuffers Code
# ============================================================================

set(FBS_SCHEMA ${CMAKE_CURRENT_SOURCE_DIR}/pluq.fbs)
set(FBS_GENERATED_DIR ${CMAKE_CURRENT_BINARY_DIR}/generated)

if(FLATCC)
    add_custom_command(
        OUTPUT ${FBS_GENERATED_DIR}/pluq_builder.h
               ${FBS_GENERATED_DIR}/pluq_reader.h
               ${FBS_GENERATED_DIR}/pluq_verifier.h
        COMMAND ${CMAKE_COMMAND} -E make_directory ${FBS_GENERATED_DIR}
        COMMAND ${FLATCC} -a -o ${FBS_GENERATED_DIR} ${FBS_SCHEMA}
        DEPENDS ${FBS_SCHEMA}
        COMMENT "Generating FlatBuffers C code from pluq.fbs"
    )

    add_custom_target(generate_flatbuffers
        DEPENDS ${FBS_GENERATED_DIR}/pluq_builder.h
                ${FBS_GENERATED_DIR}/pluq_reader.h
                ${FBS_GENERATED_DIR}/pluq_verifier.h
    )
else()
    # Assume generated files already exist
    add_custom_target(generate_flatbuffers)
endif()

# ============================================================================
# PluQ Library (Shared Memory Version)
# ============================================================================

add_library(pluq STATIC
    pluq.c
    pluq.h
)

target_include_directories(pluq PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}
)

# ============================================================================
# PluQ NNG Library (nng + FlatBuffers Version)
# ============================================================================

add_library(pluq_nng STATIC
    pluq_nng.c
    pluq_nng.h
)

target_include_directories(pluq_nng PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${NNG_INCLUDE_DIR}
    ${FBS_GENERATED_DIR}
)

target_link_libraries(pluq_nng
    ${NNG_LIBRARY}
)

add_dependencies(pluq_nng generate_flatbuffers)

# ============================================================================
# Example Programs
# ============================================================================

# Example: Shared memory backend
add_executable(pluq_backend_example
    examples/backend_example.c
)

target_link_libraries(pluq_backend_example
    pluq
)

# Example: Shared memory frontend
add_executable(pluq_frontend_example
    examples/frontend_example.c
)

target_link_libraries(pluq_frontend_example
    pluq
)

# Example: NNG backend
add_executable(pluq_nng_backend_example
    examples/nng_backend_example.c
)

target_link_libraries(pluq_nng_backend_example
    pluq_nng
)

# Example: NNG frontend
add_executable(pluq_nng_frontend_example
    examples/nng_frontend_example.c
)

target_link_libraries(pluq_nng_frontend_example
    pluq_nng
)

# ============================================================================
# Installation
# ============================================================================

install(TARGETS pluq pluq_nng
    ARCHIVE DESTINATION lib
)

install(FILES
    pluq.h
    pluq_nng.h
    DESTINATION include/pluq
)

# ============================================================================
# Build Instructions
# ============================================================================

# To build:
#   mkdir build && cd build
#   cmake ..
#   make
#
# Dependencies (Ubuntu/Debian):
#   sudo apt-get install libnng-dev flatcc
#
# Dependencies (macOS):
#   brew install nng flatcc
#
# Manual FlatBuffers generation (if flatcc not found):
#   flatcc -a -o generated pluq.fbs
