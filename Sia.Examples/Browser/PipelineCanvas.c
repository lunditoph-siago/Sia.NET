#include <pthread.h>
#include <stdint.h>
#include <stdlib.h>

extern void siaEditorInitCanvasWorker(void);

uintptr_t siaEditorCanvasThread(void)
{
    siaEditorInitCanvasWorker();
    return (uintptr_t)pthread_self();
}

void siaEditorFree(void *pointer) { free(pointer); }
