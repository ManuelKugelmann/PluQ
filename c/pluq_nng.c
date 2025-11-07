/*
Copyright (C) 2024 QuakeSpasm/Ironwail developers

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.
*/

// pluq_nng.c -- PluQ nng + FlatBuffers IPC Implementation

#include "pluq_nng.h"
#include <stdlib.h>
#include <string.h>
#include <stdio.h>

// Global context
static pluq_nng_context_t g_pluq_nng;

// ============================================================================
// INITIALIZATION / SHUTDOWN
// ============================================================================

qboolean PluQ_NNG_Init(qboolean is_backend)
{
	int rv;

	memset(&g_pluq_nng, 0, sizeof(g_pluq_nng));
	g_pluq_nng.is_backend = is_backend;
	g_pluq_nng.is_frontend = !is_backend;

	if (is_backend)
	{
		// Backend: Setup reply, publish, and pull sockets

		// Resources channel: REP socket (reply to resource requests)
		rv = nng_rep0_open(&g_pluq_nng.resources_rep);
		if (rv != 0) {
			fprintf(stderr, "PluQ_NNG: Failed to open resources REP socket: %s\n", nng_strerror(rv));
			goto error;
		}
		rv = nng_listen(g_pluq_nng.resources_rep, PLUQ_URL_RESOURCES, NULL, 0);
		if (rv != 0) {
			fprintf(stderr, "PluQ_NNG: Failed to listen on resources socket: %s\n", nng_strerror(rv));
			goto error;
		}

		// Gameplay channel: PUB socket (publish game frames)
		rv = nng_pub0_open(&g_pluq_nng.gameplay_pub);
		if (rv != 0) {
			fprintf(stderr, "PluQ_NNG: Failed to open gameplay PUB socket: %s\n", nng_strerror(rv));
			goto error;
		}
		rv = nng_listen(g_pluq_nng.gameplay_pub, PLUQ_URL_GAMEPLAY, NULL, 0);
		if (rv != 0) {
			fprintf(stderr, "PluQ_NNG: Failed to listen on gameplay socket: %s\n", nng_strerror(rv));
			goto error;
		}

		// Input channel: PULL socket (receive input commands)
		rv = nng_pull0_open(&g_pluq_nng.input_pull);
		if (rv != 0) {
			fprintf(stderr, "PluQ_NNG: Failed to open input PULL socket: %s\n", nng_strerror(rv));
			goto error;
		}
		rv = nng_listen(g_pluq_nng.input_pull, PLUQ_URL_INPUT, NULL, 0);
		if (rv != 0) {
			fprintf(stderr, "PluQ_NNG: Failed to listen on input socket: %s\n", nng_strerror(rv));
			goto error;
		}

		printf("PluQ_NNG: Backend initialized\n");
		printf("  Resources: %s (REP)\n", PLUQ_URL_RESOURCES);
		printf("  Gameplay:  %s (PUB)\n", PLUQ_URL_GAMEPLAY);
		printf("  Input:     %s (PULL)\n", PLUQ_URL_INPUT);
	}
	else
	{
		// Frontend: Setup request, subscribe, and push sockets

		// Resources channel: REQ socket (request resources)
		rv = nng_req0_open(&g_pluq_nng.resources_req);
		if (rv != 0) {
			fprintf(stderr, "PluQ_NNG: Failed to open resources REQ socket: %s\n", nng_strerror(rv));
			goto error;
		}
		rv = nng_dial(g_pluq_nng.resources_req, PLUQ_URL_RESOURCES, NULL, 0);
		if (rv != 0) {
			fprintf(stderr, "PluQ_NNG: Failed to dial resources socket: %s\n", nng_strerror(rv));
			goto error;
		}

		// Gameplay channel: SUB socket (subscribe to game frames)
		rv = nng_sub0_open(&g_pluq_nng.gameplay_sub);
		if (rv != 0) {
			fprintf(stderr, "PluQ_NNG: Failed to open gameplay SUB socket: %s\n", nng_strerror(rv));
			goto error;
		}
		// Subscribe to all topics (empty topic = all messages)
		rv = nng_socket_set(g_pluq_nng.gameplay_sub, NNG_OPT_SUB_SUBSCRIBE, "", 0);
		if (rv != 0) {
			fprintf(stderr, "PluQ_NNG: Failed to subscribe to gameplay: %s\n", nng_strerror(rv));
			goto error;
		}
		rv = nng_dial(g_pluq_nng.gameplay_sub, PLUQ_URL_GAMEPLAY, NULL, 0);
		if (rv != 0) {
			fprintf(stderr, "PluQ_NNG: Failed to dial gameplay socket: %s\n", nng_strerror(rv));
			goto error;
		}

		// Input channel: PUSH socket (send input commands)
		rv = nng_push0_open(&g_pluq_nng.input_push);
		if (rv != 0) {
			fprintf(stderr, "PluQ_NNG: Failed to open input PUSH socket: %s\n", nng_strerror(rv));
			goto error;
		}
		rv = nng_dial(g_pluq_nng.input_push, PLUQ_URL_INPUT, NULL, 0);
		if (rv != 0) {
			fprintf(stderr, "PluQ_NNG: Failed to dial input socket: %s\n", nng_strerror(rv));
			goto error;
		}

		printf("PluQ_NNG: Frontend initialized\n");
		printf("  Resources: %s (REQ)\n", PLUQ_URL_RESOURCES);
		printf("  Gameplay:  %s (SUB)\n", PLUQ_URL_GAMEPLAY);
		printf("  Input:     %s (PUSH)\n", PLUQ_URL_INPUT);
	}

	g_pluq_nng.initialized = true;
	return true;

error:
	PluQ_NNG_Shutdown();
	return false;
}

void PluQ_NNG_Shutdown(void)
{
	if (!g_pluq_nng.initialized)
		return;

	if (g_pluq_nng.is_backend)
	{
		nng_close(g_pluq_nng.resources_rep);
		nng_close(g_pluq_nng.gameplay_pub);
		nng_close(g_pluq_nng.input_pull);
	}
	else
	{
		nng_close(g_pluq_nng.resources_req);
		nng_close(g_pluq_nng.gameplay_sub);
		nng_close(g_pluq_nng.input_push);
	}

	memset(&g_pluq_nng, 0, sizeof(g_pluq_nng));
	printf("PluQ_NNG: Shutdown complete\n");
}

// ============================================================================
// RESOURCES CHANNEL (REQ/REP)
// ============================================================================

qboolean PluQ_NNG_Backend_WaitForResourceRequest(void)
{
	if (!g_pluq_nng.initialized || !g_pluq_nng.is_backend)
		return false;

	// This is handled by backend reply logic
	// In practice, backend would call ReceiveRequest + SendResource
	return true;
}

qboolean PluQ_NNG_Backend_SendResource(const void *flatbuf, size_t size)
{
	if (!g_pluq_nng.initialized || !g_pluq_nng.is_backend)
		return false;

	int rv = nng_send(g_pluq_nng.resources_rep, (void*)flatbuf, size, 0);
	if (rv != 0) {
		fprintf(stderr, "PluQ_NNG: Failed to send resource: %s\n", nng_strerror(rv));
		return false;
	}

	return true;
}

qboolean PluQ_NNG_Frontend_RequestResource(uint32_t resource_id)
{
	if (!g_pluq_nng.initialized || !g_pluq_nng.is_frontend)
		return false;

	// TODO: Build FlatBuffer ResourceRequest message
	// For now, just send the resource_id as raw bytes
	int rv = nng_send(g_pluq_nng.resources_req, &resource_id, sizeof(resource_id), 0);
	if (rv != 0) {
		fprintf(stderr, "PluQ_NNG: Failed to request resource: %s\n", nng_strerror(rv));
		return false;
	}

	return true;
}

qboolean PluQ_NNG_Frontend_ReceiveResource(void **flatbuf_out, size_t *size_out)
{
	if (!g_pluq_nng.initialized || !g_pluq_nng.is_frontend)
		return false;

	void *buf = NULL;
	size_t size;

	int rv = nng_recv(g_pluq_nng.resources_req, &buf, &size, NNG_FLAG_ALLOC);
	if (rv != 0) {
		fprintf(stderr, "PluQ_NNG: Failed to receive resource: %s\n", nng_strerror(rv));
		return false;
	}

	*flatbuf_out = buf;
	*size_out = size;
	return true;
}

// ============================================================================
// GAMEPLAY CHANNEL (PUB/SUB)
// ============================================================================

qboolean PluQ_NNG_Backend_PublishFrame(const void *flatbuf, size_t size)
{
	if (!g_pluq_nng.initialized || !g_pluq_nng.is_backend)
		return false;

	int rv = nng_send(g_pluq_nng.gameplay_pub, (void*)flatbuf, size, NNG_FLAG_NONBLOCK);
	if (rv != 0 && rv != NNG_EAGAIN) {
		fprintf(stderr, "PluQ_NNG: Failed to publish frame: %s\n", nng_strerror(rv));
		return false;
	}

	return true;
}

qboolean PluQ_NNG_Frontend_ReceiveFrame(void **flatbuf_out, size_t *size_out)
{
	if (!g_pluq_nng.initialized || !g_pluq_nng.is_frontend)
		return false;

	void *buf = NULL;
	size_t size;

	int rv = nng_recv(g_pluq_nng.gameplay_sub, &buf, &size, NNG_FLAG_ALLOC | NNG_FLAG_NONBLOCK);
	if (rv == NNG_EAGAIN) {
		// No message available (normal for non-blocking)
		return false;
	}
	if (rv != 0) {
		fprintf(stderr, "PluQ_NNG: Failed to receive frame: %s\n", nng_strerror(rv));
		return false;
	}

	*flatbuf_out = buf;
	*size_out = size;
	return true;
}

// ============================================================================
// INPUT CHANNEL (PUSH/PULL)
// ============================================================================

qboolean PluQ_NNG_Frontend_SendInput(const void *flatbuf, size_t size)
{
	if (!g_pluq_nng.initialized || !g_pluq_nng.is_frontend)
		return false;

	int rv = nng_send(g_pluq_nng.input_push, (void*)flatbuf, size, NNG_FLAG_NONBLOCK);
	if (rv != 0 && rv != NNG_EAGAIN) {
		fprintf(stderr, "PluQ_NNG: Failed to send input: %s\n", nng_strerror(rv));
		return false;
	}

	return true;
}

qboolean PluQ_NNG_Backend_ReceiveInput(void **flatbuf_out, size_t *size_out)
{
	if (!g_pluq_nng.initialized || !g_pluq_nng.is_backend)
		return false;

	void *buf = NULL;
	size_t size;

	int rv = nng_recv(g_pluq_nng.input_pull, &buf, &size, NNG_FLAG_ALLOC | NNG_FLAG_NONBLOCK);
	if (rv == NNG_EAGAIN) {
		// No message available (normal for non-blocking)
		return false;
	}
	if (rv != 0) {
		fprintf(stderr, "PluQ_NNG: Failed to receive input: %s\n", nng_strerror(rv));
		return false;
	}

	*flatbuf_out = buf;
	*size_out = size;
	return true;
}
